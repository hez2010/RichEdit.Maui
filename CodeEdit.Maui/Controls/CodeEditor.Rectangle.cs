using RichEdit.Maui;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    private RectangleGesture? _rectangle;
    private bool _updatingRectangle;
    private bool _rectangleTickQueued;

    private sealed class RectangleGesture(DocumentRevision revision, int position, RichTextCaretAffinity affinity,
        Point delta, Point point, double width)
    {
        internal readonly DocumentRevision Revision = revision;
        internal readonly int Position = position;
        internal readonly RichTextCaretAffinity Affinity = affinity;
        internal readonly Point Delta = delta;
        internal readonly double Width = width;
        internal Point Active = point;
        internal bool Dragging;
        internal readonly Dictionary<int, RectangleRow> Rows = [];
    }

    private sealed record RectangleRow(RichTextVisualLine Line, double CaretToTop);
    private void CancelRectangle() => _rectangle = null;

    private bool ObserveSelectionPointer(RichTextPointerEventArgs input, bool pressed)
    {
        if (pressed) CancelScrollRequest();
        if (input.DeviceKind == RichTextPointerDeviceKind.Touch) return false;
        if ((input.Buttons & RichTextPointerButtons.Primary) == 0)
        {
            var handled = _rectangle is not null;
            CancelRectangle();
            return handled;
        }
        var point = TranslatePointer(input).Point;
        if (!pressed)
        {
            if (_rectangle is not { } rectangle) return false;
            rectangle.Active = point;
            rectangle.Dragging = true;
            var handled = UpdateRectangleGesture();
            QueueRectangleScroll();
            return handled;
        }
        CancelRectangle();
        if (input.Modifiers == EditorKeyModifiers.None && !Composition.IsActive &&
            (Selections.Count > 1 || Selections.Primary.AnchorVirtualSpaces != 0 || Selections.Primary.ActiveVirtualSpaces != 0))
            Selections = new((TextSelection[])[Selections.Primary with { AnchorVirtualSpaces = 0, ActiveVirtualSpaces = 0 }]);
        if (input.Modifiers != EditorKeyModifiers.Alt || Composition.IsActive || TextLayout.Capture() is not { } layout ||
            TextLayout.HitTest(layout, point) is not { } hit || TextView.Handler is not RichEditorHandler handler ||
            handler.GetTextCaretBounds(hit.Position, hit.Affinity) is not { } caret) return false;
        _rectangle = new(Document.Revision, hit.Position, hit.Affinity,
            new(point.X - caret.X - layout.Origin.X, point.Y - caret.Y - layout.Origin.Y), point, layout.TextViewport.Width);
        var selections = Selections.ToList();
        var existing = selections.FindIndex(item => item.Range.IsEmpty && item.Active == hit.Position && item.ActiveVirtualSpaces == 0);
        if (existing >= 0 && selections.Count > 1)
        {
            var primaryId = Selections.Primary.Id;
            selections.RemoveAt(existing);
            var primary = selections.FindIndex(item => item.Id == primaryId);
            Selections = new(selections, primary < 0 ? Math.Min(existing, selections.Count - 1) : primary);
        }
        else AddCaret(hit.Position);
        return true;
    }

    private bool UpdateRectangleGesture()
    {
        if (_updatingRectangle || _rectangle is not { Dragging: true } gesture) return false;
        if (gesture.Revision != Document.Revision || Composition.IsActive || TextView.Handler is not RichEditorHandler handler ||
            TextLayout.Capture() is not { } layout || Math.Abs(layout.TextViewport.Width - gesture.Width) > 0.5 ||
            handler.GetTextCaretBounds(gesture.Position, gesture.Affinity) is not { } anchorCaret)
        { CancelRectangle(); return false; }
        _updatingRectangle = true;
        try
        {
            foreach (var row in layout.Lines)
                if (row.SourceRanges.Count > 0 && handler.GetTextCaretBounds(row.SourceRanges[0].Start, RichTextCaretAffinity.Downstream) is { } caret)
                    gesture.Rows[row.SourceRanges[0].Start] = new(row, row.Bounds.Top - caret.Top - layout.Origin.Y);
            var rows = new List<RichTextVisualLine>();
            foreach (var row in gesture.Rows.Values)
                if (handler.GetTextCaretBounds(row.Line.SourceRanges[0].Start, RichTextCaretAffinity.Downstream) is { } caret)
                    rows.Add(row.Line with { Bounds = new(row.Line.Bounds.X,
                        caret.Top + layout.Origin.Y + row.CaretToTop, row.Line.Bounds.Width, row.Line.Bounds.Height) });
            var anchor = new Point(anchorCaret.X + layout.Origin.X + gesture.Delta.X, anchorCaret.Y + layout.Origin.Y + gesture.Delta.Y);
            return SelectRectangleRows(anchor, gesture.Active, AllowVirtualSpace, layout, rows, handler);
        }
        finally { _updatingRectangle = false; }
    }

    private void QueueRectangleScroll()
    {
        if (_rectangleTickQueued || _rectangle is not { Dragging: true }) return;
        _rectangleTickQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
        {
            _rectangleTickQueued = false;
            if (_rectangle is not { Dragging: true } gesture || TextLayout.Capture() is not { } layout ||
                TextView.Handler is not RichEditorHandler handler) return;
            var viewport = layout.TextViewport;
            var dx = Outside(gesture.Active.X, viewport.Left, viewport.Right);
            var dy = Outside(gesture.Active.Y, viewport.Top, viewport.Bottom);
            if (dx == 0 && dy == 0) return;
            if (handler.ScrollViewportBy(new(Math.Clamp(dx, -viewport.Width / 3, viewport.Width / 3),
                Math.Clamp(dy, -viewport.Height / 3, viewport.Height / 3)))) UpdateRectangleGesture();
            QueueRectangleScroll();
        });
        static double Outside(double value, double minimum, double maximum) =>
            value < minimum ? value - minimum : value > maximum ? value - maximum : 0;
    }

    /// <summary>Identifies <see cref="AllowVirtualSpace"/>.</summary>
    public static readonly BindableProperty AllowVirtualSpaceProperty = BindableProperty.Create(
        nameof(AllowVirtualSpace), typeof(bool), typeof(CodeEditor), false,
        propertyChanged: static (view, _, value) => ((CodeEditor)view).TextView.SourceVirtualSpaceEnabled = (bool)value);

    /// <summary>Gets or sets whether caret movement and rectangular gestures can extend beyond logical line ends.</summary>
    public bool AllowVirtualSpace
    {
        get => (bool)GetValue(AllowVirtualSpaceProperty);
        set => SetValue(AllowVirtualSpaceProperty, value);
    }

    /// <summary>Selects the visual text intersecting a rectangle on visible native rows, including disjoint bidi intervals.</summary>
    /// <remarks>Points use this control's coordinates. Native glyph extents determine selection; virtual space is available only
    /// beyond a logical line end. Alt-pointer drags retain their source anchor and scroll when the pointer leaves the viewport.</remarks>
    public bool SelectRectangle(Point anchor, Point active, bool virtualSpace = false)
    {
        VerifyAccess();
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || !double.IsFinite(active.X) || !double.IsFinite(active.Y))
            throw new ArgumentOutOfRangeException(nameof(active));
        if (Composition.IsActive || TextView.Handler is not RichEditorHandler handler || TextLayout.Capture() is not { } layout)
            return false;
        return SelectRectangleRows(anchor, active, virtualSpace, layout, layout.Lines, handler);
    }

    private bool SelectRectangleRows(Point anchor, Point active, bool virtualSpace, RichTextLayoutSnapshot layout,
        IEnumerable<RichTextVisualLine> rows, RichEditorHandler handler)
    {
        var top = Math.Min(anchor.Y, active.Y);
        var bottom = Math.Max(anchor.Y, active.Y);
        var selections = new List<TextSelection>();
        var primary = 0;
        var distance = double.MaxValue;
        foreach (var line in rows.Where(row => row.SourceRanges.Count > 0).OrderBy(row => row.SourceRanges[0].Start))
        {
            if (line.Bounds.Bottom <= top || line.Bounds.Top > bottom) continue;
            var y = line.Bounds.Center.Y;
            var first = Endpoint(anchor.X, y, line);
            var last = Endpoint(active.X, y, line);
            if (first is null || last is null) continue;
            var start = line.SourceRanges[0].Start;
            var end = End(line);
            var ranges = new List<TextSpan>();
            if (Math.Abs(anchor.X - active.X) >= 0.5)
                foreach (var visible in line.SourceRanges)
                    if (Math.Min(visible.End, end) > visible.Start)
                        ranges.AddRange(handler.GetRectangleRanges(new(visible.Start, Math.Min(visible.End, end) - visible.Start),
                            Math.Min(anchor.X, active.X) - layout.Origin.X, Math.Max(anchor.X, active.X) - layout.Origin.X, y - layout.Origin.Y));
            var rowStart = selections.Count;
            foreach (var range in ranges)
                selections.Add(anchor.X <= active.X ? new(range.Start, range.End) : new(range.End, range.Start));
            if (ranges.Count == 0)
                selections.Add(new(first.Value.Position, last.Value.Position)
                {
                    AnchorVirtualSpaces = first.Value.Spaces, ActiveVirtualSpaces = last.Value.Spaces,
                    Affinity = last.Value.Affinity,
                });
            else if (first.Value.Spaces > 0 || last.Value.Spaces > 0)
                selections.Add(new(end, end) { AnchorVirtualSpaces = first.Value.Spaces, ActiveVirtualSpaces = last.Value.Spaces });
            for (var i = rowStart; i < selections.Count; i++)
                selections[i] = selections[i] with { PreferredVisualX = active.X - layout.Origin.X };
            if (Math.Abs(y - active.Y) < distance)
            {
                distance = Math.Abs(y - active.Y);
                primary = Enumerable.Range(rowStart, selections.Count - rowStart)
                    .MinBy(i => Math.Abs(selections[i].Active - last.Value.Position));
            }
        }
        if (selections.Count == 0) return false;
        var candidate = new TextSelectionSet(selections, primary);
        // Retain identities for unchanged rows. Publishing equivalent selections
        // from layout callbacks would otherwise request layout indefinitely.
        var identities = Selections.ToDictionary(item => (item.Anchor, item.Active, item.AnchorVirtualSpaces, item.ActiveVirtualSpaces), item => item.Id);
        var retained = candidate.Select(item => identities.TryGetValue((item.Anchor, item.Active, item.AnchorVirtualSpaces, item.ActiveVirtualSpaces), out var id)
            ? item with { Id = id } : item).ToArray();
        if (candidate.PrimaryIndex != Selections.PrimaryIndex || !retained.SequenceEqual(Selections))
            Selections = new(retained, candidate.PrimaryIndex);
        return true;

        int End(RichTextVisualLine line)
        {
            var end = line.SourceRanges[^1].End;
            var source = Document.CurrentSnapshot.TextBuffer;
            while (end > line.SourceRanges[0].Start && source[end - 1] is '\r' or '\n') end--;
            return end;
        }

        (int Position, int Spaces, CaretAffinity Affinity)? Endpoint(double x, double y, RichTextVisualLine line)
        {
            var hit = handler.HitTestText(new(x - layout.Origin.X, y - layout.Origin.Y));
            if (hit is null) return null;
            var source = Document.CurrentSnapshot.TextBuffer;
            var position = Math.Clamp(hit.Position, line.SourceRanges[0].Start, End(line));
            var spaces = 0;
            var logical = source.GetLine(source.GetPosition(position).Line);
            if ((virtualSpace || AllowVirtualSpace) && position == logical.Span.End &&
                handler.GetTextCaretBounds(position, RichTextCaretAffinity.Upstream) is { } end &&
                Math.Abs(end.Center.Y + layout.Origin.Y - y) < end.Height)
                spaces = Math.Max(0, checked((int)Math.Round((x - layout.Origin.X - end.X) / handler.VirtualSpaceAdvance(position))));
            return (position, spaces, (CaretAffinity)hit.Affinity);
        }
    }
}
