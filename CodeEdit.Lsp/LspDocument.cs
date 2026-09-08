using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeEdit.Lsp;

/// <summary>An immutable synchronized source revision.</summary>
/// <param name="Version">The LSP revision, independent of the editor's history revision.</param>
/// <param name="Text">The source text.</param>
public sealed record LspDocumentSnapshot(int Version, string Text);

/// <summary>Owns ordered LSP synchronization and version-bound requests for one document.</summary>
/// <remarks>Updates cancel requests for older revisions. Document notifications are ordered and are never canceled midway by a newer edit.</remarks>
public sealed class LspDocument : IAsyncDisposable
{
    private readonly LspClient _client;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _revision = new();
    private LspDocumentSnapshot _snapshot;
    private Task _tail = Task.CompletedTask;
    private Task? _closeTask;
    private Exception? _synchronizationFailure;

    internal LspDocument(LspClient client, Uri uri, string languageId, string text)
    {
        _client = client;
        Uri = uri;
        LanguageId = languageId;
        _snapshot = new(1, text);
        if (client.OpenClose)
            QueueNotification(() => client.Connection.NotifyAsync(LspMethods.DidOpen, new(new(uri.AbsoluteUri, languageId, 1, text)), _lifetime.Token));
    }

    /// <summary>Gets the document's stable URI.</summary>
    public Uri Uri { get; }
    /// <summary>Gets the document's standard LSP language identifier.</summary>
    public string LanguageId { get; }
    /// <summary>Gets the latest source revision, including queued changes.</summary>
    public LspDocumentSnapshot CurrentSnapshot { get { lock (_gate) return _snapshot; } }

    private TextDocumentIdentifier Identifier => new(Uri.AbsoluteUri);

