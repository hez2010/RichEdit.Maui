using RichEdit.Core;

namespace RichEdit.Maui;

/// <summary>A native layout capture paired with the exact rich snapshot and its explicit text projection.</summary>
public sealed class RichEditorLayoutSnapshot
{
    internal RichEditorLayoutSnapshot(RichEditorLayout owner, RichTextLayoutSnapshot native, RichTextProjection projection)
    { Owner = owner; Native = native; TextProjection = projection; Viewport = native.Viewport; Lines = native.Lines; LayoutVersion = native.LayoutVersion; IsProvisional = native.IsProvisional; }
    internal RichEditorLayoutSnapshot(RichEditorLayout owner, RichTextProjection projection, Rect viewport, long version, bool provisional,
        IReadOnlyList<RichSurfaceLayout> surfaces, IReadOnlyList<RichTextVisualLine> lines)
    { Owner = owner; TextProjection = projection; Viewport = viewport; LayoutVersion = version; IsProvisional = provisional; Surfaces = surfaces; Lines = lines; }
    internal RichEditorLayout Owner { get; }
    internal RichTextLayoutSnapshot? Native { get; }
    internal IReadOnlyList<RichSurfaceLayout> Surfaces { get; } = [];
    /// <summary>Gets the snapshot-bound text projection used by the visible line ranges.</summary>
    public RichTextProjection TextProjection { get; }
    /// <summary>Gets whether the capture includes provisional marked text.</summary>
    public bool IsProvisional { get; }
    /// <summary>Gets the native layout generation.</summary>
    public long LayoutVersion { get; }
    /// <summary>Gets the visible editor rectangle in control coordinates.</summary>
    public Rect Viewport { get; }
    /// <summary>Gets visible native lines. Their ranges address TextProjection, never an independently mutable string.</summary>
    public IReadOnlyList<RichTextVisualLine> Lines { get; }
}

