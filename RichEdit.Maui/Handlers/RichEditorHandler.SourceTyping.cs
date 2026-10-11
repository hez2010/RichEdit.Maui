namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private bool UpdateSourceTypingFormats()
    {
        if (VirtualView.SourceSession is null) return false;
        // Source has no authored character/paragraph style. Reading native
        // syntax spans here is both semantically wrong and, on some platforms,
        // requires materializing the entire native paragraph or buffer.
        _nativeTypingFormat = RichTextCharacterFormat.Default;
        _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
        VirtualView.UpdateTypingFormatsFromPlatform(_nativeTypingFormat, _nativeTypingParagraphFormat);
        return true;
    }
}
