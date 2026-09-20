using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using WinRT;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    [DynamicWindowsRuntimeCast(typeof(UIElement))]
    private Point TextLayoutOrigin()
    {
        var origin = (_adornmentScroller?.Content as UIElement)?.TransformToVisual(PlatformView).TransformPoint(new(0, 0));
        return origin is { } point ? new(point.X, point.Y) : new(PlatformView.Padding.Left, PlatformView.Padding.Top);
    }

    private int NativePositionFromDisplay(int source) => _hasNativeLinks ? GetNativeTextSnapshot().ToNativePosition(source) : source;

    private int DisplayPositionFromNative(int native) => _hasNativeLinks ? GetNativeTextSnapshot().ToLogicalPosition(native) : Math.Clamp(native, 0, DisplaySourceSnapshot.Length);

    private partial RichTextNativeLayout? CaptureTextLayoutCore()
    {
        if (!PlatformView.IsLoaded)
            return null;

        var viewport = GetAdornmentViewport();
        var padding = PlatformView.Padding;
        var textViewport = new Rect(padding.Left, padding.Top, Math.Max(0, viewport.Width - padding.Left - padding.Right), Math.Max(0, viewport.Height - padding.Top - padding.Bottom));
        var origin = TextLayoutOrigin();
        NativeGeometryQueryCount++;
        var line = PlatformView.Document.GetRangeFromPoint(new(0, Math.Max(0, -origin.Y)), PointOptions.ClientCoordinates);
        line.Expand(TextRangeUnit.Line);
        var lines = new List<RichTextVisualLine>();
        var lastStart = -1;
        while (line.StartPosition > lastStart)
        {
            lastStart = line.StartPosition;
            NativeGeometryQueryCount++;
            line.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var native, out _);
            var bounds = new Rect(native.X + origin.X, native.Y + origin.Y, Math.Max(1, native.Width), native.Height);
            if (bounds.Top >= textViewport.Bottom)
                break;
            if (bounds.Height > 0 && bounds.Bottom > textViewport.Top)
            {
                NativeGeometryQueryCount++;
                line.GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Baseline,
                    PointOptions.ClientCoordinates | PointOptions.AllowOffClient | PointOptions.Start, out var baseline);
                var ranges = VisibleSourceRanges(DisplayPositionFromNative(line.StartPosition), DisplayPositionFromNative(line.EndPosition));
                if (!ranges.IsEmpty)
                    lines.Add(new(ranges, bounds, baseline.Y + origin.Y));
            }
            if (DisplayPositionFromNative(line.EndPosition) >= DisplaySourceSnapshot.Length)
                break;

            line.SetRange(line.EndPosition, line.EndPosition);
            line.Expand(TextRangeUnit.Line);
        }
        // TOM includes the final paragraph terminator, including the empty document's caret line.
        if (GetTextCaretBoundsCore(DisplaySourceSnapshot.Length, RichTextCaretAffinity.Downstream) is { } end &&
            end.IntersectsWith(textViewport) && (lines.Count == 0 || Math.Abs(lines[^1].Bounds.Y - end.Y) > 0.5))
        {
            var caret = PlatformView.Document.GetRange(NativePositionFromDisplay(DisplaySourceSnapshot.Length), NativePositionFromDisplay(DisplaySourceSnapshot.Length));
            NativeGeometryQueryCount++;
            caret.GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Baseline, PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var baseline);
            lines.Add(new(ImmutableArray.Create(new RichTextRange(VirtualView.Document.Length, 0)), end, baseline.Y + origin.Y));
        }

        return new(viewport, textViewport, lines.ToImmutableArray());
    }

    private partial Rect? GetTextCaretBoundsCore(int position, RichTextCaretAffinity affinity)
    {
        if (!PlatformView.IsLoaded)
            return null;

        var native = NativePositionFromDisplay(position);
        var range = PlatformView.Document.GetRange(native, native);
        var origin = TextLayoutOrigin();
        if (affinity == RichTextCaretAffinity.Upstream && position > 0)
        {
            NativeGeometryQueryCount += 2;
            range.MoveStart(TextRangeUnit.Character, -1);
            range.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var previous, out _);
            range.GetPoint(HorizontalCharacterAlignment.Right, VerticalCharacterAlignment.Top,
                PointOptions.ClientCoordinates | PointOptions.AllowOffClient | PointOptions.Start, out var edge);
            return new(edge.X + origin.X, previous.Y + origin.Y, 1, previous.Height);
        }

        NativeGeometryQueryCount++;
        range.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        return new(rect.X + origin.X, rect.Y + origin.Y, Math.Max(1, rect.Width), rect.Height);
    }

    private partial IReadOnlyList<Rect> GetTextRangeBoundsCore(RichTextRange range)
    {
        var result = new List<Rect>();
        var origin = TextLayoutOrigin();
        var viewport = GetAdornmentViewport();
        var padding = PlatformView.Padding;
        var visible = new Rect(padding.Left, padding.Top, Math.Max(0, viewport.Width - padding.Left - padding.Right),
            Math.Max(0, viewport.Height - padding.Top - padding.Bottom));
        var native = PlatformView.Document.GetRange(0, 0);
        var text = DisplaySourceSnapshot.Text;
        if (range.IsEmpty)
            return result;

        // Prune whole offscreen subranges and subdivide the rest down to one grapheme.
        // Bounding boxes are only a rejection test: visible leaves still retain disjoint
        // bidi rectangles, and every split is on a text-element boundary.
        var elements = StringInfo.ParseCombiningCharacters(text.Substring(range.Start, range.Length));
        var pending = new Stack<(int First, int Last)>();
        pending.Push((0, elements.Length));
        while (pending.TryPop(out var part))
        {
            var bounds = Bounds(Offset(part.First), Offset(part.Last));
            if (part.Last - part.First == 1)
            {
                if (bounds.IntersectsWith(visible))
                    AddFragment(result, bounds);
                continue;
            }

            // An unavailable rectangle is not evidence that its characters are
            // offscreen. Subdivide it so visible leaves can still be queried.
            if (bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
                double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom) && !bounds.IntersectsWith(visible))
                continue;

            var middle = part.First + (part.Last - part.First) / 2;
            pending.Push((middle, part.Last));
            pending.Push((part.First, middle));
        }

        return result;

        int Offset(int index) => index < elements.Length ? range.Start + elements[index] : range.End;

        Rect Bounds(int start, int end)
        {
            native.SetRange(NativePositionFromDisplay(start), NativePositionFromDisplay(end));
            NativeGeometryQueryCount++;
            native.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
            return new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);
        }
    }

    private partial RichTextHit? HitTestTextCore(Point point)
    {
        if (!PlatformView.IsLoaded)
            return null;

        var origin = TextLayoutOrigin();
        NativeGeometryQueryCount += 2;
        var native = PlatformView.Document.GetRangeFromPoint(new(point.X - origin.X, point.Y - origin.Y), PointOptions.ClientCoordinates);
        var position = DisplayPositionFromNative(native.StartPosition);
        native.Expand(TextRangeUnit.Character);
        native.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        var bounds = new Rect(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);
        return SourceHit(position, RichTextCaretAffinity.Downstream, bounds.Contains(point));
    }
}
