#if ANDROID
using PlatformRichEditor = RichEdit.Maui.Platforms.Android.RichEditText;
#elif IOS || MACCATALYST
using PlatformRichEditor = RichEdit.Maui.Platforms.Apple.RichTextView;
#elif WINDOWS
using PlatformRichEditor = Microsoft.UI.Xaml.Controls.RichEditBox;
#endif

using Microsoft.Maui.Handlers;

namespace RichEdit.Maui;

internal interface IRichEditorHandler
{
    void ApplyDocument(RichTextDocument document, int selectionStart, int selectionLength);

    void ApplyTypingFormat(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    void SetSelection(int start, int length);
}

public partial class RichEditorHandler : ViewHandler<RichEditor, PlatformRichEditor>, IRichEditorHandler
{
    public static IPropertyMapper<RichEditor, RichEditorHandler> Mapper =
        new PropertyMapper<RichEditor, RichEditorHandler>(ViewMapper)
        {
            [nameof(RichEditor.Document)] = MapDocument,
            [nameof(RichEditor.Placeholder)] = MapPlaceholder,
            [nameof(RichEditor.PlaceholderColor)] = MapPlaceholder,
            [nameof(RichEditor.TextColor)] = MapAppearance,
            [nameof(RichEditor.FontFamily)] = MapAppearance,
            [nameof(RichEditor.FontSize)] = MapAppearance,
            [nameof(RichEditor.IsReadOnly)] = MapIsReadOnly,
        };

    public RichEditorHandler() : base(Mapper)
    {
    }

    public RichEditorHandler(IPropertyMapper? mapper) : base(mapper ?? Mapper)
    {
    }

    void IRichEditorHandler.ApplyDocument(RichTextDocument document, int selectionStart, int selectionLength) =>
        ApplyDocumentCore(document, selectionStart, selectionLength);

    void IRichEditorHandler.ApplyTypingFormat(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat) =>
        ApplyTypingFormatCore(characterFormat, paragraphFormat);

    void IRichEditorHandler.SetSelection(int start, int length) => SetSelectionCore(start, length);

    private partial void ApplyDocumentCore(RichTextDocument document, int selectionStart, int selectionLength);

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    private partial void SetSelectionCore(int start, int length);

    private static void MapDocument(RichEditorHandler handler, RichEditor editor)
    {
        if (!editor.IsUpdatingFromPlatform)
        {
            handler.ApplyDocumentCore(editor.Document, editor.SelectionStart, editor.SelectionLength);
            handler.ApplyTypingFormatCore(
                editor.TypingCharacterFormat,
                editor.TypingParagraphFormat);
        }
    }

    private static void MapPlaceholder(RichEditorHandler handler, RichEditor editor) =>
        handler.UpdatePlaceholder(editor);

    private static void MapAppearance(RichEditorHandler handler, RichEditor editor) =>
        handler.UpdateAppearance(editor);

    private static void MapIsReadOnly(RichEditorHandler handler, RichEditor editor) =>
        handler.UpdateIsReadOnly(editor);

    private partial void UpdatePlaceholder(RichEditor editor);

    private partial void UpdateAppearance(RichEditor editor);

    private partial void UpdateIsReadOnly(RichEditor editor);
}
