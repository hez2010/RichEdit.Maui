using CodeEdit.Lsp;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>Details of an automatic language-server failure.</summary>
/// <param name="exception">The connection, synchronization, or presentation failure.</param>
public sealed class CodeLanguageServerFailedEventArgs(Exception exception) : EventArgs
{
    /// <summary>Gets the failure. Explicitly awaited operations propagate errors to their caller.</summary>
    public Exception Exception { get; } = exception;
}

public sealed partial class CodeEditor
{
    /// <summary>Identifies <see cref="LanguageServer"/>.</summary>
    public static readonly BindableProperty LanguageServerProperty = BindableProperty.Create(
        nameof(LanguageServer), typeof(LspClient), typeof(CodeEditor), null,
        coerceValue: static (view, value) => { ((CodeEditor)view).VerifyAccess(); return value; },
        propertyChanged: static (view, _, _) =>
        {
            var editor = (CodeEditor)view;
            editor.ResetLanguageDocument();
            editor.ScheduleHighlighting();
        });

    private LspDocument? _languageDocument;
    private CodeDocument? _languageSource;
    private LspClient? _subscribedLanguageServer;
    private Task _closingLanguageDocument = Task.CompletedTask;
    private IReadOnlyList<LspDiagnostic> _diagnostics = Array.Empty<LspDiagnostic>();

    /// <summary>Gets or sets the initialized, application-owned LSP client, or null for plain editing.</summary>
    /// <remarks>Clients can be shared. Disconnecting the editor closes its document; it does not dispose the client.</remarks>
    public LspClient? LanguageServer { get => (LspClient?)GetValue(LanguageServerProperty); set { VerifyAccess(); SetValue(LanguageServerProperty, value); } }
    /// <summary>Gets diagnostics for the current source. New edits clear this collection until the server publishes again.</summary>
    public IReadOnlyList<LspDiagnostic> Diagnostics => _diagnostics;
    /// <summary>Occurs on the UI thread when current-document diagnostics change.</summary>
    public event EventHandler? DiagnosticsChanged;
    /// <summary>Occurs on the UI thread when automatic synchronization, highlighting, or document closing fails.</summary>
    public event EventHandler<CodeLanguageServerFailedEventArgs>? LanguageServerFailed;

    /// <summary>Opens or synchronizes this editor's document and returns its version-aware LSP request API.</summary>
    /// <param name="cancellationToken">Cancels the wait, without dropping queued document notifications.</param>
    /// <returns>The editor-owned session, or null when no server is attached. Call on the UI thread.</returns>
    /// <remarks>Use this session for requests. Change source through CodeDocument; the editor owns session updates and disposal.</remarks>
    public async Task<LspDocument?> GetLanguageDocumentAsync(CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        var client = LanguageServer;
        var document = Document;
        await _closingLanguageDocument.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (client is null || !ReferenceEquals(client, LanguageServer) || !ReferenceEquals(document, Document)) return null;
        if (_languageDocument is null)
        {
            _languageDocument = client.OpenDocument(document.Uri, document.LanguageId, document.Text);
            _languageSource = document;
            _subscribedLanguageServer = client;
            client.DiagnosticsPublished += OnDiagnosticsPublished;
            client.SemanticTokensRefreshRequested += OnSemanticTokensRefresh;
        }
        var session = _languageDocument;
        await session.UpdateTextAsync(document.Text).WaitAsync(cancellationToken);
        await session.SynchronizeAsync(cancellationToken);
        return ReferenceEquals(session, _languageDocument) ? session : null;
    }

    /// <summary>Converts a source offset to LSP's zero-based UTF-16 coordinates.</summary>
    /// <param name="offset">The UTF-16 source offset.</param>
    /// <returns>The LSP position.</returns>
    public LspPosition GetLspPosition(int offset) => LspText.GetPosition(Document.Text, offset);

    /// <summary>Converts LSP coordinates to a source offset.</summary>
    /// <param name="position">The LSP position in the current source.</param>
    /// <returns>The UTF-16 source offset.</returns>
    public int GetOffset(LspPosition position) => LspText.GetOffset(Document.Text, position);

