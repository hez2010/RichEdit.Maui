using Android.Content.Res;
using Android.Graphics;
using Android.Text;
using Android.Util;
using Android.Views;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private static readonly RichSoftLineBreakTransformation SoftLineBreakTransformation = new();
    private bool _applyingDocument;
    private ColorStateList? _defaultHintTextColors;
    private ColorStateList? _defaultTextColors;
    private float _defaultTextSize;
    private Typeface? _defaultTypeface;
    private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
    private NativeFormatWatcher? _formatWatcher;
    private IEditable? _watchedText;
    private bool _nativeFormatReadbackQueued;
    private int _nativeTextChangeDepth;
    private int _projectionGeneration;
    private int _queuedProjectionGeneration;
    private RichTextDocument? _queuedDocument;
    private long _queuedVersion;

    /// <inheritdoc />
    protected override RichEditText CreatePlatformView()
    {
        var editor = new RichEditText(MauiContext!.Context!)
        {
            Gravity = GravityFlags.Top | GravityFlags.Start,
            InputType = InputTypes.ClassText |
                        InputTypes.TextFlagMultiLine |
                        InputTypes.TextFlagCapSentences |
                        InputTypes.TextFlagAutoCorrect,
            OverScrollMode = OverScrollMode.Always,
            VerticalScrollBarEnabled = true,
        };

        editor.SetSingleLine(false);
        editor.SetHorizontallyScrolling(false);
        _defaultHintTextColors = editor.HintTextColors;
        _defaultTextColors = editor.TextColors;
        _defaultTextSize = editor.TextSize;
        _defaultTypeface = editor.Typeface;
        var density = editor.Resources?.DisplayMetrics?.Density ?? 1f;
        editor.SetPadding(
            checked((int)Math.Round(12 * density)),
            checked((int)Math.Round(10 * density)),
            checked((int)Math.Round(12 * density)),
            checked((int)Math.Round(10 * density)));
        return editor;
    }

    /// <inheritdoc />
    protected override void ConnectHandler(RichEditText platformView)
    {
        base.ConnectHandler(platformView);
        ConnectAdornments();
        _formatWatcher = new NativeFormatWatcher(this);
        WatchNativeFormats();
        platformView.TextChanged += OnNativeDocumentChanged;
        platformView.NativeSelectionChanged += OnNativeSelectionChanged;
        platformView.EditingCompleted += OnNativeEditingCompleted;
        platformView.PasteRequested = OnPlatformPasteAsync;
        platformView.CopyRequested = VirtualView.CopyAsync;
        platformView.CutRequested = VirtualView.CutAsync;
        platformView.UndoRequested = OnPlatformUndo;
        platformView.RedoRequested = OnPlatformRedo;
        platformView.TabRequested = OnPlatformTab;
        platformView.LinkInvoked = OnPlatformLinkInvoked;
        platformView.InlineObjectInvoked = OnPlatformInlineObjectInvoked;
        platformView.KeyDownRequested = VirtualView.SendKeyDown;
        _contextMenuCallback = new EditorActionModeCallback(this);
        platformView.CustomSelectionActionModeCallback = _contextMenuCallback;
        platformView.CustomInsertionActionModeCallback = _contextMenuCallback;
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(RichEditText platformView)
    {
        VirtualView?.Commands.Disconnect();
        DisconnectAdornments();
        _watchedText?.RemoveSpan(_formatWatcher);
        _watchedText = null;
        _formatWatcher?.Dispose();
        _formatWatcher = null;
        platformView.TextChanged -= OnNativeDocumentChanged;
        _nativeTextChangeDepth = 0;
        platformView.NativeSelectionChanged -= OnNativeSelectionChanged;
        DisconnectFolding();
        platformView.EditingCompleted -= OnNativeEditingCompleted;
        platformView.PasteRequested = null;
        platformView.CopyRequested = null;
        platformView.CutRequested = null;
        platformView.UndoRequested = null;
        platformView.RedoRequested = null;
        platformView.TabRequested = null;
        platformView.LinkInvoked = null;
        platformView.InlineObjectInvoked = null;
        platformView.ResetKeyInput();
        _contextMenuCallback?.Finish();
        platformView.CustomSelectionActionModeCallback = null;
        platformView.CustomInsertionActionModeCallback = null;
        _contextMenuCallback?.Dispose();
        _contextMenuCallback = null;
        base.DisconnectHandler(platformView);
    }

    private Task OnPlatformPasteAsync(bool asPlainText) =>
        VirtualView is null || VirtualView.IsReadOnly
            ? Task.CompletedTask
            : VirtualView.PasteAsync(asPlainText);

    private void OnPlatformUndo()
    {
        if (VirtualView is { IsReadOnly: false })
        {
            VirtualView.Undo();
        }
    }

    private void OnPlatformRedo()
    {
        if (VirtualView is { IsReadOnly: false })
        {
            VirtualView.Redo();
        }
    }

    private void OnPlatformTab()
    {
        if (VirtualView is { IsReadOnly: false } editor &&
            (editor.MaxLength < 0 || editor.Document.Length - editor.SelectedRange.Length < editor.MaxLength))
        {
            editor.Selection.ReplaceText("\t");
        }
    }

    private bool OnPlatformLinkInvoked(string target)
    {
        if (VirtualView is null)
        {
            return true;
        }

        var position = Math.Clamp(NativeProjection.ToSource(PlatformView.SelectionStart), 0, VirtualView.Document.Length);
        var link = VirtualView.Document.CurrentSnapshot.Links.FirstOrDefault(candidate =>
            candidate.Start <= position && position < candidate.End &&
            string.Equals(candidate.Target, target, StringComparison.Ordinal));
        if (link is null)
        {
            link = VirtualView.Document.CurrentSnapshot.Links.FirstOrDefault(candidate =>
                string.Equals(candidate.Target, target, StringComparison.Ordinal));
        }

        return link is null || VirtualView.RaiseLinkInvoked(link);
    }

    private bool OnPlatformInlineObjectInvoked(int position)
    {
        if (VirtualView is null)
        {
            return true;
        }

        if (NativeProjection.ContainsDisplayCharacter(position))
            return false;

        position = NativeProjection.ToSource(position);
        var image = VirtualView.Document.CurrentSnapshot.Images.FirstOrDefault(candidate =>
            candidate.Position == position);
        return image is null || VirtualView.RaiseInlineObjectInvoked(image);
    }

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _nativeTypingFormat = characterFormat;
        _nativeTypingParagraphFormat = paragraphFormat;
    }

    private partial void SetNativeSelectionCore(RichTextSelectionState selection)
    {
        if (PlatformView is null)
            return;
        if (PlatformView.SelectionStart == selection.Anchor && PlatformView.SelectionEnd == selection.Active)
            return;

        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            PlatformView.SetSelection(selection.Anchor, selection.Active);
        }
        finally
        {
            _applyingDocument = wasApplying;
        }
    }

    private partial void ScrollIntoViewCore(RichTextRange range)
    {
        _adornmentScrollAnchor = null;
        PlatformView.BringPointIntoView(range.Start);
    }

    // Android's public TextView API does not expose its internal undo manager or
    // CanUndo/CanRedo state, so the portable document history remains the fallback.
    private partial bool IsComposingCore() => PlatformView.EditableText is { } text &&
        Android.Views.InputMethods.BaseInputConnection.GetComposingSpanStart(text) >= 0;

    private partial bool SupportsNativeUndoCore() => false;

    private partial bool CanUndoCore() => VirtualView?.Document.CanUndo == true;

    private partial bool CanRedoCore() => VirtualView?.Document.CanRedo == true;

    private partial void UndoCore() => OnPlatformUndo();

    private partial void RedoCore() => OnPlatformRedo();

    private partial void ClearUndoHistoryCore()
    {
        VirtualView?.Document.ClearUndoHistory();
        VirtualView?.UpdateUndoStateFromPlatform();
    }

    private partial void UpdatePlaceholder(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.PlaceholderProperty))
        {
            PlatformView.Hint = editor.Placeholder;
        }

        if (editor.IsSet(RichEditor.PlaceholderColorProperty))
        {
            if (editor.PlaceholderColor is { } placeholderColor)
            {
                PlatformView.SetHintTextColor(placeholderColor.ToPlatform());
            }
            else if (_defaultHintTextColors is not null)
            {
                PlatformView.SetHintTextColor(_defaultHintTextColors);
            }
        }
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.TextColorProperty) && editor.TextColor is { } textColor)
        {
            PlatformView.SetTextColor(textColor.ToPlatform());
        }
        else if (editor.IsSet(RichEditor.TextColorProperty) && _defaultTextColors is not null)
        {
            PlatformView.SetTextColor(_defaultTextColors);
        }

        if (editor.IsSet(RichEditor.FontSizeProperty))
        {
            if (editor.FontSize is { } fontSize)
            {
                PlatformView.SetTextSize(ComplexUnitType.Sp, (float)fontSize);
            }
            else
            {
                PlatformView.SetTextSize(ComplexUnitType.Px, _defaultTextSize);
            }
        }

        if (editor.IsSet(RichEditor.FontFamilyProperty))
        {
            PlatformView.Typeface = string.IsNullOrWhiteSpace(editor.FontFamily)
                ? _defaultTypeface
                : Typeface.Create(editor.FontFamily, TypefaceStyle.Normal);
        }

        if (!_applyingDocument)
        {
            _applyingDocument = true;
            try
            {
                if (PlatformView.EditableText is { } editable)
                {
                    ApplyCharacterFormatsIncrementally(
                        editable,
                        DisplayPresentationSnapshot,
                        new RichTextRange(0, DisplayPresentationSnapshot.Length));
                }

                SetSelectionCore(editor.SelectedRange.Start, editor.SelectedRange.Length);
                PlatformView.RequestLayout();
                PlatformView.Invalidate();
            }
            finally
            {
                _applyingDocument = false;
            }

            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
    }

    private partial void UpdateInputConfiguration(RichEditor editor)
    {
        var selection = editor.SelectedRange;
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            PlatformView.SetTextIsSelectable(editor.IsReadOnly);
            var inputType = ReferenceEquals(editor.Keyboard, Keyboard.Numeric)
                ? InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned
                : ReferenceEquals(editor.Keyboard, Keyboard.Telephone)
                    ? InputTypes.ClassPhone
                    : ReferenceEquals(editor.Keyboard, Keyboard.Email)
                        ? InputTypes.ClassText | InputTypes.TextVariationEmailAddress
                        : ReferenceEquals(editor.Keyboard, Keyboard.Url)
                            ? InputTypes.ClassText | InputTypes.TextVariationUri
                            : InputTypes.ClassText |
                              InputTypes.TextFlagMultiLine |
                              InputTypes.TextFlagCapSentences;
            if (editor.IsTextPredictionEnabled)
            {
                inputType |= InputTypes.TextFlagAutoCorrect;
            }

            if (!editor.IsSpellCheckEnabled)
            {
                inputType |= InputTypes.TextFlagNoSuggestions;
            }

            PlatformView.InputType = inputType;
            PlatformView.SetSingleLine(false);
            PlatformView.TransformationMethod = SoftLineBreakTransformation;
            if (!editor.Folding.EffectiveRanges.IsEmpty)
                ApplyFoldingCore();

            PlatformView.SetHorizontallyScrolling(false);
            // InputType installs a key listener, including when the view was read-only.
            // Apply the read-only state after configuring the requested keyboard.
            if (editor.IsReadOnly)
            {
                PlatformView.KeyListener = null;
            }
            else
            {
                // SetTextIsSelectable(false) removes the movement method.
                PlatformView.MovementMethod = global::Android.Text.Method.ArrowKeyMovementMethod.Instance;
                PlatformView.FocusableInTouchMode = true;
            }

            PlatformView.SetCursorVisible(!editor.IsReadOnly);
            PlatformView.AcceptsTab = editor.AcceptsTab;
            UpdateDisplayInputLimit();
            WatchNativeFormats();
            SetSelectionCore(selection.Start, selection.Length);
            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
        finally
        {
            _applyingDocument = wasApplying;
        }
    }
}
