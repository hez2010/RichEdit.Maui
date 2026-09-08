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

    bool IsComposing { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    void ApplySnapshot(
        RichTextDocumentSnapshot snapshot,
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
            // Native editing menus read the attached MAUI menu when they open.
            [nameof(IContextFlyoutElement.ContextFlyout)] = static (_, _) => { },
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

    bool IRichEditorHandler.IsComposing => IsComposingCore();

    private partial bool IsComposingCore();

    bool IRichEditorHandler.CanUndo => CanUndoCore();

    bool IRichEditorHandler.CanRedo => CanRedoCore();

    void IRichEditorHandler.ApplySnapshot(
        RichTextDocumentSnapshot snapshot,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
    {
        ApplyDocumentCore(snapshot, selection.Start, selection.Length);
        ApplyFoldingCore();
        ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
        if (SupportsNativeUndoCore())
        {
            ClearUndoHistoryCore();
        }

        VirtualView.UpdateUndoStateFromPlatform();
    }

    void IRichEditorHandler.ApplyChanges(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat) =>
        ApplyChangesCore(
            changes,
            selection,
            typingCharacterFormat,
            typingParagraphFormat);

    void IRichEditorHandler.ApplyDecorations(RichTextChangeSet changes) => ApplyDecorationsCore(changes);

    void IRichEditorHandler.ApplyFolding() => ApplyFoldingCore();

    private partial void ApplyFoldingCore();

    private partial void ApplyDecorationsCore(RichTextChangeSet changes);

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

    void IRichEditorHandler.SetSelection(RichTextSelectionState selection) => SetNativeSelectionCore(selection);

    void IRichEditorHandler.ScrollIntoView(RichTextRange range) => ScrollIntoViewCore(range);

    void IRichEditorHandler.ApplyTypingFormat(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat) =>
        ApplyTypingFormatCore(characterFormat, paragraphFormat);

    void IRichEditorHandler.Undo() => UndoCore();

    void IRichEditorHandler.Redo() => RedoCore();

    void IRichEditorHandler.ClearUndoHistory() => ClearUndoHistoryCore();

    private void ApplyChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
    {
        if (ReferenceEquals(changes.SourceToken, _sourceToken))
        {
            return;
        }
        if (changes.Changes.All(static change => change.Kind is RichTextChangeKind.Metadata or RichTextChangeKind.Field))
        {
            // These are model-owned values; field result edits also carry a Text
            // change. Reprojecting invisible metadata interrupts native input,
            // including an in-progress IME composition.
            return;
        }

        ApplyIncrementalChangesCore(
            changes,
            selection,
            typingCharacterFormat,
            typingParagraphFormat);
    }

    private partial void ApplyDocumentCore(
        RichTextDocumentSnapshot document,
        int selectionStart,
        int selectionLength);

    private partial void ApplyIncrementalChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat);

    private static RichTextDocumentSnapshot? GetPreviousDecorationSnapshot(RichTextChangeSet changes)
    {
        // Native formatting repairs add a separate range, or have equal projections.
        // In either case the projected snapshot is insufficient to describe native state.
        var before = changes.BeforeSnapshot;
        return changes.Changes is [{ Kind: RichTextChangeKind.CharacterFormat }] &&
            before is not null && changes.AfterSnapshot is { } after && !before.ContentEquals(after) ? before : null;
    }

    private RichTextDocumentSnapshot? GetPreviousFormattingSnapshot(RichTextChangeSet changes) =>
        changes.BeforeSnapshot is { } snapshot && changes.Changes.All(static change => change.Kind is
            RichTextChangeKind.CharacterFormat or RichTextChangeKind.ParagraphFormat or RichTextChangeKind.DefaultFormat or
            RichTextChangeKind.Metadata or RichTextChangeKind.Field)
            ? VirtualView.Decorations.Project(snapshot) : null;

    private static IEnumerable<(RichTextRange Range, RichTextCharacterFormat Format, RichTextCharacterFormat? PreviousFormat)>
        GetCharacterFormatChanges(RichTextDocumentSnapshot snapshot, RichTextRange range, RichTextDocumentSnapshot? previousSnapshot)
    {
        // A queued native repair can outlive a text edit that shortens the document.
        range = range.Clamp(snapshot.Length);
        var index = snapshot.FindRunIndex(range.Start);
        var previousIndex = previousSnapshot?.FindRunIndex(range.Start) ?? 0;
        for (var position = range.Start; position < range.End;)
        {
            var run = snapshot.Runs[index];
            var previousRun = previousSnapshot?.Runs[previousIndex];
            var end = Math.Min(run.End, range.End);
            if (previousRun is not null) end = Math.Min(end, previousRun.End);
            if (previousRun?.Format != run.Format || previousSnapshot?.DefaultCharacterFormat != snapshot.DefaultCharacterFormat)
                yield return (new(position, end - position), run.Format, previousRun?.Format);
            position = end;
            if (run.End == end) index++;
            if (previousRun?.End == end) previousIndex++;
        }
    }

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    private void SetSelectionCore(int start, int length)
    {
        var range = new RichTextRange(start, length).Clamp(VirtualView.Document.Length);
        var selection = VirtualView.SelectionState;
        SetNativeSelectionCore(selection.Range == range ? selection : RichTextSelectionState.FromRange(range));
    }

    private partial void SetNativeSelectionCore(RichTextSelectionState selection);

    private partial void ScrollIntoViewCore(RichTextRange range);

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
        handler.ApplyFoldingCore();
        handler.ApplyTypingFormatCore(
            editor.Selection.TypingCharacterFormat,
            editor.Selection.TypingParagraphFormat);
        if (handler.SupportsNativeUndoCore())
        {
            handler.ClearUndoHistoryCore();
        }

        editor.UpdateUndoStateFromPlatform();
        editor.Folding.NotifyChanged();
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
