namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    internal bool UpdateSourceTabSize(int size)
    {
        if (VirtualView.SourceSession is null || VirtualView.Composition.IsActive || ((IRichEditorHandler)this).IsComposing) return false;
        VirtualView.SourceTabSize = size;
        // Tab spacing is view configuration. Reprojecting it must not enter the
        // source document, its revision, or its undo history.
        ApplyCurrentDocument(VirtualView.SelectedRange.Start, VirtualView.SelectedRange.Length);
        ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
        VirtualView.TextLayout.Invalidate();
        return true;
    }
}
