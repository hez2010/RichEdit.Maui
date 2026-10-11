namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    // Offsets and deltas use DIPs, like the public native-layout facade. This
    // operates on the native scroller and never temporarily moves selection.
    internal bool ScrollViewportBy(Point delta)
    {
        if (!HasTextLayout || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return false;
        var accepted = ScrollViewportByCore(delta);
        if (accepted) QueueAdornmentLayout();
        return accepted;
    }

    private partial bool ScrollViewportByCore(Point delta);
}