    /// <summary>Applies LSP text edits as one undoable source transaction if the requested snapshot is still current.</summary>
    /// <param name="edits">Non-overlapping edits in the original snapshot's coordinates.</param>
    /// <param name="snapshot">The exact CodeDocument snapshot captured before the server request.</param>
    /// <param name="description">The undo description.</param>
    /// <returns>Whether edits were applied. Stale snapshots, read-only state, composition, and length limits prevent application.</returns>
    public bool ApplyLanguageServerEdits(IEnumerable<LspTextEdit> edits, CodeDocumentSnapshot snapshot, string description = "Language server edit")
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ReferenceEquals(Document.CurrentSnapshot, snapshot) || IsReadOnly || NativeAdapter?.IsComposing == true) return false;
        var mapped = edits.Select(edit =>
        {
            var start = LspText.GetOffset(snapshot.Text, edit.Range.Start);
            var end = LspText.GetOffset(snapshot.Text, edit.Range.End);
            if (end < start) throw new ArgumentException("LSP edit ranges must have an ordered start and end.", nameof(edits));
            return new CodeTextEdit(new(start, end - start), edit.NewText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        }).OrderBy(edit => edit.Range.Start).ToArray();
        var previousEnd = 0;
        foreach (var edit in mapped)
        {
            if (edit.Range.Start < previousEnd) throw new ArgumentException("LSP edits must not overlap.", nameof(edits));
            previousEnd = edit.Range.End;
        }
        if (!ReferenceEquals(Document.CurrentSnapshot, snapshot)) return false;
        return ApplyEdits(mapped, description);
    }

    private void SynchronizeLanguageDocument()
    {
        SetDiagnostics([]);
        if (_languageDocument is { } session && ReferenceEquals(_languageSource, Document))
            _ = ObserveLanguageUpdateAsync(session, session.UpdateTextAsync(Document.Text));
    }

    private async Task ObserveLanguageUpdateAsync(LspDocument session, Task update)
    {
        try { await update; }
        catch (Exception exception)
        {
            if (ReferenceEquals(session, _languageDocument)) LanguageServerFailed?.Invoke(this, new(exception));
        }
    }

    private void ResetLanguageDocument()
    {
        CancelHighlighting();
        if (_subscribedLanguageServer is { } client)
        {
            client.DiagnosticsPublished -= OnDiagnosticsPublished;
            client.SemanticTokensRefreshRequested -= OnSemanticTokensRefresh;
            _subscribedLanguageServer = null;
        }
        if (_languageDocument is { } document)
        {
            _languageDocument = null;
            _languageSource = null;
            _closingLanguageDocument = CloseLanguageDocumentAsync(document);
        }
        _tokens = Array.Empty<SemanticToken>();
        OnPropertyChanged(nameof(Tokens));
        _syntaxLayer.Clear();
        SetDiagnostics([]);
    }

    private async Task CloseLanguageDocumentAsync(LspDocument document)
    {
        try { await document.DisposeAsync(); }
        catch (Exception exception) { LanguageServerFailed?.Invoke(this, new(exception)); }
    }

    private void OnDiagnosticsPublished(object? sender, PublishDiagnosticsParams parameters)
    {
        Dispatcher.Dispatch(() =>
        {
            if (!ReferenceEquals(sender, _subscribedLanguageServer) || _languageDocument is not { } session ||
                parameters.Uri != Document.Uri.AbsoluteUri || !ReferenceEquals(_languageSource, Document)) return;
            var snapshot = session.CurrentSnapshot;
            if (snapshot.Text != Document.Text || parameters.Version is { } version && version != snapshot.Version) return;
            SetDiagnostics(Array.AsReadOnly(parameters.Diagnostics.ToArray()));
        });
    }

    private void OnSemanticTokensRefresh(object? sender, EventArgs args) => Dispatcher.Dispatch(() =>
    {
        if (ReferenceEquals(sender, _subscribedLanguageServer)) ScheduleHighlighting();
    });

    private void SetDiagnostics(IReadOnlyList<LspDiagnostic> diagnostics)
    {
        if (_diagnostics.Count == 0 && diagnostics.Count == 0) return;
        _diagnostics = diagnostics;
        OnPropertyChanged(nameof(Diagnostics));
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }
}
