namespace RichEdit.Maui;

[Flags]
internal enum RichTextAppearanceChange
{
    None = 0,
    TextColor = 1,
    FontFamily = 2,
    FontSize = 4,
    Placeholder = 8,
    PlaceholderColor = 16,
    NativeTheme = 32,
}

internal interface IRichEditorHandler
{
    object SourceToken { get; }

    bool SupportsNativeUndo { get; }

    bool IsComposing { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    void ApplyDocument(
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat);

    void ApplyChanges(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat);

    void ApplyAppearance(RichTextAppearanceChange changes);

    void ApplyDecorations(RichTextChangeSet changes);

    void ApplyFolding();

    void SetSelection(RichTextSelectionState selection);

    void ScrollIntoView(RichTextRange range);

    void ApplyTypingFormat(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    void Undo();

    void Redo();

    void ClearUndoHistory();
}
