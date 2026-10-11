using RichEdit.Core;
using ReviewTextSelection = RichEdit.Core.RichTextSelection;

namespace RichEdit.Maui;

/// <summary>A selectable native, read-only original or final projection of a rich document.</summary>
/// <remarks>The source document owns all changes and history. The private native renderer receives
/// a derived snapshot with editing and history disabled. Accept or reject through the source document.</remarks>
public sealed partial class RichReviewView : ContentView
{
    private readonly RichEditor _renderer = new() { IsReadOnly = true };
    private RichDocument? _source;
    private EventHandler<RichDocumentChangedEventArgs>? _observer;
    private bool _refreshQueued;

    /// <summary>Identifies the canonical source document.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(nameof(Source), typeof(RichDocument), typeof(RichReviewView),
        defaultValueCreator: _ => new RichDocument(), validateValue: (_, value) => value is RichDocument,
        propertyChanged: (view, _, after) => ((RichReviewView)view).Attach((RichDocument)after));
    /// <summary>Identifies the original or final review state.</summary>
    public static readonly BindableProperty ModeProperty = BindableProperty.Create(nameof(Mode), typeof(RichReviewMode), typeof(RichReviewView), RichReviewMode.Final,
        validateValue: (_, value) => value is RichReviewMode mode && Enum.IsDefined(mode),
        propertyChanged: (view, _, _) => ((RichReviewView)view).Refresh());

    /// <summary>Creates a native review surface over an empty source document.</summary>
    public RichReviewView()
    {
        Content = _renderer;
        _renderer.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        Attach((RichDocument)GetValue(SourceProperty));
    }

    /// <summary>Gets or sets the canonical document. Review refreshes never change this document's saved state or history.</summary>
    public RichDocument Source { get => (RichDocument)GetValue(SourceProperty); set { ArgumentNullException.ThrowIfNull(value); SetValue(SourceProperty, value); } }
    /// <summary>Gets or sets the read-only review state.</summary>
    public RichReviewMode Mode { get => (RichReviewMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    /// <summary>Gets the displayed projection and its canonical address maps.</summary>
    public RichReviewProjection Projection { get; private set; } = null!;
    /// <summary>Gets or sets selection in the displayed projection's rich coordinates.</summary>
    public RichSelection Selection { get => _renderer.Selection; set => _renderer.Selection = value; }
    /// <summary>Gets native geometry for the displayed review state.</summary>
    public RichEditorLayout TextLayout => _renderer.TextLayout;
    /// <summary>Gets or sets a fallback font family for unresolved authored text.</summary>
    public string? FontFamily { get => _renderer.FontFamily; set => _renderer.FontFamily = value; }
    /// <summary>Gets or sets a fallback font size in points.</summary>
    public double? FontSize { get => _renderer.FontSize; set => _renderer.FontSize = value; }
    /// <summary>Gets or sets a fallback text color.</summary>
    public Color? TextColor { get => _renderer.TextColor; set => _renderer.TextColor = value; }
    /// <summary>Occurs after the review snapshot and native rendering source have been replaced.</summary>
    public event EventHandler? ProjectionChanged;
    /// <summary>Occurs when the displayed projection's selection changes.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Selects all displayed body content.</summary>
    public void SelectAll() => _renderer.SelectAll();
    /// <summary>Copies displayed content through the RTF clipboard path with ordinary text fallback.</summary>
    public Task CopyAsync() => _renderer.CopyAsync();
    /// <summary>Focuses the native selectable review surface.</summary>
    public new bool Focus() => _renderer.Focus();

    /// <summary>Synchronously captures the latest source revision. Source changes also queue a coalesced refresh.</summary>
    public void Refresh()
    {
        _renderer.NativeView.VerifyAccess();
        var source = Source.CurrentSnapshot;
        if (Projection is { } existing && ReferenceEquals(existing.Source, source) && existing.Mode == Mode) return;
        var prior = Projection;
        var selection = prior is not null && ReferenceEquals(prior.Source, source) ? _renderer.Selection as ReviewTextSelection : null;
        var projection = source.CreateReviewProjection(Mode);
        Projection = projection;
        // This adapter owns no authored state: each source change replaces it,
        // and no native command can edit or create an undo unit on the snapshot.
        _renderer.Document = new(projection.Snapshot) { HistoryLimit = 0 };
        _renderer.IsReadOnly = true;
        if (selection is not null && prior!.ToSource(selection.Anchor) is { } sourceAnchor && prior.ToSource(selection.Active) is { } sourceActive &&
            projection.ToReview(sourceAnchor) is { } anchor && projection.ToReview(sourceActive) is { } active)
            _renderer.Selection = new ReviewTextSelection(anchor, active);
        OnPropertyChanged(nameof(Projection));
        ProjectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Attach(RichDocument source)
    {
        _renderer.NativeView.VerifyAccess();
        if (_source is not null) _source.Changed -= _observer;
        _source = source;
        var weak = new WeakReference<RichReviewView>(this);
        EventHandler<RichDocumentChangedEventArgs>? observer = null;
        observer = (_, _) =>
        {
            if (!weak.TryGetTarget(out var view)) { source.Changed -= observer; return; }
            if (view._refreshQueued) return;
            view._refreshQueued = true;
            view.Dispatcher.Dispatch(() => { view._refreshQueued = false; view.Refresh(); });
        };
        source.Changed += _observer = observer;
        Refresh();
    }
}