internal sealed record RichSurfaceLayout(NativeEditorView View, RichTextLayoutSnapshot Layout, RichTextProjection Projection, Point Offset);

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
    public RichEditorLayoutSnapshot? Capture() => _layout.Capture() is { } native ? new(this, native, _view.Document.StructuredProjection!) : CaptureSurfaces(false);
    /// <summary>Captures native input geometry, including provisional composition content.</summary>
    public RichEditorLayoutSnapshot? CaptureInput() => _layout.CaptureInput() is { } native ? new(this, native,
        native.IsProvisional ? _editor.Composition!.Snapshot.ProjectText() : _view.Document.StructuredProjection!) : CaptureSurfaces(true);
    /// <summary>Gets a visible caret rectangle for a rich point. Stale or foreign captures return null.</summary>
    public Rect? GetCaretBounds(RichEditorLayoutSnapshot capture, RichTextPoint point)
    {
        if (!IsCurrent(capture)) return null;
        var affinity = point.Affinity == CaretAffinity.Upstream ? RichTextCaretAffinity.Upstream : RichTextCaretAffinity.Downstream;
        if (capture.Native is { } native) return _layout.GetCaretBounds(native, capture.TextProjection.GetOffset(point), affinity);
        foreach (var surface in capture.Surfaces)
            if (surface.Projection.ContainsParagraph(point.Paragraph) && surface.View.TextLayout.GetCaretBounds(surface.Layout, surface.Projection.GetOffset(point), affinity) is { } bounds)
            {
                var translated = Translate(bounds, surface.Offset);
                return translated.IntersectsWith(capture.Viewport) ? translated : null;
            }
        return null;
    }
    /// <summary>Gets separate visible rectangles for the authored text ranges of a typed selection.</summary>
    public IReadOnlyList<Rect> GetSelectionBounds(RichEditorLayoutSnapshot capture, RichSelection selection)
    {
        if (!IsCurrent(capture)) return [];
        var ranges = capture.TextProjection.Snapshot.GetSelectedText(selection);
        if (capture.Native is { } native) return ranges.SelectMany(range =>
            _layout.GetRangeBounds(native, new(capture.TextProjection.GetOffset(range.Start), range.Length))).ToArray();
        return capture.Surfaces.SelectMany(surface => ranges.Where(range => surface.Projection.ContainsParagraph(range.Start.Paragraph))
            .SelectMany(range => surface.View.TextLayout.GetRangeBounds(surface.Layout, new(surface.Projection.GetOffset(range.Start), range.Length)))
            .Select(bounds => Translate(bounds, surface.Offset))).Where(bounds => bounds.IntersectsWith(capture.Viewport))
            .Select(bounds => bounds.Intersect(capture.Viewport)).ToArray();
    }
    /// <summary>Maps a point in control coordinates to the nearest typed text address.</summary>
    public RichEditorHit? HitTest(RichEditorLayoutSnapshot capture, Point point)
    {
        if (!IsCurrent(capture)) return null;
        if (capture.Native is { } native)
        {
            if (_layout.HitTest(native, point) is not { } hit) return null;
            return capture.TextProjection.GetPoint(hit.Position, hit.Affinity == RichTextCaretAffinity.Upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream)
                is { } address ? new(address, hit.Kind) : null;
        }
        if (!capture.Viewport.Contains(point)) return null;
        foreach (var surface in capture.Surfaces)
            if (surface.View.TextLayout.HitTest(surface.Layout, new(point.X - surface.Offset.X, point.Y - surface.Offset.Y)) is { } hit &&
                surface.Projection.GetPoint(hit.Position, hit.Affinity == RichTextCaretAffinity.Upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream) is { } address)
                return new(address, hit.Kind);
        return null;
    }
    /// <summary>Reveals a committed rich text address using native scrolling.</summary>
    public void ScrollIntoView(RichTextPoint point)
    {
        if (_view.FindStructuredSurface(new RichEdit.Core.RichTextSelection(point, point)) is { } surface)
            surface.ScrollIntoView(new(surface.Document.StructuredProjection!.GetOffset(point), 0));
        else _view.ScrollIntoView(new(_view.Document.StructuredProjection!.GetOffset(point), 0));
    }
    private bool IsCurrent(RichEditorLayoutSnapshot capture) => ReferenceEquals(capture.Owner, this) &&
        ReferenceEquals(capture.TextProjection.Snapshot, capture.IsProvisional ? _editor.Composition?.Snapshot : _editor.Document.CurrentSnapshot) &&
        (capture.Native is not null || capture.LayoutVersion == _layout.Version && capture.Surfaces.All(surface => surface.Offset == surface.View.CurrentStructuredInputOffset));

    private RichEditorLayoutSnapshot? CaptureSurfaces(bool includeInput)
    {
        if (!includeInput && _editor.Composition is not null || _editor.Width <= 0 || _editor.Height <= 0) return null;
        var projection = includeInput && _editor.Composition is { } composition ? composition.Snapshot.ProjectText() : _view.Document.StructuredProjection!;
        var viewport = new Rect(0, 0, _editor.Width, _editor.Height);
        var surfaces = new List<RichSurfaceLayout>();
        var lines = new List<RichTextVisualLine>();
        foreach (var surface in _view.StructuredSurfaces())
        {
            if (surface.StructuredInputBoundsProvider?.Invoke() is { } bounds && !bounds.IntersectsWith(viewport)) continue;
            var scoped = includeInput ? surface.ScopedInputProjection : surface.Document.StructuredProjection;
            if (scoped is null) continue;
            var captured = includeInput ? surface.TextLayout.CaptureInput() : surface.TextLayout.Capture();
            if (captured is null) continue;
            var offset = surface.CurrentStructuredInputOffset;
            surfaces.Add(new(surface, captured, scoped, offset));
            foreach (var line in captured.Lines)
            {
                var translated = Translate(line.Bounds, offset);
                if (!translated.IntersectsWith(viewport)) continue;
                var ranges = new List<TextSpan>();
                foreach (var range in line.SourceRanges)
                    if (scoped.GetPoint(range.Start) is { } start && scoped.GetPoint(range.End) is { } end &&
                        projection.ContainsParagraph(start.Paragraph) && projection.ContainsParagraph(end.Paragraph))
                    {
                        var first = projection.GetOffset(start); ranges.Add(new(first, projection.GetOffset(end) - first));
                    }
                lines.Add(new(ranges, translated.Intersect(viewport), line.Baseline + offset.Y));
            }
        }
        return surfaces.Count == 0 ? null : new(this, projection, viewport, _layout.Version, _editor.Composition is not null, surfaces, lines);
    }
    private static Rect Translate(Rect bounds, Point offset) => new(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height);
}
