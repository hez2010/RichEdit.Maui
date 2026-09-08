using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeEdit.Lsp;

/// <summary>An application-owned LSP client shared by one or more documents.</summary>
/// <remarks>The application supplies the connection, including in-process or remote delivery. All serialization uses compile-time metadata.</remarks>
public sealed class LspClient : IAsyncDisposable
{
    private readonly LspClientOptions _options;
    private readonly List<IDisposable> _registrations = [];
    private readonly Dictionary<string, LspDocument> _documents = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Func<LanguageServerRequest, CancellationToken, Task<JsonElement?>>? _previousRequestHandler;
    private Task? _disposeTask;
    private bool _initialized;

    private LspClient(LspConnection connection, LspClientOptions options)
    {
        Connection = connection;
        _options = options;
        _previousRequestHandler = connection.RequestHandler;
        connection.RequestHandler = HandleRequestAsync;
        if (options.NotificationHandler is { } notificationHandler)
            connection.NotificationReceived += OnNotification;
        _registrations.Add(connection.OnNotification(LspMethods.PublishDiagnostics, (parameters, _) =>
        {
            DiagnosticsPublished?.Invoke(this, parameters);
            return Task.CompletedTask;
        }));
        _registrations.Add(connection.OnRequest(LspMethods.SemanticTokensRefresh, (_, _) =>
        {
            SemanticTokensRefreshRequested?.Invoke(this, EventArgs.Empty);
            return Task.FromResult<JsonElement?>(null);
        }));
    }

    /// <summary>Gets the endpoint for any additional typed, raw, or extension LSP methods.</summary>
    public LspConnection Connection { get; }
    /// <summary>Gets the complete capabilities negotiated with the server.</summary>
    public JsonElement ServerCapabilities { get; private set; }
    /// <summary>Gets the server's optional identity.</summary>
    public LspImplementationInfo? ServerInfo { get; private set; }
    /// <summary>Gets the semantic-token legend, or null when semantic tokens are unavailable.</summary>
    public SemanticTokensLegend? SemanticTokensLegend { get; private set; }
    /// <summary>Occurs on a background thread when the server publishes diagnostics.</summary>
    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;
    /// <summary>Occurs on a background thread when the server requests fresh semantic tokens.</summary>
    public event EventHandler? SemanticTokensRefreshRequested;

    internal int ChangeKind { get; private set; }
    internal bool OpenClose { get; private set; }
    internal bool Save { get; private set; }
    internal bool SaveText { get; private set; }
    internal bool FullSemanticTokens { get; private set; }
    internal bool RangeSemanticTokens { get; private set; }

