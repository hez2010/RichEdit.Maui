using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

// Application-owned analysis, request ordering, palette, and lifetime. Only public editor APIs are used.
internal sealed partial class CodeColorizer : IDisposable
{
    private readonly CodeEditor _editor;
    private readonly RichTextDecorationLayer _layer;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private bool _enabled = true;
    private Func<CodeToken, Color?>? _palette = DefaultPalette;
    private RichTextRevision _revision;
    internal Func<string, CancellationToken, Task<IReadOnlyList<CodeToken>>>? ClassifyAsync { get; set; }
    internal IReadOnlyList<CodeToken> Tokens { get; private set; } = Array.Empty<CodeToken>();
    internal int AnalysisRequests { get; private set; }
    internal event EventHandler<Exception>? Failed;

    internal CodeColorizer(CodeEditor editor)
    {
        _editor = editor;
        _layer = editor.Decorations.CreateLayer();
        editor.TextChanged += OnTextChanged;
        editor.DocumentChanged += OnDocumentChanged;
        editor.CompositionChanged += OnCompositionChanged;
        editor.Unloaded += OnUnloaded;
        editor.Loaded += OnLoaded;
        Schedule();
    }

    internal bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Schedule(); }
    }

    internal Func<CodeToken, Color?>? Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            if (_revision == _editor.Document.Revision) Publish(_revision, Tokens);
        }
    }

    internal static Color? DefaultPalette(CodeToken token) => token.Kind switch
    {
        CodeTokenKind.Keyword => Colors.Blue,
        CodeTokenKind.String => Colors.Maroon,
        CodeTokenKind.Comment => Colors.Green,
        CodeTokenKind.Number => Colors.DarkCyan,
        CodeTokenKind.Preprocessor => Colors.Purple,
        _ => null,
    };

    internal Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(TimeSpan.Zero, cancellationToken);

    private async Task RefreshAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pending?.Cancel();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pending = pending;
        var token = pending.Token;
        var snapshot = _editor.Document.CurrentSnapshot;
        var classify = ClassifyAsync;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            IReadOnlyList<CodeToken> tokens = Array.Empty<CodeToken>();
            if (Enabled)
            {
                AnalysisRequests++;
                tokens = classify is null
                    ? await Task.Run(() => new CSharpClassifier().Highlight(snapshot.Text, token), token)
                    : await classify(snapshot.Text, token);
            }
            // Leave source notifications before publishing or invoking application callbacks.
            await Task.Yield();
            if (token.IsCancellationRequested || _disposed || snapshot.Revision != _editor.Document.Revision) return;
            if (Publish(snapshot.Revision, tokens)) { Tokens = tokens; _revision = snapshot.Revision; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_pending, pending)) _pending = null; }
    }

    private bool Publish(RichTextRevision revision, IReadOnlyList<CodeToken> tokens)
    {
        var palette = Palette;
        var decorations = tokens.Select(token => (Token: token, Color: palette?.Invoke(token)))
            .Where(static item => item.Color is not null)
            .Select(static item => new RichTextDecoration(item.Token.Range, new() { ForegroundColor = item.Color })).ToArray();
        if (_disposed || !ReferenceEquals(Palette, palette)) return false;
        return _layer.TrySet(revision, decorations);
    }

    private async void Schedule()
    {
        if (_disposed) return;
        try { await RefreshAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None); }
        catch (Exception exception) { if (!_disposed) Failed?.Invoke(this, exception); }
    }
    private void OnTextChanged(object? sender, RichTextTextChangedEventArgs args) => Schedule();
    private void OnDocumentChanged(object? sender, CodeDocumentReplacedEventArgs args) { Tokens = Array.Empty<CodeToken>(); Schedule(); }
    private void OnLoaded(object? sender, EventArgs args) => Schedule();
    private void OnCompositionChanged(object? sender, RichTextCompositionChangedEventArgs args) { if (!args.Current.IsActive) Schedule(); }
    private void OnUnloaded(object? sender, EventArgs args) => _pending?.Cancel();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending?.Cancel();
        Tokens = Array.Empty<CodeToken>();
        _editor.TextChanged -= OnTextChanged;
        _editor.DocumentChanged -= OnDocumentChanged;
        _editor.CompositionChanged -= OnCompositionChanged;
        _editor.Unloaded -= OnUnloaded;
        _editor.Loaded -= OnLoaded;
        _layer.Dispose();
    }
}
