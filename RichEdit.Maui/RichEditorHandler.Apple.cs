#if IOS || MACCATALYST
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CoreGraphics;
using CoreText;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace RichEdit.Maui.Platforms.Apple
{
    /// <summary>Provides the Apple native text view used by <see cref="RichEditorHandler"/>.</summary>
    public class RichTextView : UITextView
    {
        private readonly UIColor _defaultPlaceholderColor;
        private readonly UILabel _placeholderLabel;
        private readonly DocumentUndoManager _documentUndoManager = new();

        /// <inheritdoc />
        public override NSUndoManager? UndoManager => _documentUndoManager ?? base.UndoManager;

        internal void SetUndoEditor(RichEditor? editor) => _documentUndoManager.SetEditor(editor);

        internal void NotifyUndoStateChanged() => _documentUndoManager.NotifyStateChanged();

        internal Func<Task>? PasteRequested { get; set; }

        internal Func<Task>? CopyRequested { get; set; }

        internal Func<Task>? CutRequested { get; set; }

        internal Action? NativeAppearanceChanged { get; set; }

        /// <summary>Initializes the native rich-text view.</summary>
        public RichTextView()
        {
            _placeholderLabel = new UILabel
            {
                BackgroundColor = UIColor.Clear,
                Lines = 0,
                UserInteractionEnabled = false,
            };
            _defaultPlaceholderColor = _placeholderLabel.TextColor ?? UIColor.Label;

            AddSubview(_placeholderLabel);
            TextContainerInset = new UIEdgeInsets(10, 8, 10, 8);
        }

        /// <summary>Sets placeholder text.</summary>
        /// <param name="text">The placeholder text.</param>
        public void SetPlaceholder(string? text)
        {
            _placeholderLabel.Text = text;
            UpdatePlaceholderVisibility();
            SetNeedsLayout();
        }

        /// <summary>Sets the placeholder color.</summary>
        /// <param name="color">The color, or null for the native default.</param>
        public void SetPlaceholderColor(UIColor? color) =>
            _placeholderLabel.TextColor = color ?? _defaultPlaceholderColor;

        /// <summary>Sets the placeholder font.</summary>
        /// <param name="font">The native font.</param>
        public void SetPlaceholderFont(UIFont font) => _placeholderLabel.Font = font;

        /// <summary>Updates placeholder visibility from current content.</summary>
        public void UpdatePlaceholderVisibility() =>
            _placeholderLabel.Hidden = !string.IsNullOrEmpty(Text);

        /// <inheritdoc />
        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            var inset = TextContainerInset;
            var x = inset.Left + TextContainer.LineFragmentPadding;
            var width = Math.Max(0, Bounds.Width - x - inset.Right - TextContainer.LineFragmentPadding);
            var size = _placeholderLabel.SizeThatFits(new CGSize(width, nfloat.MaxValue));
            _placeholderLabel.Frame = new CGRect(x, inset.Top, width, size.Height);
        }

        /// <inheritdoc />
        public override async void Paste(NSObject? sender)
        {
            if (PasteRequested is { } pasteRequested)
            {
                await RichEditorCommands.ExecuteAsync(pasteRequested);
                return;
            }

            base.Paste(sender);
        }

        /// <inheritdoc />
        public override async void Copy(NSObject? sender)
        {
            if (CopyRequested is { } copy)
            {
                await RichEditorCommands.ExecuteAsync(copy);
                return;
            }

            base.Copy(sender);
        }

        /// <inheritdoc />
        public override async void Cut(NSObject? sender)
        {
            if (CutRequested is { } cut)
            {
                await RichEditorCommands.ExecuteAsync(cut);
                return;
            }

            base.Cut(sender);
        }

#pragma warning disable CA1422 // Required on iOS 15-16; the callback remains valid on 17+.
        /// <inheritdoc />
        public override void TraitCollectionDidChange(UITraitCollection? previousTraitCollection)
        {
            base.TraitCollectionDidChange(previousTraitCollection);
            if (previousTraitCollection is not null &&
                previousTraitCollection.UserInterfaceStyle != TraitCollection.UserInterfaceStyle)
            {
                NativeAppearanceChanged?.Invoke();
            }
        }
#pragma warning restore CA1422

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PasteRequested = null;
                CopyRequested = null;
                CutRequested = null;
                NativeAppearanceChanged = null;
                _placeholderLabel.Dispose();
                _documentUndoManager.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class DocumentUndoManager : NSUndoManager
        {
            private WeakReference<RichEditor>? _editor;

            public DocumentUndoManager() => DisableUndoRegistration();

            public void SetEditor(RichEditor? editor) =>
                _editor = editor is null ? null : new WeakReference<RichEditor>(editor);

            private RichEditor? Editor => _editor?.TryGetTarget(out var editor) == true ? editor : null;

            public override bool CanUndo => Editor is { IsReadOnly: false, CanUndo: true };
            public override bool CanRedo => Editor is { IsReadOnly: false, CanRedo: true };
            public override void Undo() { if (CanUndo) Editor?.Undo(); }
            public override void Redo() { if (CanRedo) Editor?.Redo(); }

            public void NotifyStateChanged()
            {
                // This manager exposes document history; it has no native groups.
                // Posting native undo/group notifications makes UIKit replay its
                // own text-service adjustments a second time.
                WillChangeValue("canUndo");
                WillChangeValue("canRedo");
                DidChangeValue("canRedo");
                DidChangeValue("canUndo");
            }
        }
    }
}

