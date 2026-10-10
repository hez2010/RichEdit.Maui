namespace RichEdit.Maui;

internal static class TextSelectionStateExtensions
{
    internal static TextSelectionState Map(this TextSelectionState selection, IEnumerable<RichTextChange> changes, int length)
    {
        var anchor = selection.Anchor;
        var active = selection.Active;
        foreach (var change in changes.OfType<RichTextTextChange>())
        {
            anchor = MapOffset(anchor, change);
            active = MapOffset(active, change);
        }
        return new TextSelectionState(anchor, active).Clamp(length);
    }

    internal static int MapOffset(int offset, RichTextTextChange change) =>
        RichTextPositionMap.Map(offset, change, PositionAffinity.AfterInsertion)!.Value;
}
