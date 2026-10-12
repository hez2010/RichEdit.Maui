namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial Rect? GetTextRangeExtentCore(TextSpan range)
    {
        if (PlatformView.Layout is not { } layout) return null;
        using var path = new global::Android.Graphics.Path();
        using var bounds = new global::Android.Graphics.RectF();
        NativeGeometryQueryCount++;
        layout.GetSelectionPath(range.Start, range.End, path);
        path.ComputeBounds(bounds, true);
        var origin = TextLayoutOrigin();
        return new((bounds.Left + origin.X) / LayoutDensity, (bounds.Top + origin.Y) / LayoutDensity,
            bounds.Width() / LayoutDensity, bounds.Height() / LayoutDensity);
    }

    private partial bool ScrollViewportByCore(Point delta)
    {
        if (PlatformView.Layout is not { } layout) return false;
        var width = Math.Max(0, PlatformView.Width - PlatformView.CompoundPaddingLeft - PlatformView.CompoundPaddingRight);
        var height = Math.Max(0, PlatformView.Height - PlatformView.CompoundPaddingTop - PlatformView.CompoundPaddingBottom);
        PlatformView.ScrollTo(
            (int)Math.Round(Math.Clamp(PlatformView.ScrollX + delta.X * LayoutDensity, 0, Math.Max(0, PlatformView.SourceHorizontalScrollRange - width))),
            (int)Math.Round(Math.Clamp(PlatformView.ScrollY + delta.Y * LayoutDensity, 0, Math.Max(0, layout.Height - height))));
        return true;
    }
}