namespace RichEdit.Maui
{
    using RichEdit.Maui.Platforms.Apple;

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
            _textViewDelegate = new RichTextViewDelegate(this);
            platformView.Delegate = _textViewDelegate;
            platformView.PasteRequested = OnPlatformPasteAsync;
            platformView.CopyRequested = VirtualView.CopyAsync;
            platformView.CutRequested = VirtualView.CutAsync;
            platformView.NativeAppearanceChanged = OnNativeAppearanceChanged;
            platformView.SetUndoEditor(VirtualView);
            VirtualView.PropertyChanged += OnEditorUndoStateChanged;
            ObserveTextStorage();
        }

        /// <inheritdoc />
        protected override void DisconnectHandler(RichTextView platformView)
        {
            VirtualView?.Commands.Disconnect();
            _pendingNativeChange = null;
            if (_observedTextStorage is { } storage) storage.DidProcessEditing -= OnTextStorageProcessed;
            _observedTextStorage = null;
            if (VirtualView is { } editor)
            {
                editor.PropertyChanged -= OnEditorUndoStateChanged;
            }
            platformView.SetUndoEditor(null);
            platformView.PasteRequested = null;
            platformView.CopyRequested = null;
            platformView.CutRequested = null;
            platformView.NativeAppearanceChanged = null;
            platformView.Delegate = null!;
            _textViewDelegate?.Dispose();
            _textViewDelegate = null;
            base.DisconnectHandler(platformView);
        }

        private partial void ApplyDocumentCore(
            RichTextDocumentSnapshot document,
            int selectionStart,
            int selectionLength)
        {
            if (PlatformView is null)
            {
                return;
            }

            _pendingNativeChange = null;
            var wasApplying = _applyingDocument;
            _applyingDocument = true;
            _projectionGeneration++;
            List<NSTextList>? ownedTextLists = null;
            try
            {
                var attributed = new NSMutableAttributedString(document.Text);
                Dictionary<int, NSTextList[]>? textListsByParagraph = null;
                if (OperatingSystem.IsIOSVersionAtLeast(16) ||
                    OperatingSystem.IsMacCatalystVersionAtLeast(16))
                {
                    ownedTextLists = [];
                    textListsByParagraph = CreateNativeTextLists(document, ownedTextLists);
                }

                if (document.Text.Length > 0)
                {
                    var fullRange = new NSRange(0, document.Text.Length);
                    using (var attributes = CreateCharacterAttributes(document.DefaultCharacterFormat))
                    {
                        attributed.SetAttributes(attributes, fullRange);
                    }

                    foreach (var run in document.Runs)
                    {
                        using var attributes = CreateCharacterAttributes(
                            run.Format,
                            document.DefaultCharacterFormat);
                        attributed.SetAttributes(
                            attributes,
                            new NSRange(run.Start, run.Length));
                    }

                    foreach (var paragraph in document.Paragraphs)
                    {
                        var end = GetParagraphEnd(document.Text, paragraph.Start);
                        if (end <= paragraph.Start)
                        {
                            continue;
                        }

                        NSTextList[]? textLists = null;
                        textListsByParagraph?.TryGetValue(paragraph.Start, out textLists);
                        using var attributes = CreateParagraphAttributes(paragraph.Format, textLists);
                        attributed.AddAttributes(
                            attributes,
                            new NSRange(paragraph.Start, end - paragraph.Start));
                    }

                    foreach (var link in document.Links)
                    {
                        attributed.AddAttribute(
                            UIStringAttributeKey.Link,
                            new NSString(link.Target),
                            new NSRange(link.Start, link.Length));
                    }

                    foreach (var image in document.Images)
                    {
                        ApplyImage(attributed, image);
                    }
                }

                if (_restoringHistory)
                {
                    // Drop UIKit's prior replacement context before replaying
                    // the snapshot; otherwise a pending smart replacement can be
                    // applied again to the restored text. Native features stay on.
                    using var empty = new NSAttributedString(string.Empty);
                    PlatformView.AttributedText = empty;
                }
                PlatformView.AttributedText = attributed;
                ObserveTextStorage();
                SetSelectionCore(selectionStart, selectionLength);
                ApplyTrailingEmptyParagraphTypingFormat(
                    document,
                    selectionStart,
                    selectionLength);
                PlatformView.UpdatePlaceholderVisibility();
            }
            finally
            {
                if (ownedTextLists is not null)
                {
                    foreach (var textList in ownedTextLists)
                    {
                        textList.Dispose();
                    }
                }

                _applyingDocument = wasApplying;
                if (!wasApplying) QueueProjectionReadback();
            }
        }

        private partial void ApplyIncrementalChangesCore(
            RichTextChangeSet changes,
            RichTextRange selection,
            RichTextCharacterFormat typingCharacterFormat,
            RichTextParagraphFormat typingParagraphFormat)
        {
            var viewport = PlatformView.ContentOffset;
            // UITextView must own authored replacements so its input context sees
            // one coherent content/format update. Direct storage mutations can be
            // interpreted as new input and trigger additional text transformations.
            var wasApplying = _applyingDocument;
            var wasRestoring = _restoringHistory;
            _restoringHistory = changes.Origin is RichTextChangeOrigin.Undo or RichTextChangeOrigin.Redo;
            _applyingDocument = true;
            try
            {
                ApplyDocumentCore(VirtualView.Document.CurrentSnapshot, selection.Start, selection.Length);
                ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
                if (!changes.IsTextChanged || _restoringHistory)
                {
                    // Replacing attributed content resets UIKit's scroll position.
                    // Formatting and history replay must keep the user's viewport.
                    PlatformView.LayoutIfNeeded();
                    var inset = PlatformView.AdjustedContentInset;
                    var maximumX = Math.Max(-inset.Left, PlatformView.ContentSize.Width - PlatformView.Bounds.Width + inset.Right);
                    var maximumY = Math.Max(-inset.Top, PlatformView.ContentSize.Height - PlatformView.Bounds.Height + inset.Bottom);
                    PlatformView.SetContentOffset(new CGPoint(
                        Math.Clamp(viewport.X, -inset.Left, maximumX),
                        Math.Clamp(viewport.Y, -inset.Top, maximumY)), false);
                }
            }
            finally
            {
                _applyingDocument = wasApplying;
                _restoringHistory = wasRestoring;
                if (!wasApplying) QueueProjectionReadback();
            }
        }
        private void OnEditorUndoStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(RichEditor.CanUndo) or nameof(RichEditor.CanRedo) or nameof(RichEditor.IsReadOnly))
            {
                PlatformView.NotifyUndoStateChanged();
            }
        }
        private void ApplyCharacterFormatsIncrementally(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range)
        {
            if (range.IsEmpty || snapshot.Length == 0)
            {
                return;
            }

            for (var index = snapshot.FindRunIndex(range.Start);
                 index < snapshot.Runs.Length;
                 index++)
            {
                var run = snapshot.Runs[index];
                if (run.Start >= range.End)
                {
                    break;
                }

                var start = Math.Max(run.Start, range.Start);
                var end = Math.Min(run.End, range.End);
                if (end <= start)
                {
                    continue;
                }

                using var attributes = CreateCharacterAttributes(
                    run.Format,
                    snapshot.DefaultCharacterFormat);
                var nativeRange = new NSRange(start, end - start);
                RemoveOptionalCharacterAttributes(nativeRange);
                PlatformView.TextStorage.AddAttributes(attributes, nativeRange);
            }
        }

        private void RemoveOptionalCharacterAttributes(NSRange range)
        {
            // Character refreshes must not replace paragraph styles, links, list
            // metadata, or text attachments. Remove only character attributes
            // which CreateCharacterAttributes may intentionally omit, then merge
            // the current character attributes into the attributed string.
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.BackgroundColor, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.UnderlineColor, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.StrikethroughColor, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.StrokeColor, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.StrokeWidth, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.Shadow, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.Ligature, range);
            PlatformView.TextStorage.RemoveAttribute(UIStringAttributeKey.WritingDirection, range);
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
            var attributes = new NSMutableDictionary(characterAttributes);
            attributes.AddEntries(paragraphAttributes);
            var wasApplying = _applyingDocument;
            var wasApplyingTyping = _applyingTypingFormat;
            _applyingDocument = true;
            _applyingTypingFormat = true;
            try { PlatformView.TypingAttributes2 = attributes; }
            finally
            {
                _applyingTypingFormat = wasApplyingTyping;
                _applyingDocument = wasApplying;
            }
        }

        private partial void SetSelectionCore(int start, int length)
        {
            if (PlatformView is null)
            {
                return;
            }

            var textLength = PlatformView.Text?.Length ?? 0;
            start = Math.Clamp(start, 0, textLength);
            length = Math.Clamp(length, 0, textLength - start);
            var wasApplying = _applyingSelection;
            _applyingSelection = true;
            try { PlatformView.SelectedRange = new NSRange(start, length); }
            finally { _applyingSelection = wasApplying; }
        }

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

            var snapshot = editor.Document.CurrentSnapshot;
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
            finally { _applyingDocument = false; }
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
                PlatformView.SpellCheckingType = editor.IsSpellCheckEnabled
                    ? UITextSpellCheckingType.Yes
                    : UITextSpellCheckingType.No;
                PlatformView.AutocorrectionType = editor.IsTextPredictionEnabled
                    ? UITextAutocorrectionType.Yes
                    : UITextAutocorrectionType.No;
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
            finally { _applyingDocument = wasApplying; }
        }

        private NSMutableDictionary CreateCharacterAttributes(
            RichTextCharacterFormat format,
            RichTextCharacterFormat? inheritedFormat = null)
        {
            var authoredFormat = format;
            if (inheritedFormat is not null)
            {
                format = format with
                {
                    FontFamily = format.FontFamily ?? inheritedFormat.FontFamily,
                    FontSize = format.FontSize ?? inheritedFormat.FontSize,
                    ForegroundColor = format.ForegroundColor ?? inheritedFormat.ForegroundColor,
                };
            }

            var foreground = format.Hidden
                ? UIColor.Clear
                : format.ForegroundColor?.ToPlatform() ??
                  VirtualView.TextColor?.ToPlatform() ??
                  _defaultTextColor;
            var font = ResolveFont(format);
            var attributes = new UIStringAttributes
            {
                Font = font,
                ForegroundColor = foreground,
                UnderlineStyle = ToNativeUnderline(format.Underline),
                StrikethroughStyle = format.Strikethrough switch
                {
                    RichTextStrikethroughStyle.Double => NSUnderlineStyle.Double,
                    RichTextStrikethroughStyle.Single => NSUnderlineStyle.Single,
                    _ => NSUnderlineStyle.None,
                },
                BaselineOffset = (float)GetNativeBaselineOffset(format, font.PointSize),
                KerningAdjustment = (float)format.CharacterSpacing,
                Expansion = (float)(format.HorizontalScale - 1d),
            };

            if (format.BackgroundColor is not null)
            {
                attributes.BackgroundColor = format.BackgroundColor.ToPlatform();
            }

            if (format.UnderlineColor is not null)
            {
                attributes.UnderlineColor = format.UnderlineColor.ToPlatform();
            }

            if (format.StrikethroughColor is not null)
            {
                attributes.StrikethroughColor = format.StrikethroughColor.ToPlatform();
            }

            if (format.Outline)
            {
                attributes.StrokeColor = foreground;
                attributes.StrokeWidth = -3f;
            }

            if (format.Shadow)
            {
                attributes.Shadow = new NSShadow
                {
                    ShadowBlurRadius = 1,
                    ShadowColor = foreground.ColorWithAlpha(0.55f),
                    ShadowOffset = new CGSize(1, 1),
                };
            }

            if (format.Ligatures != RichTextFeatureMode.Automatic)
            {
                attributes.Ligature = format.Ligatures == RichTextFeatureMode.Enabled
                    ? NSLigatureType.Default
                    : NSLigatureType.None;
            }

            if (format.Direction != RichTextDirection.Automatic)
            {
                attributes.WritingDirectionInt =
                [
                    NSNumber.FromInt32(format.Direction == RichTextDirection.RightToLeft
                        ? (int)NSWritingDirection.RightToLeft
                        : (int)NSWritingDirection.LeftToRight),
                ];
            }

            var dictionary = new NSMutableDictionary(attributes.Dictionary);
            dictionary[CharacterMetadataKey] = new CharacterMetadata(authoredFormat);
            return dictionary;
        }

        private NSMutableDictionary CreateParagraphAttributes(
            RichTextParagraphFormat format,
            NSTextList[]? textLists = null)
        {
            var style = new NSMutableParagraphStyle
            {
                Alignment = format.Alignment switch
                {
                    RichTextAlignment.Center => UITextAlignment.Center,
                    RichTextAlignment.Right => UITextAlignment.Right,
                    RichTextAlignment.Justified or RichTextAlignment.Distributed =>
                        UITextAlignment.Justified,
                    _ => UITextAlignment.Left,
                },
                BaseWritingDirection = format.Direction switch
                {
                    RichTextDirection.LeftToRight => NSWritingDirection.LeftToRight,
                    RichTextDirection.RightToLeft => NSWritingDirection.RightToLeft,
                    _ => NSWritingDirection.Natural,
                },
                HeadIndent = (nfloat)format.LeadingIndent,
                FirstLineHeadIndent = (nfloat)(format.LeadingIndent + format.FirstLineIndent),
                TailIndent = format.TrailingIndent == 0 ? 0 : (nfloat)(-format.TrailingIndent),
                ParagraphSpacingBefore = (nfloat)format.SpaceBefore,
                ParagraphSpacing = (nfloat)format.SpaceAfter,
                HyphenationFactor = format.Hyphenation ? 1f : 0f,
                MinimumLineHeight = (nfloat)(format.MinimumLineHeight ?? 0),
                MaximumLineHeight = (nfloat)(format.MaximumLineHeight ?? 0),
            };

            switch (format.LineSpacingRule)
            {
                case RichTextLineSpacingRule.OneAndHalf:
                    style.LineHeightMultiple = 1.5f;
                    break;
                case RichTextLineSpacingRule.Double:
                    style.LineHeightMultiple = 2f;
                    break;
                case RichTextLineSpacingRule.Multiple:
                    style.LineHeightMultiple = (nfloat)format.LineSpacing;
                    break;
                case RichTextLineSpacingRule.Exactly:
                    style.MinimumLineHeight = (nfloat)format.LineSpacing;
                    style.MaximumLineHeight = (nfloat)format.LineSpacing;
                    break;
                case RichTextLineSpacingRule.AtLeast:
                    style.MinimumLineHeight = (nfloat)format.LineSpacing;
                    break;
                default:
                    style.LineSpacing = (nfloat)format.LineSpacing;
                    break;
            }

            if (!format.TabStops.IsDefaultOrEmpty)
            {
                style.TabStops = format.TabStops
                    .Select(tab => new NSTextTab(
                        tab.Alignment switch
                        {
                            RichTextTabAlignment.Center => UITextAlignment.Center,
                            RichTextTabAlignment.Right => UITextAlignment.Right,
                            RichTextTabAlignment.Decimal => UITextAlignment.Natural,
                            _ => UITextAlignment.Left,
                        },
                        (nfloat)tab.Position,
                        new NSDictionary()))
                    .ToArray();
            }

            if (format.NativeList is { } list)
            {
                if (OperatingSystem.IsIOSVersionAtLeast(16) ||
                    OperatingSystem.IsMacCatalystVersionAtLeast(16))
                {
                    if (textLists is null)
                    {
                        ApplyNativeTextList(style, list);
                    }
                    else
                    {
                        style.TextLists = textLists;
                    }
                }
            }

            var attributes = new UIStringAttributes { ParagraphStyle = style };
            var dictionary = new NSMutableDictionary(attributes.Dictionary);
            dictionary[ParagraphMetadataKey] = new ParagraphMetadata(format);
            return dictionary;
        }

        private void ApplyImage(NSMutableAttributedString attributed, RichTextImage image)
        {
            if (image.Position < 0 || image.Position >= attributed.Length)
            {
                return;
            }

            var bytes = image.Data.IsDefaultOrEmpty
                ? null
                : ImmutableCollectionsMarshal.AsArray(image.Data);
            NSTextAttachment attachment;
            if (bytes is null)
            {
                attachment = new NSTextAttachment();
            }
            else
            {
                using var data = NSData.FromArray(bytes);
                using var renderedImage = UIImage.LoadFromData(data);
                if (renderedImage is null)
                {
                    attachment = new NSTextAttachment();
                }
                else
                {
                    if (!string.IsNullOrEmpty(image.AlternativeText))
                    {
                        renderedImage.AccessibilityLabel = image.AlternativeText;
                    }

                    attachment = NSTextAttachment.Create(renderedImage);
                }
            }

            var characterAttributes = attributed.GetAttributes(image.Position, out _);
            var font = characterAttributes is null
                ? null
                : new UIStringAttributes(characterAttributes).Font;
            attachment.Bounds = new CGRect(
                0,
                GetImageVerticalOffset(font, image.Height, image.VerticalAlignment),
                image.Width,
                image.Height);
            var attributes = new UIStringAttributes { TextAttachment = attachment };
            var dictionary = new NSMutableDictionary(attributes.Dictionary);
            dictionary[ImageMetadataKey] = new ImageMetadata(image);
            attributed.AddAttributes(dictionary, new NSRange(image.Position, 1));
        }

        private RichTextDocumentSnapshot ReadDocumentFromPlatform()
        {
            var attributed = PlatformView.TextStorage;
            var text = attributed.Value ?? string.Empty;
            var previous = VirtualView.Document.CurrentSnapshot;
            var replacement = _pendingNativeChange is { } pending && pending.Version == previous.Version
                ? new RichTextRange(pending.Start, pending.RemovedLength)
                : VirtualView.SelectedRange;
            var remappedPrevious = previous.RemapText(text, replacement);
            var defaultCharacterFormat = previous.DefaultCharacterFormat;

            var runs = new List<RichTextRun>();
            var links = new List<RichTextLink>();
            var images = new List<RichTextImage>();
            string? activeLink = null;
            var activeLinkStart = 0;
            var priorRunIndex = 0;
            for (var position = 0; position < text.Length;)
            {
                var dictionary = attributed.GetAttributes(position, out var effectiveRange) ??
                    new NSDictionary();
                var effectiveEnd = Math.Min(
                    text.Length,
                    checked((int)(effectiveRange.Location + effectiveRange.Length)));
                while (remappedPrevious.Runs[priorRunIndex].End <= position) priorRunIndex++;
                var priorRun = remappedPrevious.Runs[priorRunIndex];
                var end = Math.Min(priorRun.End, Math.Max(position + 1, effectiveEnd));
                var format = ReadCharacterFormat(dictionary, priorRun.Format);
                if (runs.Count > 0 && runs[^1].Format == format)
                {
                    runs[^1] = runs[^1] with { Length = runs[^1].Length + end - position };
                }
                else
                {
                    runs.Add(new RichTextRun(position, end - position, format));
                }

                var attributes = new UIStringAttributes(dictionary);
                // Link attributes may contain NSString as well as NSURL; the
                // typed UIStringAttributes.Link accessor only exposes NSURL.
                var link = GetLinkTarget(dictionary[UIStringAttributeKey.Link]);
                if (!string.Equals(activeLink, link, StringComparison.Ordinal))
                {
                    if (activeLink is not null)
                    {
                        links.Add(new RichTextLink(
                            activeLinkStart,
                            position - activeLinkStart,
                            activeLink));
                    }

                    activeLink = link;
                    activeLinkStart = position;
                }

                if (attributes.TextAttachment is { } attachment)
                {
                    for (var imagePosition = text.IndexOf(
                             RichTextDocument.ObjectReplacementCharacter,
                             position,
                             end - position);
                         imagePosition >= 0;
                         imagePosition = text.IndexOf(
                             RichTextDocument.ObjectReplacementCharacter,
                             imagePosition + 1,
                             end - imagePosition - 1))
                    {
                        images.Add(ReadImage(dictionary, attachment, imagePosition));
                    }
                }

                position = end;
            }

            if (activeLink is not null)
            {
                links.Add(new RichTextLink(
                    activeLinkStart,
                    text.Length - activeLinkStart,
                    activeLink));
            }

            var paragraphs = new List<RichTextParagraph>();
            RichTextListFormat? previousList = null;
            var assignedListIds = new HashSet<int>();
            var nextListId = checked(previous.Lists.Keys
                .Select(static id => id.Value)
                .DefaultIfEmpty()
                .Max() + 1);
            for (var start = 0; ;)
            {
                RichTextParagraphFormat format;
                if (text.Length == 0)
                {
                    format = previous.DefaultParagraphFormat;
                }
                else
                {
                    var index = Math.Min(start, text.Length - 1);
                    format = ReadParagraphFormat(
                        attributed.GetAttributes(index, out _) ?? new NSDictionary(),
                        remappedPrevious.GetParagraphFormat(start));
                }

                if (format.NativeList is { } list)
                {
                    // A split paragraph inherits its native attributed metadata,
                    // including the old restart flag. The remapped document knows
                    // which item actually owned that restart before the edit.
                    if (format.List is { } item &&
                        remappedPrevious.GetParagraphFormat(Math.Min(start, remappedPrevious.Length)).List is { } priorItem &&
                        item.ListId == priorItem.ListId && item.Level == priorItem.Level)
                    {
                        format = format with { List = priorItem };
                        list = list with { Restart = priorItem.RestartAt is not null, StartAt = priorItem.RestartAt ?? list.StartAt };
                        format = format with { NativeList = list };
                    }

                    if (list.Id <= 0)
                    {
                        var continues = previousList is not null &&
                            previousList.Kind == list.Kind &&
                            previousList.Level == list.Level;
                        var priorId = remappedPrevious.GetParagraphFormat(
                            Math.Min(start, remappedPrevious.Length)).List?.ListId.Value;
                        var id = continues
                            ? previousList!.Id
                            : priorId is > 0 && assignedListIds.Add(priorId.Value)
                                ? priorId.Value
                                : nextListId++;
                        assignedListIds.Add(id);
                        list = list with { Id = id };
                        format = format with { NativeList = list };
                    }
                    else
                    {
                        assignedListIds.Add(list.Id);
                    }

                    previousList = list;
                }
                else
                {
                    previousList = null;
                }

                paragraphs.Add(new RichTextParagraph(start, format));
                var newline = text.IndexOf('\n', start);
                if (newline < 0)
                {
                    break;
                }

                start = newline + 1;
            }

            return previous.MergeNativeSnapshot(
                text,
                runs,
                paragraphs,
                links,
                images,
                defaultCharacterFormat,
                previous.DefaultParagraphFormat,
                remappedSnapshot: remappedPrevious);
        }

        private RichTextCharacterFormat ReadCharacterFormat(
            NSDictionary dictionary,
            RichTextCharacterFormat defaultFormat)
        {
            var metadata = dictionary[CharacterMetadataKey] as CharacterMetadata;
            var format = metadata?.Format ?? defaultFormat;
            var attributes = new UIStringAttributes(dictionary);
            if (attributes.Font is { } font)
            {
                var traits = font.FontDescriptor.SymbolicTraits;
                // UIKit can rebuild attributes without our metadata during a
                // selection update. Equivalent defaults must remain inherited.
                var expectedFont = ResolveFont(
                    VirtualView.Document.CurrentSnapshot.ResolveCharacterFormat(format));
                var bold = traits.HasFlag(UIFontDescriptorSymbolicTraits.Bold);
                format = format with
                {
                    FontFamily = font.FamilyName != expectedFont.FamilyName
                        ? font.FamilyName
                        : format.FontFamily,
                    FontSize = font.PointSize != expectedFont.PointSize
                        ? font.PointSize
                        : format.FontSize,
                    FontWeight = bold == format.Bold ? format.FontWeight : bold ? 700 : 400,
                    Italic = traits.HasFlag(UIFontDescriptorSymbolicTraits.Italic),
                };
            }

            if (attributes.ForegroundColor is { } foreground && !format.Hidden)
            {
                var expected = format.ForegroundColor ?? VirtualView.Document.DefaultCharacterFormat.ForegroundColor ??
                    VirtualView.TextColor ?? FromUIColor(_defaultTextColor);
                var color = FromUIColor(foreground);
                if (!color.Equals(expected))
                {
                    format = format with { ForegroundColor = color };
                }
            }

            format = format with
            {
                BackgroundColor = attributes.BackgroundColor is { } background ? FromUIColor(background) : null,
            };

            var underline = attributes.UnderlineStyle ?? NSUnderlineStyle.None;
            if (underline != ToNativeUnderline(format.Underline))
            {
                format = format with
                {
                    Underline = FromNativeUnderline(underline),
                };
            }

            var strikethrough = attributes.StrikethroughStyle ?? NSUnderlineStyle.None;
            var expectedStrikethrough = format.Strikethrough switch
            {
                RichTextStrikethroughStyle.Double => NSUnderlineStyle.Double,
                RichTextStrikethroughStyle.Single => NSUnderlineStyle.Single,
                _ => NSUnderlineStyle.None,
            };
            if (strikethrough != expectedStrikethrough)
            {
                format = format with
                {
                    Strikethrough = strikethrough switch
                    {
                        NSUnderlineStyle.Double => RichTextStrikethroughStyle.Double,
                        NSUnderlineStyle.None => RichTextStrikethroughStyle.None,
                        _ => RichTextStrikethroughStyle.Single,
                    },
                };
            }

            format = format with
            {
                UnderlineColor = attributes.UnderlineColor is { } underlineColor ? FromUIColor(underlineColor) : null,
                StrikethroughColor = attributes.StrikethroughColor is { } strikeColor ? FromUIColor(strikeColor) : null,
            };

            if (metadata is null)
            {
                var baseline = attributes.BaselineOffset ?? 0;
                var kerning = attributes.KerningAdjustment ?? 0;
                var expansion = attributes.Expansion ?? 0;
                format = format with
                {
                    BaselineOffset = baseline,
                    Script = baseline > 0
                        ? RichTextScript.Superscript
                        : baseline < 0
                            ? RichTextScript.Subscript
                            : RichTextScript.Normal,
                    CharacterSpacing = kerning,
                    HorizontalScale = Math.Max(1d + expansion, 0.01d),
                    Outline = attributes.StrokeWidth is not null and not 0,
                    Shadow = attributes.Shadow is not null,
                };
            }

            format = format with
            {
                Direction = attributes.WritingDirectionInt?.FirstOrDefault()?.Int32Value switch
                {
                    1 or 3 => RichTextDirection.RightToLeft,
                    0 or 2 => RichTextDirection.LeftToRight,
                    _ => RichTextDirection.Automatic,
                },
            };

            return format;
        }

        private static RichTextParagraphFormat ReadParagraphFormat(
            NSDictionary dictionary,
            RichTextParagraphFormat defaultFormat)
        {
            var metadata = dictionary[ParagraphMetadataKey] as ParagraphMetadata;
            var format = metadata?.Format ?? defaultFormat;
            var style = new UIStringAttributes(dictionary).ParagraphStyle;
            if (style is null)
            {
                return OperatingSystem.IsIOSVersionAtLeast(16) ||
                    OperatingSystem.IsMacCatalystVersionAtLeast(16)
                    ? format with { List = null, NativeList = null }
                    : format;
            }

            RichTextListFormat? list = format.NativeList;
            if (OperatingSystem.IsIOSVersionAtLeast(16) ||
                OperatingSystem.IsMacCatalystVersionAtLeast(16))
            {
                var nativeList = ReadNativeTextList(style);
                // A paragraph can retain inherited metadata after its native list is removed.
                // Native list membership is authoritative; metadata preserves richer details.
                list = nativeList is null
                    ? null
                    : list ?? nativeList;
            }

            var lineSpacingRule = RichTextLineSpacingRule.Automatic;
            var lineSpacing = (double)style.LineSpacing;
            if (style.MinimumLineHeight > 0 && style.MaximumLineHeight == style.MinimumLineHeight)
            {
                lineSpacingRule = RichTextLineSpacingRule.Exactly;
                lineSpacing = style.MinimumLineHeight;
            }
            else if (style.MinimumLineHeight > 0)
            {
                lineSpacingRule = RichTextLineSpacingRule.AtLeast;
                lineSpacing = style.MinimumLineHeight;
            }
            else if (style.LineHeightMultiple > 0)
            {
                lineSpacingRule = Math.Abs((double)style.LineHeightMultiple - 1.5d) < 0.001d
                    ? RichTextLineSpacingRule.OneAndHalf
                    : Math.Abs((double)style.LineHeightMultiple - 2d) < 0.001d
                        ? RichTextLineSpacingRule.Double
                        : RichTextLineSpacingRule.Multiple;
                lineSpacing = style.LineHeightMultiple;
            }

            var expectedMinimum = format.MinimumLineHeight ?? 0;
            var expectedMaximum = format.MaximumLineHeight ?? 0;
            var expectedMultiple = 0d;
            var expectedSpacing = 0d;
            switch (format.LineSpacingRule)
            {
                case RichTextLineSpacingRule.Exactly:
                    expectedMinimum = expectedMaximum = format.LineSpacing;
                    break;
                case RichTextLineSpacingRule.AtLeast:
                    expectedMinimum = format.LineSpacing;
                    break;
                case RichTextLineSpacingRule.OneAndHalf:
                    expectedMultiple = 1.5;
                    break;
                case RichTextLineSpacingRule.Double:
                    expectedMultiple = 2;
                    break;
                case RichTextLineSpacingRule.Multiple:
                    expectedMultiple = format.LineSpacing;
                    break;
                default:
                    expectedSpacing = format.LineSpacing;
                    break;
            }

            var preservesLineSpacing =
                style.MinimumLineHeight == expectedMinimum && style.MaximumLineHeight == expectedMaximum &&
                style.LineHeightMultiple == expectedMultiple && style.LineSpacing == expectedSpacing;
            var nativeTabs = style.TabStops ?? [];
            var defaultTabs = NSParagraphStyle.Default.TabStops ?? [];
            var preservesTabs = format.TabStops.IsDefaultOrEmpty
                ? nativeTabs.Length == defaultTabs.Length && nativeTabs.Zip(defaultTabs).All(pair =>
                    pair.First.Location == pair.Second.Location && pair.First.Alignment == pair.Second.Alignment)
                : nativeTabs.Length == format.TabStops.Length && nativeTabs.Zip(format.TabStops).All(pair =>
                    pair.First.Location == pair.Second.Position && pair.First.Alignment == (pair.Second.Alignment switch
                    {
                        RichTextTabAlignment.Center => UITextAlignment.Center,
                        RichTextTabAlignment.Right => UITextAlignment.Right,
                        _ => UITextAlignment.Left,
                    }));

            return format with
            {
                Alignment = style.Alignment switch
                {
                    UITextAlignment.Center => RichTextAlignment.Center,
                    UITextAlignment.Right => RichTextAlignment.Right,
                    UITextAlignment.Justified => format.Alignment == RichTextAlignment.Distributed
                        ? RichTextAlignment.Distributed : RichTextAlignment.Justified,
                    _ => RichTextAlignment.Left,
                },
                Direction = style.BaseWritingDirection switch
                {
                    NSWritingDirection.LeftToRight => RichTextDirection.LeftToRight,
                    NSWritingDirection.RightToLeft => RichTextDirection.RightToLeft,
                    _ => RichTextDirection.Automatic,
                },
                LeadingIndent = style.HeadIndent,
                FirstLineIndent = style.FirstLineHeadIndent - style.HeadIndent,
                TrailingIndent = style.TailIndent < 0 ? -style.TailIndent : 0,
                SpaceBefore = Math.Max(style.ParagraphSpacingBefore, 0),
                SpaceAfter = Math.Max(style.ParagraphSpacing, 0),
                LineSpacingRule = preservesLineSpacing ? format.LineSpacingRule : lineSpacingRule,
                LineSpacing = preservesLineSpacing ? format.LineSpacing : Math.Max(lineSpacing, 0),
                MinimumLineHeight = preservesLineSpacing ? format.MinimumLineHeight : style.MinimumLineHeight > 0 ? style.MinimumLineHeight : null,
                MaximumLineHeight = preservesLineSpacing ? format.MaximumLineHeight : style.MaximumLineHeight > 0 ? style.MaximumLineHeight : null,
                TabStops = preservesTabs ? format.TabStops : nativeTabs
                    .Select(tab => new RichTextTabStop(
                        tab.Location,
                        tab.Alignment switch
                        {
                            UITextAlignment.Center => RichTextTabAlignment.Center,
                            UITextAlignment.Right => RichTextTabAlignment.Right,
                            _ => RichTextTabAlignment.Left,
                        }))
                    .ToImmutableArray(),
                Hyphenation = style.HyphenationFactor > 0,
                List = list is null
                    ? null
                    : format.List ?? (list.Id > 0
                        ? RichTextListConversions.ToItem(list)
                        : null),
                NativeList = list,
            };
        }

        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        private static Dictionary<int, NSTextList[]> CreateNativeTextLists(
            RichTextDocumentSnapshot document,
            List<NSTextList> ownedTextLists,
            HashSet<int>? includedListIds = null)
        {
            var definitions = new Dictionary<(int Id, int Level), RichTextListFormat>();
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.Format.NativeList is { } list &&
                    (includedListIds is null || includedListIds.Contains(list.Id)))
                {
                    definitions.TryAdd((list.Id, list.Level), list);
                }
            }

            var result = new Dictionary<int, NSTextList[]>();
            var activeLists = new Dictionary<int, NSTextList?[]>();
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.Format.NativeList is not { } list ||
                    includedListIds is not null && !includedListIds.Contains(list.Id))
                {
                    continue;
                }

                var level = Math.Clamp(list.Level, 0, 8);
                if (!activeLists.TryGetValue(list.Id, out var levels))
                {
                    levels = new NSTextList?[9];
                    activeLists.Add(list.Id, levels);
                }

                if (list.Restart)
                {
                    levels[level] = null;
                    Array.Clear(levels, level + 1, levels.Length - level - 1);
                }

                for (var outerLevel = 0; outerLevel <= level; outerLevel++)
                {
                    if (levels[outerLevel] is not null)
                    {
                        continue;
                    }

                    var definition = definitions.GetValueOrDefault(
                        (list.Id, outerLevel),
                        list with
                        {
                            Level = outerLevel,
                            Restart = false,
                            StartAt = 1,
                        });
                    if (outerLevel == level && list.Restart)
                    {
                        definition = list;
                    }

                    var textList = CreateNativeTextList(definition);
                    levels[outerLevel] = textList;
                    ownedTextLists.Add(textList);
                }

                var paragraphLists = new NSTextList[level + 1];
                for (var outerLevel = 0; outerLevel <= level; outerLevel++)
                {
                    paragraphLists[outerLevel] = levels[outerLevel]!;
                }

                result.Add(paragraph.Start, paragraphLists);
            }

            return result;
        }

        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        private static void ApplyNativeTextList(
            NSMutableParagraphStyle style,
            RichTextListFormat list)
        {
            var textList = CreateNativeTextList(list);
            style.TextLists = Enumerable.Repeat(textList, list.Level + 1).ToArray();
            textList.Dispose();
        }

        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        private static NSTextList CreateNativeTextList(RichTextListFormat list)
        {
            var markerFormat = list.Kind == RichListKind.Bulleted
                ? (string.IsNullOrEmpty(list.BulletText) ? "{disc}" : list.BulletText)
                : string.Concat(
                    list.Prefix,
                    list.NumberStyle switch
                    {
                        RichListNumberStyle.UpperRoman => "{upper-roman}",
                        RichListNumberStyle.LowerRoman => "{lower-roman}",
                        RichListNumberStyle.UpperLetter => "{upper-alpha}",
                        RichListNumberStyle.LowerLetter => "{lower-alpha}",
                        _ => "{decimal}",
                    },
                    list.Suffix);
            return new NSTextList(
                markerFormat,
                NSTextListOptions.None,
                list.StartAt);
        }

        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        private static RichTextListFormat? ReadNativeTextList(NSParagraphStyle style)
        {
            var textLists = style.TextLists;
            if (textLists is null || textLists.Length == 0)
            {
                return null;
            }

            var textList = textLists[^1];
            var level = textLists.Length - 1;
            var marker = textList.MarkerFormat;
            var kind = marker is NSTextListMarkerFormats.Disc or
                NSTextListMarkerFormats.Circle or
                NSTextListMarkerFormats.Square or
                NSTextListMarkerFormats.Diamond or
                NSTextListMarkerFormats.Box or
                NSTextListMarkerFormats.Check or
                NSTextListMarkerFormats.Hyphen
                ? RichListKind.Bulleted
                : RichListKind.Numbered;
            var numberStyle = marker == NSTextListMarkerFormats.UppercaseRoman
                ? RichListNumberStyle.UpperRoman
                : marker == NSTextListMarkerFormats.LowercaseRoman
                    ? RichListNumberStyle.LowerRoman
                    : marker is NSTextListMarkerFormats.UppercaseAlpha or
                        NSTextListMarkerFormats.UppercaseLatin
                        ? RichListNumberStyle.UpperLetter
                        : marker is NSTextListMarkerFormats.LowercaseAlpha or
                            NSTextListMarkerFormats.LowercaseLatin
                            ? RichListNumberStyle.LowerLetter
                            : RichListNumberStyle.Arabic;
            return new RichTextListFormat
            {
                Id = 0,
                Level = Math.Clamp(level, 0, 8),
                Kind = kind,
                NumberStyle = numberStyle,
                StartAt = Math.Max((int)textList.StartingItemNumber, 1),
                Suffix = kind == RichListKind.Numbered ? "." : string.Empty,
            };
        }

        private static RichTextImage ReadImage(
            NSDictionary dictionary,
            NSTextAttachment attachment,
            int position)
        {
            if (dictionary[ImageMetadataKey] is ImageMetadata metadata)
            {
                return metadata.Image with { Position = position };
            }

            var data = attachment.Contents?.ToArray() ??
                attachment.Image?.AsPNG()?.ToArray() ?? [];
            return new RichTextImage
            {
                Position = position,
                MediaType = string.IsNullOrWhiteSpace(attachment.FileType)
                    ? "application/octet-stream"
                    : attachment.FileType,
                Data = ImmutableArray.CreateRange(data),
                Width = attachment.Bounds.Width,
                Height = attachment.Bounds.Height,
                VerticalAlignment = ReadImageVerticalAlignment(dictionary, attachment.Bounds),
                AlternativeText = attachment.Image?.AccessibilityLabel,
            };
        }

        private static nfloat GetImageVerticalOffset(
            UIFont? font,
            double height,
            RichTextImageVerticalAlignment alignment)
        {
            if (font is null || alignment == RichTextImageVerticalAlignment.Baseline)
            {
                return 0;
            }

            return alignment switch
            {
                RichTextImageVerticalAlignment.Bottom => font.Descender,
                RichTextImageVerticalAlignment.Center =>
                    (nfloat)(((double)font.CapHeight - height) / 2d),
                RichTextImageVerticalAlignment.Top => (nfloat)((double)font.Ascender - height),
                _ => 0,
            };
        }

        private static RichTextImageVerticalAlignment ReadImageVerticalAlignment(
            NSDictionary dictionary,
            CGRect bounds)
        {
            var font = new UIStringAttributes(dictionary).Font;
            if (font is null)
            {
                return RichTextImageVerticalAlignment.Baseline;
            }

            var actual = (double)bounds.Y;
            var result = RichTextImageVerticalAlignment.Baseline;
            var distance = Math.Abs(actual);
            var bottomDistance = Math.Abs(
                actual - GetImageVerticalOffset(
                    font,
                    bounds.Height,
                    RichTextImageVerticalAlignment.Bottom));
            if (bottomDistance < distance)
            {
                distance = bottomDistance;
                result = RichTextImageVerticalAlignment.Bottom;
            }

            var centerDistance = Math.Abs(
                actual - GetImageVerticalOffset(
                    font,
                    bounds.Height,
                    RichTextImageVerticalAlignment.Center));
            if (centerDistance < distance)
            {
                distance = centerDistance;
                result = RichTextImageVerticalAlignment.Center;
            }

            var topDistance = Math.Abs(
                actual - GetImageVerticalOffset(
                    font,
                    bounds.Height,
                    RichTextImageVerticalAlignment.Top));
            if (topDistance < distance)
            {
                result = RichTextImageVerticalAlignment.Top;
            }

            return result;
        }

        private static string? GetLinkTarget(NSObject? value) => value switch
        {
            null => null,
            NSUrl url => url.AbsoluteString,
            NSString text when text.Length > 0 => text.ToString(),
            _ => value.ToString(),
        };

        private UIFont ResolveFont(RichTextCharacterFormat format)
        {
            var family = format.FontFamily ?? VirtualView.FontFamily;
            var size = (nfloat)(format.FontSize ?? VirtualView.FontSize ?? _defaultFont.PointSize);
            var font = (string.IsNullOrWhiteSpace(family)
                ? UIFont.FromDescriptor(_defaultFont.FontDescriptor, size) ?? _defaultFont
                : UIFont.FromName(family, size) ?? UIFont.SystemFontOfSize(size))!;

            var traits = (UIFontDescriptorSymbolicTraits)0;
            if (format.Bold)
            {
                traits |= UIFontDescriptorSymbolicTraits.Bold;
            }

            if (format.Italic)
            {
                traits |= UIFontDescriptorSymbolicTraits.Italic;
            }

            if (traits != 0 && font.FontDescriptor.CreateWithTraits(traits) is { } descriptor)
            {
                font = UIFont.FromDescriptor(descriptor, size) ?? font;
            }

            if (format.SmallCaps || format.AllCaps)
            {
                var attributes = font.FontDescriptor.FontAttributes;
#pragma warning disable CA1422 // The iOS 15-compatible feature-selector API is deprecated but still supported.
                attributes.FeatureSettings =
                [
                    new UIFontFeature(format.AllCaps
                        ? CTFontFeatureLetterCase.Selector.AllCaps
                        : CTFontFeatureLetterCase.Selector.SmallCaps),
                ];
#pragma warning restore CA1422
                var featureDescriptor = font.FontDescriptor.CreateWithAttributes(attributes);
                font = UIFont.FromDescriptor(featureDescriptor, size) ?? font;
            }

            return font;
        }

        private static NSUnderlineStyle ToNativeUnderline(RichTextUnderlineStyle value) => value switch
        {
            RichTextUnderlineStyle.None => NSUnderlineStyle.None,
            RichTextUnderlineStyle.Words => NSUnderlineStyle.Single | NSUnderlineStyle.ByWord,
            RichTextUnderlineStyle.Double => NSUnderlineStyle.Double,
            RichTextUnderlineStyle.Dotted => NSUnderlineStyle.Single | NSUnderlineStyle.PatternDot,
            RichTextUnderlineStyle.Dash => NSUnderlineStyle.Single | NSUnderlineStyle.PatternDash,
            RichTextUnderlineStyle.DashDot => NSUnderlineStyle.Single | NSUnderlineStyle.PatternDashDot,
            RichTextUnderlineStyle.DashDotDot =>
                NSUnderlineStyle.Single | NSUnderlineStyle.PatternDashDotDot,
            RichTextUnderlineStyle.Thick => NSUnderlineStyle.Thick,
            _ => NSUnderlineStyle.Single,
        };

        private static RichTextUnderlineStyle FromNativeUnderline(NSUnderlineStyle value)
        {
            if (value == NSUnderlineStyle.None)
            {
                return RichTextUnderlineStyle.None;
            }

            if (value.HasFlag(NSUnderlineStyle.ByWord))
            {
                return RichTextUnderlineStyle.Words;
            }

            if (value.HasFlag(NSUnderlineStyle.Double))
            {
                return RichTextUnderlineStyle.Double;
            }

            // PatternDashDot contains the PatternDot bit; these are a masked
            // enum field, not independent flags.
            return ((long)value & 0x0F00) switch
            {
                (long)NSUnderlineStyle.PatternDot => RichTextUnderlineStyle.Dotted,
                (long)NSUnderlineStyle.PatternDash => RichTextUnderlineStyle.Dash,
                (long)NSUnderlineStyle.PatternDashDot => RichTextUnderlineStyle.DashDot,
                (long)NSUnderlineStyle.PatternDashDotDot => RichTextUnderlineStyle.DashDotDot,
                _ => value.HasFlag(NSUnderlineStyle.Thick) ? RichTextUnderlineStyle.Thick : RichTextUnderlineStyle.Single,
            };
        }

        private static double GetNativeBaselineOffset(
            RichTextCharacterFormat format,
            double defaultFontSize) =>
            format.BaselineOffset + format.Script switch
            {
                RichTextScript.Superscript => (format.FontSize ?? defaultFontSize) * 0.35d,
                RichTextScript.Subscript => (format.FontSize ?? defaultFontSize) * -0.15d,
                _ => 0,
            };

        private static Color FromUIColor(UIColor color)
        {
            color.GetRGBA(out var red, out var green, out var blue, out var alpha);
            return Color.FromRgba((float)red, (float)green, (float)blue, (float)alpha);
        }

        private static int GetParagraphStart(string text, int position) =>
            position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

        private static int GetParagraphEnd(string text, int start)
        {
            var newline = text.IndexOf('\n', start);
            return newline < 0 ? text.Length : newline + 1;
        }

        private bool ShouldAllowNativeChange(NSRange range, string replacementText)
        {
            if (VirtualView is null || VirtualView.IsReadOnly)
            {
                return false;
            }

            if (!VirtualView.AcceptsTab && string.Equals(replacementText, "\t", StringComparison.Ordinal))
            {
                return false;
            }

            if (VirtualView.MaxLength < 0)
            {
                return true;
            }

            var currentLength = checked((int)PlatformView.TextStorage.Length);
            var removedLength = range.Length > int.MaxValue
                ? currentLength
                : Math.Min((int)range.Length, currentLength);
            return replacementText.Length == 0 ||
                currentLength - removedLength + replacementText.Length <= VirtualView.MaxLength;
        }

        private void RecordPendingNativeChange(NSRange range)
        {
            if (range.Location < 0 || range.Location > int.MaxValue ||
                range.Length > int.MaxValue)
            {
                _pendingNativeChange = null;
                return;
            }

            _pendingNativeChange = new PendingNativeChange(
                (int)range.Location,
                (int)range.Length,
                VirtualView?.Document.Version ?? -1);
        }

        private RichTextDocumentSnapshot? TryReadIncrementalNativeChange(UITextView textView)
        {
            if (_pendingNativeChange is not { } pending || VirtualView is null)
            {
                return null;
            }

            var previous = VirtualView.Document.CurrentSnapshot;
            if (pending.Version != previous.Version ||
                pending.Start < 0 || pending.RemovedLength < 0 ||
                pending.Start > previous.Length - pending.RemovedLength)
            {
                return null;
            }

            var attributed = textView.TextStorage;
            if (attributed is null || attributed.Length > int.MaxValue)
            {
                return null;
            }

            var nativeLength = (int)attributed.Length;
            var insertedLength = nativeLength - (previous.Length - pending.RemovedLength);
            if (insertedLength < 0 || pending.Start > nativeLength - insertedLength ||
                !NativeAnchorsMatch(attributed, previous, pending, insertedLength))
            {
                return null;
            }

            using var inserted = attributed.Substring(pending.Start, insertedLength);
            var insertedText = inserted.Value ?? string.Empty;
            if (previous.Text.AsSpan(pending.Start, pending.RemovedLength).Contains('\n') ||
                insertedText.Contains('\n'))
            {
                // NSTextStorage owns paragraph/list semantics. Structural edits
                // are read back in full rather than predicted or corrected.
                return null;
            }

            var document = previous.Replace(
                pending.Start..(pending.Start + pending.RemovedLength),
                insertedText,
                insertedLength == 0 ? null : _nativeTypingFormat);
            if (RequiresFullNativeSnapshot(
                    attributed,
                    document,
                    new RichTextRange(pending.Start, insertedLength)))
            {
                return null;
            }

            return document;
        }

        private static bool NativeAnchorsMatch(
            NSAttributedString attributed,
            RichTextDocumentSnapshot previous,
            PendingNativeChange pending,
            int insertedLength)
        {
            const int AnchorLength = 16;
            var prefixLength = Math.Min(pending.Start, AnchorLength);
            if (prefixLength > 0)
            {
                using var prefix = attributed.Substring(
                    pending.Start - prefixLength,
                    prefixLength);
                if (!previous.Text.AsSpan(pending.Start - prefixLength, prefixLength)
                    .SequenceEqual((prefix.Value ?? string.Empty).AsSpan()))
                {
                    return false;
                }
            }

            var oldSuffixStart = pending.Start + pending.RemovedLength;
            var newSuffixStart = pending.Start + insertedLength;
            var suffixLength = Math.Min(previous.Length - oldSuffixStart, AnchorLength);
            if (suffixLength == 0)
            {
                return true;
            }

            using var suffix = attributed.Substring(newSuffixStart, suffixLength);
            return previous.Text.AsSpan(oldSuffixStart, suffixLength)
                .SequenceEqual((suffix.Value ?? string.Empty).AsSpan());
        }

        private bool RequiresFullNativeSnapshot(
            NSAttributedString? attributed,
            RichTextDocumentSnapshot expected,
            RichTextRange insertedRange)
        {
            if (attributed is null || attributed.Length != expected.Length)
            {
                return true;
            }

            for (var position = insertedRange.Start; position < insertedRange.End;)
            {
                var dictionary = attributed.GetAttributes(position, out var effectiveRange) ??
                    new NSDictionary();
                var attributes = new UIStringAttributes(dictionary);
                if (dictionary[UIStringAttributeKey.Link] is not null || attributes.TextAttachment is not null ||
                    ReadCharacterFormat(
                        dictionary,
                        expected.GetCharacterFormat(position)) !=
                    expected.GetCharacterFormat(position))
                {
                    return true;
                }

                var nativeEnd = checked((int)(effectiveRange.Location + effectiveRange.Length));
                position = Math.Max(position + 1, Math.Min(nativeEnd, insertedRange.End));
            }

            var paragraphStart = GetParagraphStart(expected.Text, insertedRange.Start);
            var paragraphLimit = GetParagraphEnd(expected.Text, insertedRange.End);
            for (var start = paragraphStart; start < paragraphLimit;)
            {
                if (expected.Length == 0)
                {
                    break;
                }

                var index = Math.Min(start, expected.Length - 1);
                var dictionary = attributed.GetAttributes(index, out _) ?? new NSDictionary();
                var expectedFormat = expected.GetParagraphFormat(start);
                var nativeFormat = ReadParagraphFormat(dictionary, expectedFormat);
                if (nativeFormat != expectedFormat)
                {
                    return true;
                }

                start = GetParagraphEnd(expected.Text, start);
            }

            return false;
        }

        private bool OnNativeLinkInvoked(NSUrl url, NSRange characterRange)
        {
            if (VirtualView is null || characterRange.Location < 0 ||
                characterRange.Location > int.MaxValue)
            {
                return true;
            }

            var start = (int)characterRange.Location;
            var length = characterRange.Length > int.MaxValue
                ? 0
                : (int)characterRange.Length;
            var end = start > int.MaxValue - length ? int.MaxValue : start + length;
            var target = url.AbsoluteString;
            var link = VirtualView.Document.CurrentSnapshot.Links.FirstOrDefault(link =>
                link.End > start && link.Start < end &&
                (string.IsNullOrEmpty(target) ||
                 string.Equals(link.Target, target, StringComparison.Ordinal)));
            return link is null || VirtualView.RaiseLinkInvoked(link);
        }

        private bool OnNativeInlineObjectInvoked(NSRange characterRange)
        {
            if (VirtualView is null || characterRange.Location < 0 ||
                characterRange.Location > int.MaxValue)
            {
                return true;
            }

            var position = (int)characterRange.Location;
            var image = VirtualView.Document.CurrentSnapshot.Images.FirstOrDefault(
                image => image.Position == position);
            if (image is not null)
            {
                return VirtualView.RaiseInlineObjectInvoked(image);
            }

            return true;
        }

        private void QueueProjectionReadback()
        {
            if (VirtualView is null || string.Equals(PlatformView.TextStorage.Value, VirtualView.Document.Text, StringComparison.Ordinal)) return;
            // Native text services can transform an authored update as it is
            // presented. Reconcile the actual result after the document's commit,
            // and merge it only with the exact version that caused the transform.
            _projectedDocument = VirtualView.Document;
            _projectedVersion = _projectedDocument.Version;
            QueueNativeReadback();
        }

        private void ObserveTextStorage()
        {
            var storage = PlatformView.TextStorage;
            if (ReferenceEquals(storage, _observedTextStorage)) return;
            if (_observedTextStorage is { } previous) previous.DidProcessEditing -= OnTextStorageProcessed;
            _observedTextStorage = storage;
            storage.DidProcessEditing += OnTextStorageProcessed;
        }

        private void OnTextStorageProcessed(object? sender, NSTextStorageEventArgs args)
        {
            QueueNativeReadback();
        }

        private void QueueNativeReadback()
        {
            if (_applyingDocument && !_applyingTypingFormat || VirtualView is null)
            {
                return;
            }

            if (!_nativeReadbackQueued || !ReferenceEquals(_queuedDocument, VirtualView.Document) || _queuedProjectionGeneration != _projectionGeneration)
            {
                _queuedDocument = VirtualView.Document;
                _queuedVersion = _queuedDocument.Version;
                _queuedProjectionGeneration = _projectionGeneration;
                _queuedNativeContinuation = false;
            }
            _queuedNativeContinuation |= _applyingTypingFormat;
            if (_nativeReadbackQueued) return;
            _nativeReadbackQueued = true;
            PlatformView.BeginInvokeOnMainThread(() =>
            {
                _nativeReadbackQueued = false;
                // UITextView's Changed callback normally commits synchronously.
                // Text services can edit storage without issuing that callback;
                // wait until their selection and smart-spacing edits are finished.
                if (!_applyingDocument && ReferenceEquals(VirtualView?.Document, _queuedDocument) &&
                    _queuedProjectionGeneration == _projectionGeneration && _observedTextStorage is not null)
                {
                    OnNativeDocumentChanged(PlatformView, mergeWithPrevious: _queuedNativeContinuation || _queuedDocument!.Version != _queuedVersion);
                }
            });
        }

        private void OnNativeDocumentChanged(UITextView textView, bool mergeWithPrevious = false)
        {
            if (_applyingDocument || VirtualView is null)
            {
                if (_applyingTypingFormat && VirtualView is not null) QueueNativeReadback();
                return;
            }
            if (_applyingSelection)
            {
                // UIKit can report an edit while SelectedRange is still being
                // assigned. Publishing that intermediate caret reenters MAUI's
                // selection setter and overwrites the requested selection.
                QueueNativeReadback();
                return;
            }
            ObserveTextStorage();

            var before = VirtualView.Document.CurrentSnapshot;
            var previousSelection = VirtualView.SelectedRange;

            PlatformView.UpdatePlaceholderVisibility();
            RichTextDocumentSnapshot document;
            try
            {
                document = TryReadIncrementalNativeChange(textView) ??
                    ReadDocumentFromPlatform();
            }
            finally
            {
                _pendingNativeChange = null;
            }

            var start = Math.Clamp((int)textView.SelectedRange.Location, 0, document.Text.Length);
            var length = Math.Clamp(
                (int)textView.SelectedRange.Length,
                0,
                document.Text.Length - start);
            if (VirtualView.MaxLength >= 0 && document.Length > VirtualView.MaxLength && document.Length > before.Length)
            {
                ApplyDocumentCore(before, previousSelection.Start, previousSelection.Length);
                ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
                return;
            }

            VirtualView.UpdateDocumentFromPlatform(
                document,
                start,
                length,
                _sourceToken,
                mergeWithPrevious: mergeWithPrevious,
                projectedVersion: ReferenceEquals(_projectedDocument, VirtualView.Document) ? _projectedVersion : null);
            _projectedDocument = null;
            _projectedVersion = null;

            VirtualView.UpdateUndoStateFromPlatform();
            UpdateTypingFormatsFromPlatform();
        }

        private void OnNativeSelectionChanged(UITextView textView)
        {
            if (_applyingDocument || _applyingSelection || VirtualView is null)
            {
                return;
            }

            // UIKit may move the caret before delivering Changed. Keep the
            // pre-edit range available so that callback can remap semantic ranges.
            if (!string.Equals(textView.Text, VirtualView.Document.Text, StringComparison.Ordinal))
            {
                return;
            }

            var start = Math.Clamp(
                (int)textView.SelectedRange.Location,
                0,
                VirtualView.Document.Text.Length);
            var length = Math.Clamp(
                (int)textView.SelectedRange.Length,
                0,
                VirtualView.Document.Text.Length - start);
            VirtualView.UpdateSelectionFromPlatform(start, length);
            UpdateTypingFormatsFromPlatform();
            _pendingNativeChange = null;
        }

        private Task OnPlatformPasteAsync() =>
            VirtualView is null || VirtualView.IsReadOnly
                ? Task.CompletedTask
                : VirtualView.PasteAsync();

        private void OnNativeAppearanceChanged()
        {
            if (VirtualView is null)
            {
                return;
            }

            UpdateAppearance(VirtualView);
            VirtualView.NotifyNativeAppearanceChanged();
        }

        private void UpdateTypingFormatsFromPlatform()
        {
            if (VirtualView is null)
            {
                return;
            }

            var attributes = PlatformView.TypingAttributes2 ?? new NSDictionary();
            _nativeTypingFormat = ReadCharacterFormat(
                attributes,
                VirtualView.TypingCharacterFormat);
            var snapshot = VirtualView.Document.CurrentSnapshot;
            var selectionStart = Math.Clamp(
                (int)PlatformView.SelectedRange.Location,
                0,
                snapshot.Length);
            var selectionLength = Math.Clamp(
                (int)PlatformView.SelectedRange.Length,
                0,
                snapshot.Length - selectionStart);
            if (IsCaretInTrailingEmptyParagraph(
                    snapshot,
                    selectionStart,
                    selectionLength))
            {
                ApplyTrailingEmptyParagraphTypingFormat(
                    snapshot,
                    selectionStart,
                    selectionLength);
            }
            else
            {
                _nativeTypingParagraphFormat = ReadParagraphFormat(
                    attributes,
                    VirtualView.TypingParagraphFormat);
                if (_nativeTypingParagraphFormat.NativeList is { Id: <= 0 } nativeList &&
                    VirtualView.TypingParagraphFormat.NativeList is { } previousList)
                {
                    nativeList = nativeList with { Id = previousList.Id };
                    _nativeTypingParagraphFormat = _nativeTypingParagraphFormat with
                    {
                        NativeList = nativeList,
                        List = RichTextListConversions.ToItem(nativeList),
                    };
                }
            }

            VirtualView.UpdateTypingFormatsFromPlatform(
                _nativeTypingFormat,
                _nativeTypingParagraphFormat);
        }

        private void ApplyTrailingEmptyParagraphTypingFormat(
            RichTextDocumentSnapshot snapshot,
            int selectionStart,
            int selectionLength)
        {
            if (!IsCaretInTrailingEmptyParagraph(
                    snapshot,
                    selectionStart,
                    selectionLength))
            {
                return;
            }

            var paragraphFormat = snapshot.GetParagraphFormat(selectionStart);
            using var paragraphAttributes = CreateParagraphAttributes(paragraphFormat);
            var typingAttributes = PlatformView.TypingAttributes2 is { } current
                ? new NSMutableDictionary(current)
                : new NSMutableDictionary();
            typingAttributes.AddEntries(paragraphAttributes);
            PlatformView.TypingAttributes2 = typingAttributes;
            _nativeTypingParagraphFormat = paragraphFormat;
        }

        private static bool IsCaretInTrailingEmptyParagraph(
            RichTextDocumentSnapshot snapshot,
            int selectionStart,
            int selectionLength) =>
            selectionLength == 0 &&
            selectionStart == snapshot.Length &&
            (snapshot.Length == 0 || snapshot.Text[^1] == '\n');

        private void OnNativeEditingEnded() => VirtualView?.RaiseCompleted();

        private sealed class RichTextViewDelegate(RichEditorHandler handler) : UITextViewDelegate
        {
            private readonly WeakReference<RichEditorHandler> _handler = new(handler);

            public override bool ShouldChangeText(
                UITextView textView,
                NSRange range,
                string text)
            {
                if (!_handler.TryGetTarget(out var target))
                {
                    return true;
                }
                if (target._applyingDocument || target._applyingSelection)
                {
                    // Projection/selection updates are not a proposed user edit.
                    // UIKit may call this delegate while synchronizing its input
                    // context; that range must not become the next edit's hint.
                    return true;
                }

                if (!target.ShouldAllowNativeChange(range, text))
                {
                    return false;
                }

                target.RecordPendingNativeChange(range);
                return true;
            }

            public override void Changed(UITextView textView)
            {
                if (_handler.TryGetTarget(out var target))
                {
                    target.OnNativeDocumentChanged(textView);
                }
            }

            public override void SelectionChanged(UITextView textView)
            {
                if (_handler.TryGetTarget(out var target))
                {
                    target.OnNativeSelectionChanged(textView);
                }
            }

            public override void EditingEnded(UITextView textView)
            {
                if (_handler.TryGetTarget(out var target))
                {
                    target.OnNativeEditingEnded();
                }
            }

            public override bool ShouldInteractWithUrl(
                UITextView textView,
                NSUrl url,
                NSRange characterRange,
                UITextItemInteraction interaction) =>
                !_handler.TryGetTarget(out var target) ||
                target.OnNativeLinkInvoked(url, characterRange);

            public override bool ShouldInteractWithTextAttachment(
                UITextView textView,
                NSTextAttachment textAttachment,
                NSRange characterRange,
                UITextItemInteraction interaction)
            {
                if (_handler.TryGetTarget(out var target))
                {
                    return target.OnNativeInlineObjectInvoked(characterRange);
                }

                return true;
            }
        }

        private readonly record struct PendingNativeChange(
            int Start,
            int RemovedLength,
            long Version);

        private sealed class CharacterMetadata(RichTextCharacterFormat format) : NSObject
        {
            public RichTextCharacterFormat Format { get; } = format;
        }

        private sealed class ParagraphMetadata(RichTextParagraphFormat format) : NSObject
        {
            public RichTextParagraphFormat Format { get; } = format;
        }

        private sealed class ImageMetadata(RichTextImage image) : NSObject
        {
            public RichTextImage Image { get; } = image;
        }
    }
}
#endif
