#if ANDROID
using PlatformRichEditor = RichEdit.Maui.Platforms.Android.RichEditText;
#elif IOS || MACCATALYST
using PlatformRichEditor = RichEdit.Maui.Platforms.Apple.RichTextView;
#elif WINDOWS
using PlatformRichEditor = Microsoft.UI.Xaml.Controls.RichEditBox;
#endif

using Microsoft.Maui.Handlers;

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

    bool CanUndo { get; }

    bool CanRedo { get; }

    void ApplySnapshot(RichTextDocumentSnapshot snapshot, RichTextRange selection);

    void ApplyChanges(RichTextChangeSet changes, RichTextRange selection);

    void ApplyAppearance(RichTextAppearanceChange changes);

    void SetSelection(RichTextRange selection);

    void Undo();

    void Redo();

    void ClearUndoHistory();
}

/// <summary>
/// Connects <see cref="RichEditor"/> to the native rich editor for the current target.
/// </summary>
public partial class RichEditorHandler : ViewHandler<RichEditor, PlatformRichEditor>, IRichEditorHandler
{
    /// <summary>
    /// Gets the default property mapper used by rich-editor handlers.
    /// </summary>
    public static IPropertyMapper<RichEditor, RichEditorHandler> Mapper =
        new PropertyMapper<RichEditor, RichEditorHandler>(ViewMapper)
        {
            [nameof(RichEditor.Document)] = MapDocument,
            [nameof(RichEditor.Placeholder)] = MapPlaceholder,
            [nameof(RichEditor.PlaceholderColor)] = MapPlaceholder,
            [nameof(RichEditor.TextColor)] = MapAppearance,
            [nameof(RichEditor.FontFamily)] = MapAppearance,
            [nameof(RichEditor.FontSize)] = MapAppearance,
            [nameof(RichEditor.IsReadOnly)] = MapInputConfiguration,
            [nameof(RichEditor.IsSpellCheckEnabled)] = MapInputConfiguration,
            [nameof(RichEditor.IsTextPredictionEnabled)] = MapInputConfiguration,
            [nameof(RichEditor.Keyboard)] = MapInputConfiguration,
            [nameof(RichEditor.MaxLength)] = MapInputConfiguration,
            [nameof(RichEditor.AutoSize)] = MapInputConfiguration,
            [nameof(RichEditor.AcceptsTab)] = MapInputConfiguration,
        };

    private readonly object _sourceToken = new();

    /// <summary>
    /// Initializes a handler with the default mapper.
    /// </summary>
    public RichEditorHandler()
        : base(Mapper)
    {
    }

    /// <summary>
    /// Initializes a handler with a custom property mapper.
    /// </summary>
    /// <param name="mapper">The mapper to use, or null for <see cref="Mapper"/>.</param>
    public RichEditorHandler(IPropertyMapper? mapper)
        : base(mapper ?? Mapper)
    {
    }

    object IRichEditorHandler.SourceToken => _sourceToken;

    bool IRichEditorHandler.SupportsNativeUndo => SupportsNativeUndoCore();

    bool IRichEditorHandler.CanUndo => CanUndoCore();

    bool IRichEditorHandler.CanRedo => CanRedoCore();

    void IRichEditorHandler.ApplySnapshot(
        RichTextDocumentSnapshot snapshot,
        RichTextRange selection)
    {
        ApplyDocumentCore(snapshot, selection.Start, selection.Length);
        ClearUndoHistoryCore();
        VirtualView.UpdateUndoStateFromPlatform();
    }

    void IRichEditorHandler.ApplyChanges(
        RichTextChangeSet changes,
        RichTextRange selection) =>
        ApplyChangesCore(changes, selection);

    void IRichEditorHandler.ApplyAppearance(RichTextAppearanceChange changes)
    {
        if ((changes & (RichTextAppearanceChange.Placeholder |
                        RichTextAppearanceChange.PlaceholderColor)) != 0)
        {
            UpdatePlaceholder(VirtualView);
        }

        if ((changes & (RichTextAppearanceChange.TextColor |
                        RichTextAppearanceChange.FontFamily |
                        RichTextAppearanceChange.FontSize |
                        RichTextAppearanceChange.NativeTheme)) != 0)
        {
            UpdateAppearance(VirtualView);
        }
    }

    void IRichEditorHandler.SetSelection(RichTextRange selection)
    {
        SetSelectionCore(selection.Start, selection.Length);
        ApplyTypingFormatCore(
            VirtualView.Selection.TypingCharacterFormat,
            VirtualView.Selection.TypingParagraphFormat);
    }

    void IRichEditorHandler.Undo() => UndoCore();

    void IRichEditorHandler.Redo() => RedoCore();

    void IRichEditorHandler.ClearUndoHistory() => ClearUndoHistoryCore();

    private void ApplyChangesCore(RichTextChangeSet changes, RichTextRange selection)
    {
        if (ReferenceEquals(changes.SourceToken, _sourceToken))
        {
            return;
        }

        ApplyIncrementalChangesCore(changes, selection);
    }

    private partial void ApplyDocumentCore(
        RichTextDocumentSnapshot document,
        int selectionStart,
        int selectionLength);

    private partial void ApplyIncrementalChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection);

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    private partial void SetSelectionCore(int start, int length);

    private partial bool SupportsNativeUndoCore();

    private partial bool CanUndoCore();

    private partial bool CanRedoCore();

    private partial void UndoCore();

    private partial void RedoCore();

    private partial void ClearUndoHistoryCore();

    private static void MapDocument(RichEditorHandler handler, RichEditor editor)
    {
        handler.ApplyDocumentCore(
            editor.Document.CurrentSnapshot,
            editor.SelectedRange.Start,
            editor.SelectedRange.Length);
        handler.ApplyTypingFormatCore(
            editor.Selection.TypingCharacterFormat,
            editor.Selection.TypingParagraphFormat);
        handler.ClearUndoHistoryCore();
        editor.UpdateUndoStateFromPlatform();
    }

    private static void MapPlaceholder(RichEditorHandler handler, RichEditor editor) =>
        ((IRichEditorHandler)handler).ApplyAppearance(
            RichTextAppearanceChange.Placeholder |
            RichTextAppearanceChange.PlaceholderColor);

    private static void MapAppearance(RichEditorHandler handler, RichEditor editor) =>
        ((IRichEditorHandler)handler).ApplyAppearance(
            RichTextAppearanceChange.TextColor |
            RichTextAppearanceChange.FontFamily |
            RichTextAppearanceChange.FontSize);

    private static void MapInputConfiguration(RichEditorHandler handler, RichEditor editor) =>
        handler.UpdateInputConfiguration(editor);

    private partial void UpdatePlaceholder(RichEditor editor);

    private partial void UpdateAppearance(RichEditor editor);

    private partial void UpdateInputConfiguration(RichEditor editor);
}
