using RichEdit.Core;

namespace RichEdit.Maui;

/// <summary>A native layout capture paired with the exact rich snapshot and its explicit text projection.</summary>
public sealed class RichEditorLayoutSnapshot
{
    internal RichEditorLayoutSnapshot(RichEditorLayout owner, RichTextLayoutSnapshot native, RichTextProjection projection)
    { Owner = owner; Native = native; TextProjection = projection; }
    internal RichEditorLayout Owner { get; }
    internal RichTextLayoutSnapshot Native { get; }
    /// <summary>Gets the snapshot-bound text projection used by the visible line ranges.</summary>
    public RichTextProjection TextProjection { get; }
    /// <summary>Gets whether the capture includes provisional marked text.</summary>
    public bool IsProvisional => Native.IsProvisional;
    /// <summary>Gets the native layout generation.</summary>
    public long LayoutVersion => Native.LayoutVersion;
    /// <summary>Gets the visible editor rectangle in control coordinates.</summary>
    public Rect Viewport => Native.Viewport;
    /// <summary>Gets visible native lines. Their ranges address TextProjection, never an independently mutable string.</summary>
    public IReadOnlyList<RichTextVisualLine> Lines => Native.Lines;
}

/// <summary>A native geometry hit mapped to a typed rich text point.</summary>
public sealed record RichEditorHit(RichTextPoint Point, RichTextHitKind Kind);

/// <summary>Queries native rich-text geometry without moving the caret or manufacturing structural text.</summary>
public sealed class RichEditorLayout
{
    private readonly RichEditor _editor;
    private readonly NativeEditorView _view;
    private readonly RichTextLayout _layout;
    internal RichEditorLayout(RichEditor editor, NativeEditorView view)
    { _editor = editor; _view = view; _layout = view.TextLayout.CreateRelativeTo(editor); }
    /// <summary>Occurs when geometry, appearance, source, or viewport changes.</summary>
    public event EventHandler? Changed { add => _layout.Changed += value; remove => _layout.Changed -= value; }
    /// <summary>Captures committed geometry, or null during composition or before native layout.</summary>
    public RichEditorLayoutSnapshot? Capture() => _layout.Capture() is { } native ? new(this, native, _view.Document.StructuredProjection!) : null;
    /// <summary>Captures native input geometry, including provisional composition content.</summary>
    public RichEditorLayoutSnapshot? CaptureInput() => _layout.CaptureInput() is { } native ? new(this, native,
        native.IsProvisional ? _editor.Composition!.Snapshot.ProjectText() : _view.Document.StructuredProjection!) : null;
    /// <summary>Gets a visible caret rectangle for a rich point. Stale or foreign captures return null.</summary>
    public Rect? GetCaretBounds(RichEditorLayoutSnapshot capture, RichTextPoint point)
    {
        if (!IsCurrent(capture)) return null;
        return _layout.GetCaretBounds(capture.Native, capture.TextProjection.GetOffset(point),
            point.Affinity == CaretAffinity.Upstream ? RichTextCaretAffinity.Upstream : RichTextCaretAffinity.Downstream);
    }
    /// <summary>Gets separate visible rectangles for the authored text ranges of a typed selection.</summary>
    public IReadOnlyList<Rect> GetSelectionBounds(RichEditorLayoutSnapshot capture, RichSelection selection)
    {
        if (!IsCurrent(capture)) return [];
        return capture.TextProjection.Snapshot.GetSelectedText(selection).SelectMany(range =>
            _layout.GetRangeBounds(capture.Native, new(capture.TextProjection.GetOffset(range.Start), range.Length))).ToArray();
    }
    /// <summary>Maps a point in control coordinates to the nearest typed text address.</summary>
    public RichEditorHit? HitTest(RichEditorLayoutSnapshot capture, Point point)
    {
        if (!IsCurrent(capture) || _layout.HitTest(capture.Native, point) is not { } hit) return null;
        return capture.TextProjection.GetPoint(hit.Position, hit.Affinity == RichTextCaretAffinity.Upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream)
            is { } address ? new(address, hit.Kind) : null;
    }
    /// <summary>Reveals a committed rich text address using native scrolling.</summary>
    public void ScrollIntoView(RichTextPoint point) => _view.ScrollIntoView(new(_view.Document.StructuredProjection!.GetOffset(point), 0));
    private bool IsCurrent(RichEditorLayoutSnapshot capture) => ReferenceEquals(capture.Owner, this) &&
        ReferenceEquals(capture.TextProjection.Snapshot, capture.IsProvisional ? _editor.Composition?.Snapshot : _editor.Document.CurrentSnapshot);
}
