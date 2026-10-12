#if IOS || MACCATALYST
namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial Rect? GetTextRangeExtentCore(TextSpan range)
    {
        var rectangles = GetTextRangeBoundsCore(range);
        if (rectangles.Count == 0) return null;
        var bounds = rectangles[0];
        for (var i = 1; i < rectangles.Count; i++) bounds = bounds.Union(rectangles[i]);
        return bounds;
    }

    private partial bool ScrollViewportByCore(Point delta)
    {
        var inset = PlatformView.AdjustedContentInset;
        PlatformView.SetContentOffset(new(
            Math.Clamp(PlatformView.ContentOffset.X + delta.X, -inset.Left,
                Math.Max(-inset.Left, PlatformView.ContentSize.Width - PlatformView.Bounds.Width + inset.Right)),
            Math.Clamp(PlatformView.ContentOffset.Y + delta.Y, -inset.Top,
                Math.Max(-inset.Top, PlatformView.ContentSize.Height - PlatformView.Bounds.Height + inset.Bottom))), false);
        return true;
    }
}
#endif
