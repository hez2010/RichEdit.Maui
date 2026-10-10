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

    bool TryCompleteComposition();

    bool CanUndo { get; }

    bool CanRedo { get; }

    void ApplyDocument(
        TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat);

    void ApplyChanges(
        RichTextChangeSet changes,
        TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat);

    void ApplyAppearance(RichTextAppearanceChange changes);

    void ApplyDecorations(RichTextChangeSet changes);

    void ApplyFolding();

    void SetSelection(TextSelectionState selection);

    void ScrollIntoView(TextSpan range);

    void ApplyTypingFormat(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    void Undo();

    void Redo();

    void ClearUndoHistory();
}
