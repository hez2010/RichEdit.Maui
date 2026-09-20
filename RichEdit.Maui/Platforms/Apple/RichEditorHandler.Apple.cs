#if IOS || MACCATALYST
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

using RichEdit.Maui.Platforms.Apple;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private static readonly NSString CharacterMetadataKey =
        new("RichEdit.Maui.CharacterFormat");
    private static readonly NSString ParagraphMetadataKey =
        new("RichEdit.Maui.ParagraphFormat");
    private static readonly NSString ImageMetadataKey =
        new("RichEdit.Maui.Image");

    private bool _applyingDocument;
    private bool _restoringHistory;
    private UIFont _defaultFont = UIFont.SystemFontOfSize(UIFont.SystemFontSize)!;
    private UIColor _defaultTextColor = UIColor.Label;
    private NSTextStorage? _observedTextStorage;
    private bool _nativeReadbackQueued;
    private long _nativeEditGeneration;
    private long _readNativeGeneration;
    private bool _applyingSelection;
    private bool _applyingTypingFormat;
    private int _projectionGeneration;
    private int _queuedProjectionGeneration;
    private RichTextDocument? _queuedDocument;
    private long _queuedVersion;
    private bool _queuedNativeContinuation;
    private RichTextDocument? _projectedDocument;
    private long? _projectedVersion;
    private UIColor _defaultTintColor = UIColor.SystemBlue;
    private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
    private RichTextViewDelegate? _textViewDelegate;
    private PendingNativeChange? _pendingNativeChange;

    /// <inheritdoc />
    protected override RichTextView CreatePlatformView()
    {
        var textView = new RichTextView
        {
            AllowsEditingTextAttributes = true,
            AutocorrectionType = UITextAutocorrectionType.Yes,
            Editable = true,
            ScrollEnabled = true,
            SpellCheckingType = UITextSpellCheckingType.Yes,
        };
        _defaultFont = textView.Font ?? UIFont.SystemFontOfSize(UIFont.SystemFontSize)!;
        _defaultTextColor = textView.TextColor ?? UIColor.Label;
        _defaultTintColor = textView.TintColor ?? UIColor.SystemBlue;
        return textView;
    }

    /// <inheritdoc />
    protected override void ConnectHandler(RichTextView platformView)
    {
        base.ConnectHandler(platformView);
        ConnectAdornments();
        _textViewDelegate = new RichTextViewDelegate(this);
        platformView.Delegate = _textViewDelegate;
        platformView.PasteRequested = OnPlatformPasteAsync;
        platformView.CopyRequested = VirtualView.CopyAsync;
        platformView.CutRequested = VirtualView.CutAsync;
        platformView.NativeAppearanceChanged = OnNativeAppearanceChanged;
        platformView.SetUndoEditor(VirtualView);
        platformView.KeyDownRequested = VirtualView.SendKeyDown;
        platformView.EditorKeyBindings = VirtualView.KeyBindings;
        VirtualView.PropertyChanged += OnEditorUndoStateChanged;
        ObserveTextStorage();
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(RichTextView platformView)
    {
        VirtualView?.Commands.Disconnect();
        DisconnectAdornments();
        _pendingNativeChange = null;
        if (_observedTextStorage is { } storage)
            storage.DidProcessEditing -= OnTextStorageProcessed;

        _observedTextStorage = null;
        if (VirtualView is { } editor)
        {
            editor.PropertyChanged -= OnEditorUndoStateChanged;
        }

        platformView.SetUndoEditor(null);
        platformView.ResetKeyInput();
        platformView.PasteRequested = null;
        platformView.CopyRequested = null;
        platformView.CutRequested = null;
        platformView.NativeAppearanceChanged = null;
        platformView.Delegate = null!;
        DisconnectFolding();
        _textViewDelegate?.Dispose();
        _textViewDelegate = null;
        base.DisconnectHandler(platformView);
    }

    private void OnEditorUndoStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RichEditor.CanUndo) or nameof(RichEditor.CanRedo) or nameof(RichEditor.IsReadOnly))
        {
            PlatformView.NotifyUndoStateChanged();
        }
    }

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _nativeTypingFormat = characterFormat;
        _nativeTypingParagraphFormat = paragraphFormat;
        if (PlatformView is null || PlatformView.SelectedRange.Length != 0)
        {
            return;
        }

        using var characterAttributes = CreateCharacterAttributes(
            characterFormat,
            VirtualView?.Document.DefaultCharacterFormat);
        using var paragraphAttributes = CreateParagraphAttributes(paragraphFormat);
        using var attributes = new NSMutableDictionary(characterAttributes);
        attributes.AddEntries(paragraphAttributes);
        var current = PlatformView.TypingAttributes2;
        if (current is not null && current.Count == attributes.Count && attributes.Keys.All(key =>
            current[key] is { } prior && (prior, attributes[key]) switch
            {
                (CharacterMetadata first, CharacterMetadata second) => first.Format == second.Format,
                (ParagraphMetadata first, ParagraphMetadata second) => first.Format == second.Format,
                (_, var value) => prior.IsEqual(value),
            }))
            return;

        var wasApplying = _applyingDocument;
        var wasApplyingTyping = _applyingTypingFormat;
        _applyingDocument = true;
        _applyingTypingFormat = true;
        try
        {
            PlatformView.TypingAttributes2 = attributes;
        }
        finally
        {
            _applyingTypingFormat = wasApplyingTyping;
            _applyingDocument = wasApplying;
        }
    }

    private partial void SetNativeSelectionCore(RichTextSelectionState selection)
    {
        if (PlatformView is null)
            return;

        var range = selection.Range;
        var current = PlatformView.SelectedRange;
        if (current.Location == range.Start && current.Length == range.Length)
            return;

        var wasApplying = _applyingSelection;
        _applyingSelection = true;
        try
        {
            PlatformView.SelectedRange = new NSRange(range.Start, range.Length);
        }
        finally
        {
            _applyingSelection = wasApplying;
        }
    }

    private partial void ScrollIntoViewCore(RichTextRange range)
    {
        _adornmentScrollAnchor = null;
        _viewportAfterLayout = null;
        PlatformView.ScrollRangeToVisible(new NSRange(range.Start, range.Length));
    }

    private partial bool IsComposingCore() => PlatformView.MarkedTextRange is not null;

    private partial bool SupportsNativeUndoCore() => false;

    private partial bool CanUndoCore() => VirtualView?.Document.CanUndo == true;

    private partial bool CanRedoCore() => VirtualView?.Document.CanRedo == true;

    private partial void UndoCore() => VirtualView?.Undo();

    private partial void RedoCore() => VirtualView?.Redo();

    private partial void ClearUndoHistoryCore() => VirtualView?.Document.ClearUndoHistory();

    private partial void UpdatePlaceholder(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.PlaceholderProperty))
        {
            PlatformView.SetPlaceholder(editor.Placeholder);
        }

        if (editor.IsSet(RichEditor.PlaceholderColorProperty))
        {
            PlatformView.SetPlaceholderColor(editor.PlaceholderColor?.ToPlatform());
        }
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        if (_applyingDocument)
        {
            return;
        }

        var snapshot = DisplayPresentationSnapshot;
        var selection = editor.SelectedRange;
        _applyingDocument = true;
        try
        {
            // UITextView.Font/TextColor rewrite attributed text and can issue
            // delegate callbacks. Keep the entire appearance update guarded.
            if (editor.IsSet(RichEditor.FontFamilyProperty) || editor.IsSet(RichEditor.FontSizeProperty))
            {
                var font = ResolveFont(RichTextCharacterFormat.Default);
                PlatformView.Font = font;
                PlatformView.SetPlaceholderFont(font);
            }

            if (editor.IsSet(RichEditor.TextColorProperty))
            {
                var textColor = editor.TextColor?.ToPlatform();
                PlatformView.TextColor = textColor ?? _defaultTextColor;
                PlatformView.TintColor = textColor ?? _defaultTintColor;
            }

            PlatformView.TextStorage.BeginEditing();
            try
            {
                ApplyCharacterFormatsIncrementally(snapshot, new RichTextRange(0, snapshot.Length));
            }
            finally
            {
                PlatformView.TextStorage.EndEditing();
            }

            SetSelectionCore(selection.Start, selection.Length);
            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
        finally
        {
            _applyingDocument = false;
        }
    }

    private partial void UpdateInputConfiguration(RichEditor editor)
    {
        var selection = editor.SelectedRange;
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            PlatformView.Editable = !editor.IsReadOnly;
            PlatformView.Selectable = true;
            if (editor.Keyboard is CustomKeyboard)
                ((IUITextInputTraits)PlatformView).ApplyKeyboard(editor.Keyboard);
            else
                PlatformView.AutocapitalizationType = editor.Keyboard == Keyboard.Default || editor.Keyboard == Keyboard.Text || editor.Keyboard == Keyboard.Chat
                    ? UITextAutocapitalizationType.Sentences : UITextAutocapitalizationType.None;
            if (editor.Keyboard is not CustomKeyboard || editor.IsSet(RichEditor.IsSpellCheckEnabledProperty))
                PlatformView.SpellCheckingType = editor.IsSpellCheckEnabled ? UITextSpellCheckingType.Yes : UITextSpellCheckingType.No;
            if (editor.Keyboard is not CustomKeyboard || editor.IsSet(RichEditor.IsTextPredictionEnabledProperty))
                PlatformView.AutocorrectionType = editor.IsTextPredictionEnabled ? UITextAutocorrectionType.Yes : UITextAutocorrectionType.No;
            PlatformView.KeyboardType = ReferenceEquals(editor.Keyboard, Keyboard.Numeric)
                ? UIKeyboardType.DecimalPad
                : ReferenceEquals(editor.Keyboard, Keyboard.Telephone)
                    ? UIKeyboardType.PhonePad
                    : ReferenceEquals(editor.Keyboard, Keyboard.Email)
                        ? UIKeyboardType.EmailAddress
                        : ReferenceEquals(editor.Keyboard, Keyboard.Url)
                            ? UIKeyboardType.Url
                            : UIKeyboardType.Default;
            SetSelectionCore(selection.Start, selection.Length);
            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
        finally
        {
            _applyingDocument = wasApplying;
        }
    }

    private static string? GetLinkTarget(NSObject? value) => value switch
    {
        null => null,
        NSUrl url => url.AbsoluteString,
        NSString text when text.Length > 0 => text.ToString(),
        _ => value.ToString(),
    };
}
#endif
