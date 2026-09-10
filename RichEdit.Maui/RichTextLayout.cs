using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>Chooses a caret edge at a wrap or bidirectional boundary.</summary>
public enum RichTextCaretAffinity
{
    /// <summary>The edge following preceding source text.</summary>
    Upstream,
    /// <summary>The edge preceding following source text.</summary>
    Downstream,
}

/// <summary>Describes the content under a pointer.</summary>
public enum RichTextHitKind
{
    /// <summary>The point lies over source text.</summary>
    Text,
    /// <summary>The point maps to the nearest source position in whitespace.</summary>
    NearestPosition,
    /// <summary>The point lies over an application adornment.</summary>
    Adornment,
}

/// <summary>A visible native line fragment, excluding hidden source and display reservations.</summary>
/// <param name="SourceRanges">The visible source intervals, in source order.</param>
/// <param name="Bounds">The fragment bounds in the exposing control's coordinates.</param>
/// <param name="Baseline">The baseline's vertical coordinate.</param>
public sealed record RichTextVisualLine(IReadOnlyList<RichTextRange> SourceRanges, Rect Bounds, double Baseline);

/// <summary>A source-position hit without changing selection, scrolling, or folding.</summary>
/// <param name="Position">The nearest UTF-16 source boundary.</param>
/// <param name="Affinity">The source edge chosen at an ambiguous boundary.</param>
/// <param name="Kind">Whether the point is over source, whitespace, or an adornment.</param>
/// <param name="Adornment">The application adornment, when applicable.</param>
public sealed record RichTextHit(int Position, RichTextCaretAffinity Affinity, RichTextHitKind Kind, RichTextAdornment? Adornment = null);

/// <summary>Immutable visible-layout metadata. Queries against stale or foreign captures return no result.</summary>
public sealed class RichTextLayoutSnapshot
{
    internal object Owner { get; }
    internal object Attachment { get; }
    internal Point Origin { get; }
    internal RichTextLayoutSnapshot(object owner, object attachment, RichTextRevision revision, long version,
        Rect viewport, Rect textViewport, IEnumerable<RichTextVisualLine> lines, Point origin)
    {
        Owner = owner; Attachment = attachment; DocumentRevision = revision; LayoutVersion = version;
        Viewport = viewport; TextViewport = textViewport; Lines = lines.ToImmutableArray(); Origin = origin;
    }
    /// <summary>Gets the captured source-document revision.</summary>
    public RichTextRevision DocumentRevision { get; }
    /// <summary>Gets the layout generation, independent of source history.</summary>
    public long LayoutVersion { get; }
    /// <summary>Gets the visible editor rectangle in device-independent units.</summary>
    public Rect Viewport { get; }
    /// <summary>Gets the viewport excluding padding and reserved margins.</summary>
    public Rect TextViewport { get; }
    /// <summary>Gets visible native line fragments. No native objects are retained.</summary>
    public IReadOnlyList<RichTextVisualLine> Lines { get; }
}

/// <summary>Queries visible native geometry on the editor's UI thread.</summary>
public sealed class RichTextLayout
{
    private readonly RichEditor _editor;
    private readonly View? _ancestor;
    private readonly object _owner = new();
    private long _version;
    private bool _notificationQueued;
    private EventHandler? _changed;
    internal RichTextLayout(RichEditor editor) => _editor = editor;
    private RichTextLayout(RichEditor editor, View ancestor)
    {
        _editor = editor;
        _ancestor = ancestor;
    }

    /// <summary>Occurs after layout, viewport, appearance, attachment, or source changes.</summary>
    public event EventHandler? Changed
    {
        add
        {
            _editor.VerifyAccess();
            if (_ancestor is not null && _changed is null) _editor.TextLayout.Changed += OnSourceLayoutChanged;
            _changed += value;
        }
        remove
        {
            _editor.VerifyAccess();
            _changed -= value;
            if (_ancestor is not null && _changed is null) _editor.TextLayout.Changed -= OnSourceLayoutChanged;
        }
    }
    private void OnSourceLayoutChanged(object? sender, EventArgs args) => _changed?.Invoke(this, args);

