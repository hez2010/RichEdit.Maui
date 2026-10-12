#if ANDROID
using PlatformRichEditor = RichEdit.Maui.Platforms.Android.RichEditText;
#elif IOS || MACCATALYST
using PlatformRichEditor = RichEdit.Maui.Platforms.Apple.RichTextView;
#elif WINDOWS
using PlatformRichEditor = Microsoft.UI.Xaml.Controls.Control;
#endif

using Microsoft.Maui.Handlers;

namespace RichEdit.Maui;

/// <summary>
/// Connects <see cref="NativeEditorView"/> to the native rich editor for the current target.
/// </summary>
public partial class RichEditorHandler : ViewHandler<NativeEditorView, PlatformRichEditor>, IRichEditorHandler
{
    /// <summary>
    /// Gets the default property mapper used by rich-editor handlers.
    /// </summary>
    public static IPropertyMapper<NativeEditorView, RichEditorHandler> Mapper =
        new PropertyMapper<NativeEditorView, RichEditorHandler>(ViewMapper)
        {
            [nameof(NativeEditorView.Document)] = MapDocument,
            [nameof(NativeEditorView.Placeholder)] = MapPlaceholder,
            [nameof(NativeEditorView.PlaceholderColor)] = MapPlaceholder,
            [nameof(NativeEditorView.TextColor)] = MapAppearance,
            [nameof(NativeEditorView.FontFamily)] = MapAppearance,
            [nameof(NativeEditorView.FontSize)] = MapAppearance,
            [nameof(NativeEditorView.IsReadOnly)] = MapInputConfiguration,
            [nameof(NativeEditorView.IsSpellCheckEnabled)] = MapInputConfiguration,
            [nameof(NativeEditorView.IsTextPredictionEnabled)] = MapInputConfiguration,
            [nameof(NativeEditorView.Keyboard)] = MapInputConfiguration,
            [nameof(NativeEditorView.MaxLength)] = MapInputConfiguration,
            [nameof(NativeEditorView.AutoSize)] = MapInputConfiguration,
            [nameof(NativeEditorView.AcceptsTab)] = MapInputConfiguration,
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

    bool IRichEditorHandler.IsComposing => VirtualView.Composition.IsActive || IsComposingCore();

    private partial bool IsComposingCore();

    bool IRichEditorHandler.CanUndo => CanUndoCore();

    bool IRichEditorHandler.CanRedo => CanRedoCore();

    void IRichEditorHandler.ApplyDocument(
        TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
    {
        ApplyCurrentDocument(selection.Start, selection.Length);
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
        TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat) =>
            ApplyChangesCore(
                changes,
                selection,
                typingCharacterFormat,
                typingParagraphFormat);

    void IRichEditorHandler.ApplyDecorations(RichTextChangeSet changes)
    {
        if (_structuredNativeMap is not null) { ApplyStructuredDecorations(); return; }
        var updating = _updatingDisplayProjection;
        _updatingDisplayProjection = true;
        try
        {
            changes = PrepareDisplayChanges(changes);
            // A layer update can coincide with queued widget removal or resizing. Its
            // composed delta may therefore include native text/objects as well as paint.
            if (changes.Changes.Any(static change => change.Kind is not (RichTextChangeKind.CharacterFormat or RichTextChangeKind.Metadata)))
            {
                ApplyDisplayChangesCore(changes, VirtualView.SelectedRange, VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
                ApplyFoldingCore();
            }
            else
                ApplyDecorationsCore(changes);

            CompleteDisplayProjection();
        }
        finally
        {
            _updatingDisplayProjection = updating;
        }
    }

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

    void IRichEditorHandler.SetSelection(TextSelectionState selection)
    {
        if (VirtualView.SourceSession is not null && !_applyingDocument && !VirtualView.Composition.IsActive &&
            !IsComposingCore() && _projectedVirtualSpace != RequestedVirtualSpace())
            RefreshDisplayProjection(CaptureTextLayout());
        var native = _structuredNativeMap is not null && !VirtualView.Composition.IsActive ? StructuredNativeSelection() : NativeProjection.ToDisplayPrimary(selection, _virtualSpace);
        _structuredSelection = native;
        SetNativeSelectionCore(native);
    }

    void IRichEditorHandler.ScrollIntoView(TextSpan range)
    {
        ScrollIntoViewCore(NativeProjection.ToDisplay(range));
        QueueAdornmentLayout();
    }

    void IRichEditorHandler.ApplyTypingFormat(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat) =>
            ApplyTypingFormatCore(characterFormat, paragraphFormat);

    void IRichEditorHandler.Undo() => UndoCore();

    void IRichEditorHandler.Redo() => RedoCore();

    void IRichEditorHandler.ClearUndoHistory() => ClearUndoHistoryCore();

    private void ApplyChangesCore(
        RichTextChangeSet changes,
        TextSpan selection,
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
            ? ReferenceEquals(changes.AfterSnapshot, DisplayPresentationSnapshot) ? snapshot : VirtualView.Decorations.Project(snapshot) : null;

    private static IEnumerable<(TextSpan Range, RichTextCharacterFormat Format, RichTextCharacterFormat? PreviousFormat)>
        GetCharacterFormatChanges(RichTextDocumentSnapshot snapshot, TextSpan range, RichTextDocumentSnapshot? previousSnapshot)
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
            if (previousRun is not null)
                end = Math.Min(end, previousRun.End);
            if (previousRun?.Format != run.Format || previousSnapshot?.DefaultCharacterFormat != snapshot.DefaultCharacterFormat)
                yield return (new(position, end - position), run.Format, previousRun?.Format);

            position = end;
            if (run.End == end)
                index++;
            if (previousRun?.End == end)
                previousIndex++;
        }
    }

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat);

    private void SetSelectionCore(int start, int length)
    {
        var range = new TextSpan(start, length).Clamp(VirtualView.InputSnapshot.Length);
        var selection = VirtualView.SelectionState;
        SetNativeSelectionCore(NativeProjection.ToDisplayPrimary(selection.Range == range ? selection : TextSelectionState.FromRange(range), _virtualSpace));
    }

    private partial void SetNativeSelectionCore(TextSelectionState selection);

    private partial void ScrollIntoViewCore(TextSpan range);

    private partial bool SupportsNativeUndoCore();

    private partial bool CanUndoCore();

    private partial bool CanRedoCore();

    private partial void UndoCore();

    private partial void RedoCore();

    private partial void ClearUndoHistoryCore();

    private static void MapDocument(RichEditorHandler handler, NativeEditorView editor)
    {
        ((IRichEditorHandler)handler).ApplyDocument(
            editor.SelectedRange,
            editor.Selection.TypingCharacterFormat,
            editor.Selection.TypingParagraphFormat);
        editor.Folding.NotifyChanged();
    }

    private static void MapPlaceholder(RichEditorHandler handler, NativeEditorView editor) =>
        ((IRichEditorHandler)handler).ApplyAppearance(
            RichTextAppearanceChange.Placeholder |
            RichTextAppearanceChange.PlaceholderColor);

    private static void MapAppearance(RichEditorHandler handler, NativeEditorView editor) =>
        ((IRichEditorHandler)handler).ApplyAppearance(
            RichTextAppearanceChange.TextColor |
            RichTextAppearanceChange.FontFamily |
            RichTextAppearanceChange.FontSize);

    private static void MapInputConfiguration(RichEditorHandler handler, NativeEditorView editor) =>
        handler.UpdateInputConfiguration(editor);

    private partial void UpdatePlaceholder(NativeEditorView editor);

    private partial void UpdateAppearance(NativeEditorView editor);

    private partial void UpdateInputConfiguration(NativeEditorView editor);
}
