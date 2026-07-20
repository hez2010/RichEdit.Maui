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

        internal Func<Task>? PasteRequested { get; set; }

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
                await pasteRequested();
                return;
            }

            base.Paste(sender);
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
                NativeAppearanceChanged = null;
                _placeholderLabel.Dispose();
            }

            base.Dispose(disposing);
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
        private UIFont _defaultFont = UIFont.SystemFontOfSize(UIFont.SystemFontSize)!;
        private UIColor _defaultTextColor = UIColor.Label;
        private UIColor _defaultTintColor = UIColor.SystemBlue;
        private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
        private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
        private RichTextViewDelegate? _textViewDelegate;
        private PendingNativeChange? _pendingNativeChange;
        private NSUndoManager? _observedUndoManager;
        private NSObject? _undoGroupClosedObserver;
        private NSObject? _didUndoObserver;
        private NSObject? _didRedoObserver;
        private bool _restoringNativeUndo;

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
            platformView.NativeAppearanceChanged = OnNativeAppearanceChanged;
            EnsureUndoManagerNotifications(platformView.UndoManager);
        }

        /// <inheritdoc />
        protected override void DisconnectHandler(RichTextView platformView)
        {
            _pendingNativeChange = null;
            StopObservingUndoManager();
            platformView.UndoManager?.RemoveAllActions(platformView);
            platformView.PasteRequested = null;
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
            _applyingDocument = true;
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

                PlatformView.AttributedText = attributed;
                SetSelectionCore(selectionStart, selectionLength);
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

                _applyingDocument = false;
            }
        }

        private partial void ApplyIncrementalChangesCore(
            RichTextChangeSet changes,
            RichTextRange selection,
            RichTextCharacterFormat typingCharacterFormat,
            RichTextParagraphFormat typingParagraphFormat)
        {
            if (PlatformView is null)
            {
                return;
            }
            var undoManager = PlatformView.UndoManager;
            EnsureUndoManagerNotifications(undoManager);
            var previousSelection = GetNativeSelection(
                changes.BeforeSnapshot?.Length ?? checked((int)PlatformView.TextStorage.Length));
            // Programmatic NSTextStorage edits do not register undo actions. Leave
            // UIKit's shared manager enabled while TextKit processes the edit, then
            // register the one model transaction after the native projection succeeds.
            ApplyIncrementalChangesToTextStorage(
                changes,
                selection,
                typingCharacterFormat,
                typingParagraphFormat);
            CompleteNativeUndoTransaction(
                undoManager,
                changes,
                previousSelection,
                selection);
        }

        private void ApplyIncrementalChangesToTextStorage(
            RichTextChangeSet changes,
            RichTextRange selection,
            RichTextCharacterFormat typingCharacterFormat,
            RichTextParagraphFormat typingParagraphFormat)
        {
            var snapshot = VirtualView.Document.CurrentSnapshot;
            if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
            {
                ApplyDocumentCore(snapshot, selection.Start, selection.Length);
                ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
                return;
            }

            _applyingDocument = true;
            List<NSTextList>? ownedTextLists = null;
            PlatformView.TextStorage.BeginEditing();
            try
            {
                foreach (var textChange in changes.Changes.OfType<RichTextTextChange>())
                {
                    PlatformView.TextStorage.Replace(
                        new NSRange(textChange.OldRange.Start, textChange.OldRange.Length),
                        textChange.InsertedText);
                }

                var affected = GetAffectedRange(changes, snapshot.Length);
                var refreshCharacters = changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or
                    RichTextChangeKind.CharacterFormat or
                    RichTextChangeKind.DefaultFormat);
                if (refreshCharacters)
                {
                    affected = ExpandToCharacterRuns(snapshot, affected);
                    ApplyCharacterFormatsIncrementally(snapshot, affected);
                }

                var refreshParagraphs = refreshCharacters ||
                    changes.Changes.Any(static change => change.Kind is
                        RichTextChangeKind.ParagraphFormat or
                        RichTextChangeKind.List or
                        RichTextChangeKind.DefaultFormat);
                if (refreshParagraphs)
                {
                    var paragraphRange = GetAffectedParagraphRange(affected, snapshot.Text);
                    Dictionary<int, NSTextList[]>? textListsByParagraph = null;
                    if (OperatingSystem.IsIOSVersionAtLeast(16) ||
                        OperatingSystem.IsMacCatalystVersionAtLeast(16))
                    {
                        var listIds = GetListIds(snapshot, paragraphRange);
                        if (listIds.Count != 0)
                        {
                            paragraphRange = ExpandToLists(snapshot, paragraphRange, listIds);
                            ownedTextLists = [];
                            textListsByParagraph = CreateNativeTextLists(
                                snapshot,
                                ownedTextLists,
                                listIds);
                        }
                    }

                    ApplyParagraphFormatsIncrementally(
                        snapshot,
                        paragraphRange,
                        textListsByParagraph);
                }

                if (refreshCharacters || changes.Changes.Any(static change =>
                        change.Kind == RichTextChangeKind.Link))
                {
                    ApplyLinksIncrementally(snapshot, affected);
                }

                if (refreshCharacters || changes.Changes.Any(static change =>
                        change.Kind == RichTextChangeKind.Image))
                {
                    ApplyImagesIncrementally(snapshot, affected);
                }

                SetSelectionCore(selection.Start, selection.Length);
                ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
                PlatformView.UpdatePlaceholderVisibility();
            }
            finally
            {
                PlatformView.TextStorage.EndEditing();
                if (ownedTextLists is not null)
                {
                    foreach (var textList in ownedTextLists)
                    {
                        textList.Dispose();
                    }
                }

                _applyingDocument = false;
            }
        }

        private RichTextRange GetNativeSelection(int documentLength)
        {
            var start = Math.Clamp(
                (int)PlatformView.SelectedRange.Location,
                0,
                documentLength);
            var length = Math.Clamp(
                (int)PlatformView.SelectedRange.Length,
                0,
                documentLength - start);
            return new RichTextRange(start, length);
        }

        private void CompleteNativeUndoTransaction(
            NSUndoManager? manager,
            RichTextChangeSet changes,
            RichTextRange previousSelection,
            RichTextRange resultingSelection)
        {
            if (manager is null || _restoringNativeUndo)
            {
                return;
            }

            if (changes.UndoBehavior is RichTextUndoBehavior.DoNotRecord or
                RichTextUndoBehavior.ClearHistory)
            {
                manager.RemoveAllActions();
                VirtualView.UpdateUndoStateFromPlatform();
                return;
            }

            if (!manager.IsUndoRegistrationEnabled)
            {
                manager.RemoveAllActions();
                VirtualView.UpdateUndoStateFromPlatform();
                return;
            }

            if (changes.BeforeSnapshot is not { } before ||
                changes.AfterSnapshot is not { } after)
            {
                return;
            }

            RegisterNativeUndoAction(
                manager,
                new NativeUndoTransition(
                    before,
                    previousSelection,
                    after,
                    resultingSelection,
                    changes.UndoDescription));
            VirtualView.UpdateUndoStateFromPlatform();
        }

        private void RegisterNativeUndoAction(
            NSUndoManager manager,
            NativeUndoTransition transition)
        {
            if (!manager.IsUndoRegistrationEnabled)
            {
                return;
            }

            // MAUI commands can run before the undo manager has opened its automatic
            // run-loop group. NSUndoManager requires an established group when an
            // operation is registered, so own only the otherwise-missing top level.
            var startedGroup = manager.GroupingLevel == 0;
            if (startedGroup)
            {
                manager.BeginUndoGrouping();
            }

            try
            {
                var weakHandler = new WeakReference<RichEditorHandler>(this);
                manager.RegisterUndo(PlatformView, _ =>
                {
                    if (weakHandler.TryGetTarget(out var handler))
                    {
                        handler.ApplyNativeUndoTransition(transition);
                    }
                });
                if (!string.IsNullOrWhiteSpace(transition.ActionName))
                {
                    manager.SetActionName(transition.ActionName);
                }
            }
            finally
            {
                if (startedGroup)
                {
                    manager.EndUndoGrouping();
                }
            }
        }

        private void ApplyNativeUndoTransition(NativeUndoTransition transition)
        {
            if (PlatformView?.UndoManager is not { } manager || VirtualView is null)
            {
                return;
            }

            var origin = manager.IsRedoing
                ? RichTextChangeOrigin.Redo
                : RichTextChangeOrigin.Undo;
            _restoringNativeUndo = true;
            try
            {
                VirtualView.RestoreDocumentFromNativeUndo(
                    transition.TargetSnapshot,
                    transition.TargetSelection,
                    origin);
            }
            finally
            {
                _restoringNativeUndo = false;
            }

            RegisterNativeUndoAction(manager, transition.Reverse());
        }

        private void EnsureUndoManagerNotifications(NSUndoManager? manager)
        {
            if (manager is null || _observedUndoManager?.Handle == manager.Handle)
            {
                return;
            }

            StopObservingUndoManager();
            _observedUndoManager = manager;
            _undoGroupClosedObserver = NSUndoManager.Notifications.ObserveDidCloseUndoGroup(
                manager,
                (_, _) => VirtualView?.UpdateUndoStateFromPlatform());
            _didUndoObserver = NSUndoManager.Notifications.ObserveDidUndoChange(
                manager,
                (_, _) => VirtualView?.UpdateUndoStateFromPlatform());
            _didRedoObserver = NSUndoManager.Notifications.ObserveDidRedoChange(
                manager,
                (_, _) => VirtualView?.UpdateUndoStateFromPlatform());
        }

        private void StopObservingUndoManager()
        {
            _undoGroupClosedObserver?.Dispose();
            _undoGroupClosedObserver = null;
            _didUndoObserver?.Dispose();
            _didUndoObserver = null;
            _didRedoObserver?.Dispose();
            _didRedoObserver = null;
            _observedUndoManager = null;
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

        private void ApplyParagraphFormatsIncrementally(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range,
            Dictionary<int, NSTextList[]>? textListsByParagraph)
        {
            var paragraphStart = range.Start == 0
                ? 0
                : snapshot.Text.LastIndexOf('\n', range.Start - 1) + 1;
            for (var index = snapshot.FindParagraphIndex(paragraphStart);
                 index < snapshot.Paragraphs.Length;
                 index++)
            {
                var paragraph = snapshot.Paragraphs[index];
                if (paragraph.Range.Start > range.End)
                {
                    break;
                }

                if (paragraph.Range.End < range.Start || paragraph.Range.Start > range.End)
                {
                    continue;
                }

                var end = GetParagraphEnd(snapshot.Text, paragraph.Start);
                if (end <= paragraph.Start)
                {
                    continue;
                }

                NSTextList[]? textLists = null;
                textListsByParagraph?.TryGetValue(paragraph.Start, out textLists);
                using var attributes = CreateParagraphAttributes(paragraph.Format, textLists);
                PlatformView.TextStorage.AddAttributes(
                    attributes,
                    new NSRange(paragraph.Start, end - paragraph.Start));
            }
        }

        private void ApplyLinksIncrementally(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range)
        {
            if (range.IsEmpty)
            {
                return;
            }

            PlatformView.TextStorage.RemoveAttribute(
                UIStringAttributeKey.Link,
                new NSRange(range.Start, range.Length));
            foreach (var link in snapshot.Links.Where(link =>
                         link.End > range.Start && link.Start < range.End))
            {
                PlatformView.TextStorage.AddAttribute(
                    UIStringAttributeKey.Link,
                    new NSString(link.Target),
                    new NSRange(link.Start, link.Length));
            }
        }

        private void ApplyImagesIncrementally(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range)
        {
            if (range.IsEmpty)
            {
                return;
            }

            var nativeRange = new NSRange(range.Start, range.Length);
            PlatformView.TextStorage.RemoveAttribute(
                UIStringAttributeKey.Attachment,
                nativeRange);
            PlatformView.TextStorage.RemoveAttribute(ImageMetadataKey, nativeRange);
            foreach (var image in snapshot.Images.Where(image =>
                         image.Position >= range.Start && image.Position < range.End))
            {
                ApplyImage(PlatformView.TextStorage, image);
            }
        }

        private static RichTextRange GetAffectedRange(
            RichTextChangeSet changes,
            int documentLength) =>
            changes.GetAffectedRange(documentLength);

        private static RichTextRange ExpandToCharacterRuns(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range)
        {
            if (range.IsEmpty || snapshot.Runs.IsDefaultOrEmpty)
            {
                return range;
            }

            var firstIndex = snapshot.FindRunIndex(range.Start);
            var lastPosition = Math.Max(range.End - 1, range.Start);
            var lastIndex = snapshot.FindRunIndex(lastPosition);
            return firstIndex >= snapshot.Runs.Length
                ? range
                : new RichTextRange(
                    snapshot.Runs[firstIndex].Start,
                    snapshot.Runs[lastIndex].End - snapshot.Runs[firstIndex].Start);
        }

        private static HashSet<int> GetListIds(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range)
        {
            var result = new HashSet<int>();
            var start = range.Start == 0
                ? 0
                : snapshot.Text.LastIndexOf('\n', range.Start - 1) + 1;
            for (var index = snapshot.FindParagraphIndex(start);
                 index < snapshot.Paragraphs.Length &&
                 snapshot.Paragraphs[index].Range.Start <= range.End;
                 index++)
            {
                if (snapshot.Paragraphs[index].Format.NativeList is { } list)
                {
                    result.Add(list.Id);
                }
            }

            return result;
        }

        private static RichTextRange ExpandToLists(
            RichTextDocumentSnapshot snapshot,
            RichTextRange range,
            HashSet<int> listIds)
        {
            var start = range.Start;
            var end = range.End;
            foreach (var paragraph in snapshot.Paragraphs)
            {
                if (paragraph.Format.NativeList is { } list && listIds.Contains(list.Id))
                {
                    start = Math.Min(start, paragraph.Range.Start);
                    end = Math.Max(end, paragraph.Range.End);
                }
            }

            return new RichTextRange(start, end - start);
        }

        private static RichTextRange GetAffectedParagraphRange(
            RichTextRange range,
            string text)
        {
            var start = range.Start == 0 ? 0 : text.LastIndexOf('\n', range.Start - 1) + 1;
            var newline = text.IndexOf('\n', range.End);
            var end = newline < 0 ? text.Length : newline + 1;
            return new RichTextRange(start, end - start);
        }

        private partial void ApplyTypingFormatCore(
            RichTextCharacterFormat characterFormat,
            RichTextParagraphFormat paragraphFormat)
        {
            _nativeTypingFormat = characterFormat;
            _nativeTypingParagraphFormat = paragraphFormat;
            if (PlatformView is null)
            {
                return;
            }

            using var characterAttributes = CreateCharacterAttributes(
                characterFormat,
                VirtualView?.Document.DefaultCharacterFormat);
            using var paragraphAttributes = CreateParagraphAttributes(paragraphFormat);
            var attributes = new NSMutableDictionary(characterAttributes);
            attributes.AddEntries(paragraphAttributes);
            PlatformView.TypingAttributes2 = attributes;
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
            PlatformView.SelectedRange = new NSRange(start, length);
        }

        private partial bool TryCutCore()
        {
            if (PlatformView is null ||
                VirtualView.IsReadOnly ||
                PlatformView.SelectedRange.Length == 0)
            {
                return false;
            }

            PlatformView.Cut(null);
            VirtualView.UpdateUndoStateFromPlatform();
            return true;
        }

        private partial bool SupportsNativeUndoCore() => true;

        private partial bool CanUndoCore()
        {
            var manager = PlatformView?.UndoManager;
            EnsureUndoManagerNotifications(manager);
            return manager?.CanUndo == true;
        }

        private partial bool CanRedoCore()
        {
            var manager = PlatformView?.UndoManager;
            EnsureUndoManagerNotifications(manager);
            return manager?.CanRedo == true;
        }

        private partial void UndoCore()
        {
            if (VirtualView.IsReadOnly || PlatformView?.UndoManager is not { CanUndo: true } manager)
            {
                return;
            }

            manager.Undo();
            VirtualView.UpdateUndoStateFromPlatform();
        }

        private partial void RedoCore()
        {
            if (VirtualView.IsReadOnly || PlatformView?.UndoManager is not { CanRedo: true } manager)
            {
                return;
            }

            manager.Redo();
            VirtualView.UpdateUndoStateFromPlatform();
        }

        private partial void ClearUndoHistoryCore()
        {
            PlatformView?.UndoManager?.RemoveAllActions();
            VirtualView?.UpdateUndoStateFromPlatform();
        }

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
            if (editor.IsSet(RichEditor.FontFamilyProperty) ||
                editor.IsSet(RichEditor.FontSizeProperty))
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

            if (!_applyingDocument)
            {
                var snapshot = editor.Document.CurrentSnapshot;
                _applyingDocument = true;
                PlatformView.TextStorage.BeginEditing();
                try
                {
                    ApplyCharacterFormatsIncrementally(
                        snapshot,
                        new RichTextRange(0, snapshot.Length));
                    SetSelectionCore(editor.SelectedRange.Start, editor.SelectedRange.Length);
                }
                finally
                {
                    PlatformView.TextStorage.EndEditing();
                    _applyingDocument = false;
                }

                ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
            }
        }

        private partial void UpdateInputConfiguration(RichEditor editor)
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
                  PlatformView.TextColor ??
                  UIColor.Label;
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
            var attributed = PlatformView.AttributedText ?? new NSAttributedString(string.Empty);
            var text = attributed.Value ?? string.Empty;
            var previous = VirtualView.Document.CurrentSnapshot;
            var remappedPrevious = previous.RemapText(text);
            var defaultCharacterFormat = previous.DefaultCharacterFormat;

            var runs = new List<RichTextRun>();
            var links = new List<RichTextLink>();
            var images = new List<RichTextImage>();
            string? activeLink = null;
            var activeLinkStart = 0;
            for (var position = 0; position < text.Length;)
            {
                var dictionary = attributed.GetAttributes(position, out var effectiveRange) ??
                    new NSDictionary();
                var effectiveEnd = Math.Min(
                    text.Length,
                    checked((int)(effectiveRange.Location + effectiveRange.Length)));
                var end = Math.Max(position + 1, effectiveEnd);
                var format = ReadCharacterFormat(dictionary, defaultCharacterFormat);
                if (runs.Count > 0 && runs[^1].Format == format)
                {
                    runs[^1] = runs[^1] with { Length = runs[^1].Length + end - position };
                }
                else
                {
                    runs.Add(new RichTextRun(position, end - position, format));
                }

                var attributes = new UIStringAttributes(dictionary);
                var link = GetLinkTarget(attributes.Link);
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
                        previous.DefaultParagraphFormat);
                }

                if (format.NativeList is { } list)
                {
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
                format = format with
                {
                    FontFamily = metadata is null || metadata.Format.FontFamily is not null
                        ? font.FamilyName
                        : format.FontFamily,
                    FontSize = metadata is null || metadata.Format.FontSize is not null
                        ? font.PointSize
                        : format.FontSize,
                    FontWeight = traits.HasFlag(UIFontDescriptorSymbolicTraits.Bold)
                        ? Math.Max(format.FontWeight, 700)
                        : metadata is null ? 400 : format.FontWeight,
                    Italic = traits.HasFlag(UIFontDescriptorSymbolicTraits.Italic),
                };
            }

            if (attributes.ForegroundColor is { } foreground && !format.Hidden &&
                (metadata is null || metadata.Format.ForegroundColor is not null))
            {
                format = format with { ForegroundColor = FromUIColor(foreground) };
            }

            if (attributes.BackgroundColor is { } background)
            {
                format = format with { BackgroundColor = FromUIColor(background) };
            }

            if (metadata is null || metadata.Format.Underline == RichTextUnderlineStyle.None)
            {
                format = format with
                {
                    Underline = FromNativeUnderline(
                        attributes.UnderlineStyle ?? NSUnderlineStyle.None),
                };
            }

            if (metadata is null || metadata.Format.Strikethrough == RichTextStrikethroughStyle.None)
            {
                format = format with
                {
                    Strikethrough = attributes.StrikethroughStyle switch
                    {
                        NSUnderlineStyle.Double => RichTextStrikethroughStyle.Double,
                        NSUnderlineStyle.None => RichTextStrikethroughStyle.None,
                        _ => RichTextStrikethroughStyle.Single,
                    },
                };
            }

            if (attributes.UnderlineColor is { } underlineColor)
            {
                format = format with { UnderlineColor = FromUIColor(underlineColor) };
            }

            if (attributes.StrikethroughColor is { } strikeColor)
            {
                format = format with { StrikethroughColor = FromUIColor(strikeColor) };
            }

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

            return format with
            {
                Alignment = style.Alignment switch
                {
                    UITextAlignment.Center => RichTextAlignment.Center,
                    UITextAlignment.Right => RichTextAlignment.Right,
                    UITextAlignment.Justified => RichTextAlignment.Justified,
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
                LineSpacingRule = lineSpacingRule,
                LineSpacing = Math.Max(lineSpacing, 0),
                MinimumLineHeight = style.MinimumLineHeight > 0 ? style.MinimumLineHeight : null,
                MaximumLineHeight = style.MaximumLineHeight > 0 ? style.MaximumLineHeight : null,
                TabStops = (style.TabStops ?? [])
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

            if (value.HasFlag(NSUnderlineStyle.PatternDot))
            {
                return RichTextUnderlineStyle.Dotted;
            }

            if (value.HasFlag(NSUnderlineStyle.PatternDashDotDot))
            {
                return RichTextUnderlineStyle.DashDotDot;
            }

            if (value.HasFlag(NSUnderlineStyle.PatternDashDot))
            {
                return RichTextUnderlineStyle.DashDot;
            }

            if (value.HasFlag(NSUnderlineStyle.PatternDash))
            {
                return RichTextUnderlineStyle.Dash;
            }

            return value.HasFlag(NSUnderlineStyle.Thick)
                ? RichTextUnderlineStyle.Thick
                : RichTextUnderlineStyle.Single;
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
            return currentLength - removedLength + replacementText.Length <= VirtualView.MaxLength;
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

            var attributed = textView.AttributedText;
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
                if (attributes.Link is not null || attributes.TextAttachment is not null ||
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

        private void OnNativeDocumentChanged(UITextView textView)
        {
            if (_applyingDocument || VirtualView is null)
            {
                return;
            }

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
            VirtualView.UpdateDocumentFromPlatform(document, start, length, _sourceToken);
            VirtualView.UpdateUndoStateFromPlatform();
            UpdateTypingFormatsFromPlatform();
        }

        private void OnNativeSelectionChanged(UITextView textView)
        {
            if (_applyingDocument || VirtualView is null)
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

            VirtualView.UpdateTypingFormatsFromPlatform(
                _nativeTypingFormat,
                _nativeTypingParagraphFormat);
        }

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

        private sealed record NativeUndoTransition(
            RichTextDocumentSnapshot TargetSnapshot,
            RichTextRange TargetSelection,
            RichTextDocumentSnapshot InverseSnapshot,
            RichTextRange InverseSelection,
            string? ActionName)
        {
            public NativeUndoTransition Reverse() =>
                new(
                    InverseSnapshot,
                    InverseSelection,
                    TargetSnapshot,
                    TargetSelection,
                    ActionName);
        }

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