    /// <summary>Queues a full or incremental change according to the negotiated synchronization mode.</summary>
    /// <param name="text">The new complete source.</param>
    /// <returns>The synchronization operation. A newer edit does not cancel this notification.</returns>
    public Task UpdateTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            CheckOpen();
            if (text == _snapshot.Text) return _tail;
            _revision.Cancel();
            _revision.Dispose();
            _revision = new();
            var previous = _snapshot;
            _snapshot = new(checked(previous.Version + 1), text);
            if (_client.ChangeKind == 0)
            {
                _synchronizationFailure = new NotSupportedException("The language server did not enable document change synchronization.");
                return Task.FromException(_synchronizationFailure);
            }
            var change = _client.ChangeKind == 2 ? LspText.CreateChange(previous.Text, text) : new TextDocumentContentChangeEvent(text);
            var parameters = new DidChangeTextDocumentParams(new(Uri.AbsoluteUri, _snapshot.Version), [change]);
            return QueueNotification(() => _client.Connection.NotifyAsync(LspMethods.DidChange, parameters, _lifetime.Token));
        }
    }

    /// <summary>Waits for queued synchronization and earlier requests to complete.</summary>
    /// <param name="cancellationToken">Cancels the wait without canceling document notifications.</param>
    /// <returns>The synchronization operation.</returns>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (_gate) { CheckOpen(); pending = _tail; }
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        CheckSynchronization();
    }

    /// <summary>Notifies the server after the application has successfully saved this source revision.</summary>
    /// <param name="version">The LSP revision actually saved. A stale save is rejected.</param>
    /// <returns>The save notification, if requested by the server.</returns>
    public Task NotifySavedAsync(int version)
    {
        lock (_gate)
        {
            CheckOpen();
            if (version != _snapshot.Version) throw new InvalidOperationException("The saved LSP revision is no longer current.");
            if (!_client.Save) return Task.CompletedTask;
            var parameters = new DidSaveTextDocumentParams(Identifier, _client.SaveText ? _snapshot.Text : null);
            return QueueNotification(() => _client.Connection.NotifyAsync(LspMethods.DidSave, parameters, _lifetime.Token));
        }
    }

    /// <summary>Requests semantic tokens for the current synchronized source.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Decoded semantic tokens, or an empty collection when unsupported.</returns>
    public async Task<IReadOnlyList<SemanticToken>> GetSemanticTokensAsync(CancellationToken cancellationToken = default)
    {
        LspDocumentSnapshot snapshot;
        Task<SemanticTokens?> request;
        lock (_gate)
        {
            CheckOpen();
            snapshot = _snapshot;
            if (_client.SemanticTokensLegend is null || !_client.FullSemanticTokens && !_client.RangeSemanticTokens) return [];
            request = _client.FullSemanticTokens
                ? RequestAsync(LspMethods.SemanticTokensFull, new(Identifier), cancellationToken)
                : RequestAsync(LspMethods.SemanticTokensRange, new(Identifier, new(new(0, 0), LspText.GetPosition(snapshot.Text, snapshot.Text.Length))), cancellationToken);
        }
        var response = await request.ConfigureAwait(false);
        return response is null ? [] : LspText.DecodeSemanticTokens(snapshot.Text, response, _client.SemanticTokensLegend!, cancellationToken);
    }

    /// <summary>Requests completion items at a UTF-16 position.</summary>
    /// <param name="position">The source position.</param>
    /// <param name="context">An optional completion context.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The completion list, or null if unsupported.</returns>
    public Task<CompletionList?> GetCompletionsAsync(LspPosition position, JsonElement? context = null, CancellationToken cancellationToken = default) =>
        _client.Supports("completionProvider") ? RequestAsync(LspMethods.Completion, new(Identifier, position, context), cancellationToken) : Task.FromResult<CompletionList?>(null);

    /// <summary>Resolves a completion item using its preserved server data.</summary>
    /// <param name="item">The original item.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The resolved item.</returns>
    public Task<CompletionItem> ResolveCompletionAsync(CompletionItem item, CancellationToken cancellationToken = default) =>
        _client.ServerCapabilities.TryGetProperty("completionProvider", out var completion) && completion.ValueKind == JsonValueKind.Object &&
        completion.TryGetProperty("resolveProvider", out var resolve) && resolve.ValueKind == JsonValueKind.True
            ? RequestAsync(LspMethods.ResolveCompletion, item, cancellationToken) : Task.FromResult(item);

    /// <summary>Requests hover information.</summary>
    /// <param name="position">The source position.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The hover result, or null if unsupported.</returns>
    public Task<Hover?> GetHoverAsync(LspPosition position, CancellationToken cancellationToken = default) =>
        _client.Supports("hoverProvider") ? RequestAsync(LspMethods.Hover, new(Identifier, position), cancellationToken) : Task.FromResult<Hover?>(null);

    /// <summary>Requests definition locations or links.</summary>
    /// <param name="position">The source position.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The complete standard LSP definition result, or null if unsupported.</returns>
    public Task<JsonElement?> GetDefinitionAsync(LspPosition position, CancellationToken cancellationToken = default) =>
        _client.Supports("definitionProvider") ? RequestAsync(LspMethods.Definition, new(Identifier, position), cancellationToken) : Task.FromResult<JsonElement?>(null);

    /// <summary>Requests references to a symbol.</summary>
    /// <param name="position">The source position.</param>
    /// <param name="includeDeclaration">Whether to include the declaration.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The locations, or null if unsupported.</returns>
    public Task<LspLocation[]?> GetReferencesAsync(LspPosition position, bool includeDeclaration = true, CancellationToken cancellationToken = default) =>
        _client.Supports("referencesProvider") ? RequestAsync(LspMethods.References, new(Identifier, position, new(includeDeclaration)), cancellationToken) : Task.FromResult<LspLocation[]?>(null);

    /// <summary>Requests source formatting without applying edits.</summary>
    /// <param name="tabSize">The indentation width.</param>
    /// <param name="insertSpaces">Whether to use spaces.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Edits in the requested source revision, or null if unsupported.</returns>
    public Task<LspTextEdit[]?> FormatAsync(int tabSize = 4, bool insertSpaces = true, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tabSize);
        return _client.Supports("documentFormattingProvider")
            ? RequestAsync(LspMethods.Formatting, new(Identifier, new JsonObject { ["tabSize"] = tabSize, ["insertSpaces"] = insertSpaces }), cancellationToken)
            : Task.FromResult<LspTextEdit[]?>(null);
    }

    /// <summary>Requests a rename. The application owns review and application of the returned WorkspaceEdit.</summary>
    /// <param name="position">The symbol position.</param>
    /// <param name="newName">The requested name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The complete WorkspaceEdit, or null if unsupported.</returns>
    public Task<JsonElement?> RenameAsync(LspPosition position, string newName, CancellationToken cancellationToken = default) =>
        _client.Supports("renameProvider") ? RequestAsync(LspMethods.Rename, new(Identifier, position, newName), cancellationToken) : Task.FromResult<JsonElement?>(null);

    /// <summary>Sends any typed request after synchronization, canceling it when this revision is superseded.</summary>
    /// <typeparam name="TParams">The standard or extension parameter type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="request">The method and source-generated JSON metadata.</param>
    /// <param name="parameters">The complete parameters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server response.</returns>
    public Task<TResult> RequestAsync<TParams, TResult>(LspRequest<TParams, TResult> request, TParams parameters, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            CheckOpen();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _revision.Token, _lifetime.Token);
            return Queue(async () =>
            {
                using (linked)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    CheckSynchronization();
                    var result = await _client.Connection.RequestAsync(request, parameters, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    return result;
                }
            });
        }
    }

    /// <summary>Sends any LSP document feature, adding this session's textDocument identifier.</summary>
    /// <param name="method">The standard or extension method name.</param>
    /// <param name="parameters">Other parameters such as position, range, context, or options. The object is copied.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The full LSP result.</returns>
    public Task<JsonElement?> RequestAsync(string method, JsonObject? parameters = null, CancellationToken cancellationToken = default)
    {
        var payload = parameters?.DeepClone().AsObject() ?? new JsonObject();
        payload["textDocument"] = new JsonObject { ["uri"] = Uri.AbsoluteUri };
        return RequestAsync(new LspRequest<JsonObject, JsonElement?>(method, LspJsonContext.Default.JsonObject, LspJsonContext.Default.NullableJsonElement), payload, cancellationToken);
    }

    private Task QueueNotification(Func<Task> notify) => Queue(async () =>
    {
        try { CheckSynchronization(); await notify().ConfigureAwait(false); }
        catch (Exception exception) { _synchronizationFailure = exception; throw; }
        return true;
    });

    private Task<T> Queue<T>(Func<Task<T>> operation)
    {
        var previous = _tail;
        var task = Task.Run(async () => { await previous.ConfigureAwait(false); return await operation().ConfigureAwait(false); });
        _tail = ObserveAsync(task);
        return task;
    }

    private static async Task ObserveAsync(Task task)
    {
        // The originating caller receives request errors. Subsequent requests and didClose must still run.
        try { await task.ConfigureAwait(false); }
        catch { }
    }

    private void CheckSynchronization()
    {
        if (_synchronizationFailure is { } failure) throw new InvalidOperationException("The document could not be synchronized with the language server.", failure);
    }
    private void CheckOpen() => ObjectDisposedException.ThrowIf(_closeTask is not null, this);

    /// <summary>Cancels pending requests and sends didClose after queued notifications.</summary>
    /// <returns>The close operation. The shared client remains open.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closeTask is not null) return new(_closeTask);
            _revision.Cancel();
            return new(_closeTask = Queue(async () =>
            {
                try
                {
                    if (_client.OpenClose) await _client.Connection.NotifyAsync(LspMethods.DidClose, new(Identifier), _lifetime.Token).ConfigureAwait(false);
                    return true;
                }
                finally { _lifetime.Cancel(); _client.Closed(this); }
            }));
        }
    }
}