    /// <summary>Initializes a client on an application-provided connection.</summary>
    /// <param name="connection">An application-implemented endpoint with any application handlers already registered.</param>
    /// <param name="options">Workspace, capability, and callback settings.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>An initialized client ready to open documents.</returns>
    public static async Task<LspClient> ConnectAsync(LspConnection connection, LspClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        options ??= new();
        if (options.RootUri is { IsAbsoluteUri: false }) throw new ArgumentException("The workspace root must be an absolute URI.", nameof(options));
        var client = new LspClient(connection, options);
        try
        {
            var root = options.RootUri?.AbsoluteUri;
            var result = await connection.RequestAsync(LspMethods.Initialize,
                new(BuildCapabilities(options.Capabilities), Environment.ProcessId, new("CodeEdit.Lsp"), root, options.InitializationOptions,
                    root is null ? null : [new(root, Path.GetFileName(options.RootUri!.LocalPath.TrimEnd('/', '\\')))]), cancellationToken).ConfigureAwait(false);
            client.ReadCapabilities(result);
            await connection.NotifyAsync(LspMethods.Initialized, JsonSerializer.SerializeToElement(new JsonObject(), LspJsonContext.Default.JsonObject), cancellationToken).ConfigureAwait(false);
            client._initialized = true;
            return client;
        }
        catch
        {
            client.Unregister();
            if (!options.LeaveConnectionOpen) await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Opens an independently synchronized document.</summary>
    /// <param name="uri">A stable, absolute document URI. Only one open document per URI is allowed on this client.</param>
    /// <param name="languageId">The standard LSP language identifier, such as csharp or python.</param>
    /// <param name="text">The initial source.</param>
    /// <returns>A document session. Dispose it to send didClose; the shared server remains running.</returns>
    public LspDocument OpenDocument(Uri uri, string languageId, string text)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        if (!uri.IsAbsoluteUri) throw new ArgumentException("The document URI must be absolute.", nameof(uri));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_documents.ContainsKey(uri.AbsoluteUri)) throw new InvalidOperationException($"'{uri}' already has an open LSP document.");
            var document = new LspDocument(this, uri, languageId, text);
            _documents.Add(uri.AbsoluteUri, document);
            return document;
        }
    }

    /// <summary>Checks a top-level server capability, such as hoverProvider or completionProvider.</summary>
    /// <param name="capability">The standard capability property name.</param>
    /// <returns>Whether the capability is enabled or has an options object.</returns>
    public bool Supports(string capability) => ServerCapabilities.TryGetProperty(capability, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.Object;

    internal void Closed(LspDocument document)
    {
        lock (_gate) _documents.Remove(document.Uri.AbsoluteUri);
    }

    private void ReadCapabilities(InitializeResult result)
    {
        ServerCapabilities = JsonSerializer.SerializeToElement(result.Capabilities, LspJsonContext.Default.JsonObject);
        ServerInfo = result.ServerInfo;
        if (ServerCapabilities.TryGetProperty("positionEncoding", out var encoding) && encoding.GetString() is not (null or "utf-16"))
            throw new NotSupportedException("CodeEdit.Lsp advertises and supports UTF-16 positions only.");
        if (ServerCapabilities.TryGetProperty("textDocumentSync", out var synchronization))
        {
            if (synchronization.ValueKind == JsonValueKind.Number)
            {
                ChangeKind = synchronization.GetInt32();
                OpenClose = ChangeKind != 0;
            }
            else if (synchronization.ValueKind == JsonValueKind.Object)
            {
                OpenClose = synchronization.TryGetProperty("openClose", out var openClose) && openClose.ValueKind == JsonValueKind.True;
                ChangeKind = synchronization.TryGetProperty("change", out var change) ? change.GetInt32() : 0;
                if (synchronization.TryGetProperty("save", out var save))
                {
                    Save = save.ValueKind is JsonValueKind.True or JsonValueKind.Object;
                    SaveText = save.ValueKind == JsonValueKind.Object && save.TryGetProperty("includeText", out var includeText) && includeText.ValueKind == JsonValueKind.True;
                }
            }
            if (ChangeKind is < 0 or > 2) throw new InvalidDataException("Unknown LSP textDocumentSync kind.");
        }
        if (ServerCapabilities.TryGetProperty("semanticTokensProvider", out var semantic) && semantic.ValueKind == JsonValueKind.Object)
        {
            SemanticTokensLegend = semantic.GetProperty("legend").Deserialize(LspJsonContext.Default.SemanticTokensLegend);
            FullSemanticTokens = semantic.TryGetProperty("full", out var full) && full.ValueKind is JsonValueKind.True or JsonValueKind.Object;
            RangeSemanticTokens = semantic.TryGetProperty("range", out var range) && range.ValueKind is JsonValueKind.True or JsonValueKind.Object;
        }
    }

    private static JsonObject BuildCapabilities(JsonObject? additions)
    {
        var capabilities = JsonNode.Parse("""
            {
              "general": { "positionEncodings": ["utf-16"] },
              "workspace": { "configuration": true, "semanticTokens": { "refreshSupport": true } },
              "textDocument": {
                "synchronization": { "dynamicRegistration": false, "didSave": true },
                "publishDiagnostics": { "versionSupport": true, "relatedInformation": true, "dataSupport": true },
                "semanticTokens": {
                  "dynamicRegistration": false, "requests": { "full": true, "range": true },
                  "tokenTypes": ["namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter", "variable", "property", "enumMember", "event", "function", "method", "macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator", "decorator"],
                  "tokenModifiers": ["declaration", "definition", "readonly", "static", "deprecated", "abstract", "async", "modification", "documentation", "defaultLibrary"],
                  "formats": ["relative"], "multilineTokenSupport": false, "overlappingTokenSupport": false,
                  "serverCancelSupport": false, "augmentsSyntaxTokens": false
                },
                "completion": { "dynamicRegistration": false, "completionItem": { "snippetSupport": false, "documentationFormat": ["plaintext", "markdown"] } },
                "hover": { "dynamicRegistration": false, "contentFormat": ["plaintext", "markdown"] },
                "signatureHelp": { "dynamicRegistration": false },
                "definition": { "dynamicRegistration": false, "linkSupport": true },
                "declaration": { "dynamicRegistration": false, "linkSupport": true },
                "typeDefinition": { "dynamicRegistration": false, "linkSupport": true },
                "implementation": { "dynamicRegistration": false, "linkSupport": true },
                "references": { "dynamicRegistration": false },
                "documentSymbol": { "dynamicRegistration": false, "hierarchicalDocumentSymbolSupport": true },
                "formatting": { "dynamicRegistration": false },
                "rename": { "dynamicRegistration": false },
                "foldingRange": { "dynamicRegistration": false }
              }
            }
            """)!.AsObject();
        if (additions is not null) Merge(capabilities, additions);
        capabilities["general"]!["positionEncodings"] = new JsonArray("utf-16");
        // The editor's synchronization and token renderer use these fixed protocol contracts.
        capabilities["textDocument"]!["synchronization"]!["dynamicRegistration"] = false;
        var semantic = capabilities["textDocument"]!["semanticTokens"]!;
        semantic["dynamicRegistration"] = false;
        semantic["multilineTokenSupport"] = false;
        semantic["overlappingTokenSupport"] = false;
        return capabilities;
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
            if (value is JsonObject sourceObject && target[key] is JsonObject targetObject) Merge(targetObject, sourceObject);
            else target[key] = value?.DeepClone();
    }

    private Task<JsonElement?> HandleRequestAsync(LanguageServerRequest request, CancellationToken token)
    {
        if ((_options.RequestHandler ?? _previousRequestHandler) is { } handler) return handler(request, token);
        JsonElement? result = request.Method switch
        {
            "workspace/configuration" => JsonSerializer.SerializeToElement(new JsonArray(
                Enumerable.Repeat<JsonNode?>(null, request.Parameters!.Value.GetProperty("items").GetArrayLength()).ToArray()),
                LspJsonContext.Default.JsonArray),
            "window/showMessageRequest" => null,
            "workspace/applyEdit" => JsonSerializer.SerializeToElement(new JsonObject
                { ["applied"] = false, ["failureReason"] = "The application has not registered a workspace edit handler." }, LspJsonContext.Default.JsonObject),
            _ => throw new LanguageServerException(-32601, $"No application handler is registered for '{request.Method}'."),
        };
        return Task.FromResult(result);
    }

    private void OnNotification(object? sender, LanguageServerNotification notification) => _options.NotificationHandler!(notification);

    /// <summary>Closes documents, requests shutdown, sends exit, and disposes the owned connection.</summary>
    /// <returns>The cleanup operation. Shutdown is bounded to five seconds.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        // Leave the lock and ensure the disposed state is visible before invoking callbacks.
        await Task.Yield();
        try
        {
            LspDocument[] documents;
            lock (_gate) documents = [.. _documents.Values];
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var document in documents) await document.DisposeAsync().AsTask().WaitAsync(cancellation.Token).ConfigureAwait(false);
            if (_initialized)
            {
                await Connection.RequestAsync(LspMethods.Shutdown, null, cancellation.Token).ConfigureAwait(false);
                await Connection.NotifyAsync(LspMethods.Exit, null, cancellation.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            Unregister();
            if (!_options.LeaveConnectionOpen) await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Unregister()
    {
        foreach (var registration in _registrations) registration.Dispose();
        Connection.NotificationReceived -= OnNotification;
        Connection.RequestHandler = _previousRequestHandler;
    }
}
