using RichEdit.Maui;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    /// <summary>Gets visible geometry and hit testing in this composed editor's coordinates.</summary>
    public RichTextLayout TextLayout { get; private set; } = null!;
    /// <summary>Gets authoritative native marked-text state.</summary>
    public RichTextCompositionState Composition => TextView.Composition;
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
        TextLayout.Changed += (_, _) => InvalidateGutter();
        TextView.CompositionChanged += (_, args) => CompositionChanged?.Invoke(this, args);
        TextView.PointerMoved += (_, args) => PointerMoved?.Invoke(this, TranslatePointer(args));
        TextView.PointerPressed += (_, args) => PointerPressed?.Invoke(this, TranslatePointer(args));
        TextView.PointerExited += (_, _) => PointerExited?.Invoke(this, EventArgs.Empty);
    }

    private RichTextPointerEventArgs TranslatePointer(RichTextPointerEventArgs args) => new(
        new(args.Point.X + TextView.X + Content.X, args.Point.Y + TextView.Y + Content.Y), args.DeviceKind, args.Buttons, args.Modifiers);

    internal IReadOnlyList<VisibleCodeLine> GetVisibleLines()
    {
        var layout = TextLayout.Capture();
        if (layout is null) return Array.Empty<VisibleCodeLine>();
        var result = new List<VisibleCodeLine>();
        var seen = new HashSet<int>();
        foreach (var fragment in layout.Lines)
        {
            if (fragment.SourceRanges.Count == 0) continue;
            var number = Lines.GetPosition(fragment.SourceRanges[0].Start).Line;
            if (seen.Add(number)) result.Add(new(number, (float)fragment.Bounds.Y, (float)fragment.Bounds.Height));
        }
        return result;
    }
}
