using System.Collections.Immutable;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private float LayoutDensity => PlatformView.Resources?.DisplayMetrics?.Density ?? 1f;
    private Point TextLayoutOrigin() => new(PlatformView.CompoundPaddingLeft - PlatformView.ScrollX, PlatformView.CompoundPaddingTop - PlatformView.ScrollY);

    private partial RichTextNativeLayout? CaptureTextLayoutCore()
    {
        if (PlatformView.Layout is not { } layout) return null;
        var density = LayoutDensity;
        var viewport = GetAdornmentViewport();
        var textViewport = new Rect(PlatformView.CompoundPaddingLeft / density, PlatformView.CompoundPaddingTop / density,
            Math.Max(0, (PlatformView.Width - PlatformView.CompoundPaddingLeft - PlatformView.CompoundPaddingRight) / density),
            Math.Max(0, (PlatformView.Height - PlatformView.CompoundPaddingTop - PlatformView.CompoundPaddingBottom) / density));
        var origin = TextLayoutOrigin();
        var lines = new List<RichTextVisualLine>();
        NativeGeometryQueryCount += 2;
        var lineCount = layout.LineCount;
        for (var index = layout.GetLineForVertical(Math.Max(0, PlatformView.ScrollY)); index < lineCount; index++)
        {
            NativeGeometryQueryCount++;
            var top = (layout.GetLineTop(index) + origin.Y) / density;
            if (top >= textViewport.Bottom) break;
            NativeGeometryQueryCount++;
            var bottom = (layout.GetLineBottom(index) + origin.Y) / density;
            if (bottom <= textViewport.Top) continue;
            NativeGeometryQueryCount += 2;
            var ranges = VisibleSourceRanges(layout.GetLineStart(index), layout.GetLineEnd(index));
            if (ranges.IsEmpty) continue;
            NativeGeometryQueryCount += 4;
            lines.Add(new(ranges, new((layout.GetLineLeft(index) + origin.X) / density, top,
                Math.Max(1, (layout.GetLineRight(index) - layout.GetLineLeft(index)) / density), bottom - top),
                (layout.GetLineBaseline(index) + origin.Y) / density));
        }
        return new(viewport, textViewport, lines.ToImmutableArray());
    }

    private partial Rect? GetTextCaretBoundsCore(int position, RichTextCaretAffinity affinity)
    {
        if (PlatformView.Layout is not { } layout) return null;
        NativeGeometryQueryCount += 5;
        var index = layout.GetLineForOffset(position);
        var x = layout.GetPrimaryHorizontal(position);
        if (affinity == RichTextCaretAffinity.Upstream && position > 0)
        {
            NativeGeometryQueryCount++;
            var preceding = layout.GetLineForOffset(position - 1);
            if (preceding < index && DisplaySourceSnapshot.Text[position - 1] is not ('\n' or '\u2028'))
            {
                NativeGeometryQueryCount += 2;
                index = preceding;
                x = layout.GetParagraphDirection(index) < 0 ? layout.GetLineLeft(index) : layout.GetLineRight(index);
            }
            else if (position < DisplaySourceSnapshot.Length)
            {
                NativeGeometryQueryCount += 2;
                if (layout.IsRtlCharAt(position - 1) != layout.IsRtlCharAt(position))
                {
                    NativeGeometryQueryCount++;
                    x = layout.GetSecondaryHorizontal(position);
                }
            }
        }
        var origin = TextLayoutOrigin();
        return new((x + origin.X) / LayoutDensity, (layout.GetLineTop(index) + origin.Y) / LayoutDensity,
            1, (layout.GetLineBottom(index) - layout.GetLineTop(index)) / LayoutDensity);
    }

    private partial IReadOnlyList<Rect> GetTextRangeBoundsCore(RichTextRange range)
    {
        if (PlatformView.Layout is not { } layout) return Array.Empty<Rect>();
        var origin = TextLayoutOrigin();
        using var path = new global::Android.Graphics.Path();
        NativeGeometryQueryCount++;
        layout.GetSelectionPath(range.Start, range.End, path);
        using var clip = new global::Android.Graphics.Region((int)-origin.X, (int)-origin.Y,
            (int)(PlatformView.Width - origin.X), (int)(PlatformView.Height - origin.Y));
        using var region = new global::Android.Graphics.Region();
        region.SetPath(path, clip);
        using var iterator = new global::Android.Graphics.RegionIterator(region);
        using var rect = new global::Android.Graphics.Rect();
        var result = new List<Rect>();
        while (iterator.Next(rect)) AddFragment(result, new((rect.Left + origin.X) / LayoutDensity, (rect.Top + origin.Y) / LayoutDensity,
            rect.Width() / LayoutDensity, rect.Height() / LayoutDensity));
        return result;
    }

    private partial RichTextHit? HitTestTextCore(Point point)
    {
        if (PlatformView.Layout is not { } layout) return null;
        var origin = TextLayoutOrigin();
        var x = point.X * LayoutDensity - origin.X;
        var y = point.Y * LayoutDensity - origin.Y;
        NativeGeometryQueryCount += 6;
        var index = layout.GetLineForVertical((int)y);
        var position = layout.GetOffsetForHorizontal(index, (float)x);
        var top = layout.GetLineTop(index);
        var bottom = layout.GetLineBottom(index);
        var left = layout.GetLineLeft(index);
        var right = layout.GetLineRight(index);
        return SourceHit(position, RichTextCaretAffinity.Downstream, y >= top && y < bottom && x >= left && x <= right);
    }
}
