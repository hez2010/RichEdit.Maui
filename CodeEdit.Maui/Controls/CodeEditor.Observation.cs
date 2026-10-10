using RichEdit.Maui;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    /// <summary>Requests native composition completion across views of this document without moving focus.</summary>
    /// <returns>False when the native input method must finish through its own interaction.</returns>
    public bool TryCompleteComposition() => TextView.TryCompleteComposition();

    /// <summary>Gets visible geometry and hit testing in this composed editor's coordinates.</summary>
    public RichTextLayout TextLayout { get; private set; } = null!;

    /// <summary>Gets authoritative native marked-text state.</summary>
    public RichTextCompositionState Composition => TextView.Composition;

    /// <summary>Gets provisional native source with an invalid edit revision, or null outside composition.</summary>
    public CodeDocumentSnapshot? CompositionSnapshot
    {
        get
        {
            var source = TextView.CompositionSnapshot;
            if (!ReferenceEquals(source, _compositionSource))
            {
                _compositionSource = source;
                _compositionSnapshot = source is null ? null : new(default, source.TextBuffer);
            }
            return _compositionSnapshot;
        }
    }

    private RichTextDocumentSnapshot? _compositionSource;
    private CodeDocumentSnapshot? _compositionSnapshot;

    /// <summary>Gets the native provisional selection, or null outside composition.</summary>
    public TextSelectionState? CompositionSelection => TextView.CompositionSelection;

    /// <summary>Occurs when native marked text starts, changes, or ends.</summary>
    public event EventHandler<RichTextCompositionChangedEventArgs>? CompositionChanged;

    /// <summary>Observes local pointer movement without changing native input behavior.</summary>
    public event EventHandler<RichTextPointerEventArgs>? PointerMoved;

    /// <summary>Observes local pointer presses without suppressing selection or focus.</summary>
    public event EventHandler<RichTextPointerEventArgs>? PointerPressed;

    /// <summary>Occurs when a hovering pointer exits the text surface.</summary>
    public event EventHandler? PointerExited;

    private void InitializeObservation()
    {
        TextLayout = TextView.TextLayout.CreateRelativeTo(this);
        TextView.SourcePointerInput = ObserveSelectionPointer;
        TextLayout.Changed += (_, _) => InvalidateGutter();
        TextView.CompositionChanged += (_, args) =>
        {
            OnPropertyChanged(nameof(CompositionSnapshot));
            OnPropertyChanged(nameof(CompositionSelection));
            CompositionChanged?.Invoke(this, args);
        };
        TextView.PointerMoved += (_, args) => PointerMoved?.Invoke(this, TranslatePointer(args));
        TextView.PointerPressed += (_, args) => PointerPressed?.Invoke(this, TranslatePointer(args));
        TextView.PointerExited += (_, _) => PointerExited?.Invoke(this, EventArgs.Empty);
    }

    private RichTextPointerEventArgs TranslatePointer(RichTextPointerEventArgs args) => new(
        new(args.Point.X + TextView.X + Content.X, args.Point.Y + TextView.Y + Content.Y), args.DeviceKind, args.Buttons, args.Modifiers);

    internal IReadOnlyList<VisibleCodeLine> GetVisibleLines()
    {
        var layout = TextLayout.CaptureInput();
        if (layout is null)
            return Array.Empty<VisibleCodeLine>();

        var result = new List<VisibleCodeLine>();
        var seen = new HashSet<int>();
        var lines = CompositionSnapshot?.Lines ?? Lines;
        foreach (var fragment in layout.Lines)
        {
            if (fragment.SourceRanges.Count == 0)
                continue;

            var number = lines.GetPosition(fragment.SourceRanges[0].Start).Line + 1;
            if (seen.Add(number))
                result.Add(new(number, (float)fragment.Bounds.Y, (float)fragment.Bounds.Height, (float)fragment.Baseline));
        }

        return result;
    }
}
