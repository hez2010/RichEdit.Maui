#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;


namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial void ApplyDisplayDocumentCore(
        RichTextDocumentSnapshot document,
        int selectionStart,
        int selectionLength)
    {
        // The shared projection has already composed source appearance and reservations.
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
                // Drop UIKit's prior replacement context before replaying the snapshot;
                // otherwise a pending smart replacement can be applied again to it.
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
            if (!wasApplying)
                QueueProjectionReadback();
        }
    }

    private partial void ApplyDecorationsCore(RichTextChangeSet changes)
    {
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            PlatformView.TextStorage.BeginEditing();
            try
            {
                var before = GetPreviousDecorationSnapshot(changes);
                foreach (var change in changes.Changes)
                    ApplyCharacterFormatsIncrementally(DisplayPresentationSnapshot, change.NewRange, before);
            }
            finally
            {
                PlatformView.TextStorage.EndEditing();
            }

            ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
        }
        finally
        {
            _applyingDocument = wasApplying;
        }
    }

    private partial void ApplyDisplayChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
    {
        if (_updatingDisplayProjection)
        {
            ApplyReservationChanges(changes, selection, typingCharacterFormat, typingParagraphFormat);
            return;
        }

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
            ApplyCurrentDocument(selection.Start, selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
            if (!changes.IsTextChanged || _restoringHistory)
            {
                // Replacing attributed content resets UIKit's scroll position.
                // Formatting and history replay must keep the user's viewport.
                PlatformView.LayoutManager.EnsureLayoutForTextContainer(PlatformView.TextContainer);
                PlatformView.LayoutIfNeeded();
                var inset = PlatformView.AdjustedContentInset;
                var maximumX = Math.Max(-inset.Left, PlatformView.ContentSize.Width - PlatformView.Bounds.Width + inset.Right);
                var maximumY = Math.Max(-inset.Top, PlatformView.ContentSize.Height - PlatformView.Bounds.Height + inset.Bottom);
                PlatformView.SetContentOffset(new CGPoint(
                    Math.Clamp(viewport.X, -inset.Left, maximumX),
                    Math.Clamp(viewport.Y, -inset.Top, maximumY)), false);
                _viewportAfterLayout = (VirtualView.Document.Revision, viewport);
                PlatformView.SetNeedsLayout();
            }
        }
        finally
        {
            _applyingDocument = wasApplying;
            _restoringHistory = wasRestoring;
            if (!wasApplying)
                QueueProjectionReadback();
        }
    }
}
#endif
