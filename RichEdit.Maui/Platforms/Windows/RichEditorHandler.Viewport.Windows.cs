namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial Rect? GetTextRangeExtentCore(TextSpan range)
    {
        var origin = TextLayoutOrigin();
        NativeGeometryQueryCount++;
        PlatformView.Document.GetRange(NativePositionFromDisplay(range.Start), NativePositionFromDisplay(range.End))
            .GetRect(Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var rect, out _);
        return new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);
    }

    private partial bool ScrollViewportByCore(Point delta)
    {
        if (!PlatformView.IsLoaded) return false;
        PlatformView.ScrollBy(delta.X, delta.Y);
        return true;
    }
}