    /// <summary>Creates a facade in an ancestor view's coordinates, for composed controls.</summary>
    /// <param name="ancestor">An ancestor of the text view in the MAUI visual tree.</param>
    /// <returns>A layout service that translates captures and pointer hit tests to that ancestor.</returns>
    public RichTextLayout CreateRelativeTo(View ancestor)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(ancestor);
        return new(_editor, ancestor);
    }

    /// <summary>Captures visible metadata, or null when no reconciled native layout is available.</summary>
    /// <returns>An immutable layout capture.</returns>
    public RichTextLayoutSnapshot? Capture()
    {
        _editor.VerifyAccess();
        if (Origin is not { } origin || _editor.Handler is not RichEditorHandler handler || handler.CaptureTextLayout() is not { } native) return null;
        return new(_owner, handler.LayoutAttachment, _editor.Document.Revision, Version,
            Translate(native.Viewport, origin), Translate(native.TextViewport, origin),
            native.Lines.Select(line => line with { Bounds = Translate(line.Bounds, origin), Baseline = line.Baseline + origin.Y }), origin);
    }

    /// <summary>Gets a visible caret rectangle without moving the caret.</summary>
    /// <param name="layout">A current capture from this service.</param>
    /// <param name="position">The UTF-16 source boundary.</param>
    /// <param name="affinity">Which side of a wrap or bidirectional boundary to use.</param>
    /// <returns>A visible caret rectangle, or null.</returns>
    public Rect? GetCaretBounds(RichTextLayoutSnapshot layout, int position, RichTextCaretAffinity affinity = RichTextCaretAffinity.Downstream)
    {
        if (!IsCurrent(layout)) return null;
        new RichTextRange(position, 0).Validate(_editor.Document.Length, nameof(position));
        if (!Enum.IsDefined(affinity)) throw new ArgumentOutOfRangeException(nameof(affinity));
        if (_editor.Folding.FindRange(position) is not null) return null;
        if (((RichEditorHandler)_editor.Handler!).GetTextCaretBounds(position, affinity) is not { } caret) return null;
        caret = Translate(caret, layout.Origin);
        return caret.IntersectsWith(layout.TextViewport) ? caret : null;
    }

    /// <summary>Gets separate visible rectangles for a source range, including wraps and bidi fragments.</summary>
    /// <param name="layout">A current capture from this service.</param>
    /// <param name="range">The bounded source range. Empty ranges use downstream caret geometry.</param>
    /// <returns>Visible rectangles, or an empty collection.</returns>
    public IReadOnlyList<Rect> GetRangeBounds(RichTextLayoutSnapshot layout, RichTextRange range)
    {
        if (!IsCurrent(layout)) return Array.Empty<Rect>();
        range.Validate(_editor.Document.Length, nameof(range));
        if (range.IsEmpty) return GetCaretBounds(layout, range.Start) is { } caret ? (Rect[])[caret] : Array.Empty<Rect>();
        var rectangles = new List<Rect>();
        var handler = (RichEditorHandler)_editor.Handler!;
        foreach (var line in layout.Lines)
        foreach (var visible in line.SourceRanges)
        {
            var start = Math.Max(range.Start, visible.Start);
            var end = Math.Min(range.End, visible.End);
            if (start >= end) continue;
            foreach (var rect in handler.GetTextRangeBounds(new(start, end - start)))
            {
                var translated = Translate(rect, layout.Origin);
                if (translated.IntersectsWith(layout.TextViewport)) rectangles.Add(translated.Intersect(layout.TextViewport));
            }
        }
        return rectangles.ToImmutableArray();
    }

    /// <summary>Hit-tests a local point without changing source, selection, scrolling, or folding.</summary>
    /// <param name="layout">A current capture from this service.</param>
    /// <param name="point">A point in the same coordinate space as the capture.</param>
    /// <returns>A source/adornment hit, or null outside the visible editor or for a stale capture.</returns>
    public RichTextHit? HitTest(RichTextLayoutSnapshot layout, Point point)
    {
        if (!IsCurrent(layout)) return null;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
        if (!layout.Viewport.Contains(point)) return null;
        var nativePoint = new Point(point.X - layout.Origin.X, point.Y - layout.Origin.Y);
        foreach (var adornment in _editor.Adornments.Reverse())
            if (adornment.LayoutBounds.Contains(nativePoint)) return new(adornment.Position, RichTextCaretAffinity.Downstream, RichTextHitKind.Adornment, adornment);
        return ((RichEditorHandler)_editor.Handler!).HitTestText(nativePoint);
    }

    internal long Version => _editor.TextLayout == this ? _version : _editor.TextLayout.Version;
    internal void Invalidate()
    {
        _version++;
        if (_notificationQueued) return;
        _notificationQueued = true;
        if (!_editor.Dispatcher.Dispatch(() => { _notificationQueued = false; _changed?.Invoke(this, EventArgs.Empty); })) _notificationQueued = false;
    }

    internal Point? Origin
    {
        get
        {
            if (_ancestor is null || ReferenceEquals(_ancestor, _editor)) return Point.Zero;
            var point = Point.Zero;
            for (Element? view = _editor; view is not null; view = view.Parent)
            {
                if (ReferenceEquals(view, _ancestor)) return point;
                if (view is VisualElement element) point = new(point.X + element.X + element.TranslationX, point.Y + element.Y + element.TranslationY);
            }
            return null;
        }
    }

    private bool IsCurrent(RichTextLayoutSnapshot layout)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(layout);
        return ReferenceEquals(layout.Owner, _owner) && layout.DocumentRevision == _editor.Document.Revision && layout.LayoutVersion == Version &&
            Origin == layout.Origin && _editor.Handler is RichEditorHandler handler && ReferenceEquals(layout.Attachment, handler.LayoutAttachment) && handler.HasTextLayout;
    }
    private static Rect Translate(Rect rect, Point offset) => new(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
}

internal sealed record RichTextNativeLayout(Rect Viewport, Rect TextViewport, IReadOnlyList<RichTextVisualLine> Lines);
