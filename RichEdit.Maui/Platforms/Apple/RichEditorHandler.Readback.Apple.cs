#if IOS || MACCATALYST
using System.Collections.Immutable;
using System.Runtime.Versioning;
using Foundation;
using UIKit;


namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichTextDocumentSnapshot ReadDocumentFromPlatform()
    {
        var attributed = PlatformView.TextStorage;
        var text = attributed.Value ?? string.Empty;
        var previous = DisplaySourceSnapshot;
        var replacement = _pendingNativeChange is { } pending && pending.Version == previous.Version
            ? new RichTextRange(pending.Start, pending.RemovedLength)
            : NativeProjection.ToDisplay(VirtualView.SelectedRange);
        var remappedPrevious = previous.RemapText(text, replacement);
        var emptiedParagraphStart = -1;
        var deletedLength = previous.Length - text.Length;
        if (deletedLength > 0)
        {
            var start = replacement.Length == deletedLength &&
                RichTextDocumentSnapshot.TryGetReplacement(previous.Text, text, replacement, out var insertedText) && insertedText.Length == 0
                    ? replacement.Start : previous.Text.AsSpan().CommonPrefixLength(text);
            if (start < text.Length && text[start] == '\n' && (start == 0 || text[start - 1] == '\n') &&
                !previous.Text.AsSpan(start, deletedLength).Contains('\n') &&
                previous.Text.AsSpan(start + deletedLength).SequenceEqual(text.AsSpan(start)))
                emptiedParagraphStart = start;
        }
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
            while (remappedPrevious.Runs[priorRunIndex].End <= position)
                priorRunIndex++;

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
            if (start == text.Length || start == emptiedParagraphStart)
            {
                // Deleting an item's last character must keep its own paragraph
                // format, including an empty item between two other paragraphs.
                format = remappedPrevious.GetParagraphFormat(start);
            }
            else
            {
                format = ReadParagraphFormat(
                    attributed.GetAttributes(start, out _) ?? new NSDictionary(),
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

        var currentLength = VirtualView.Document.Length;
        var removedLength = range.Length > int.MaxValue
            ? currentLength
            : Math.Max(0, NativeProjection.ToSource((int)(range.Location + range.Length)) - NativeProjection.ToSource((int)range.Location));
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

    internal void RecordNativeInsertion()
    {
        if (_applyingDocument || _applyingSelection || VirtualView is null)
            return;

        using var marked = PlatformView.MarkedTextRange;
        if (marked is null)
            RecordPendingNativeChange(PlatformView.SelectedRange);
    }

    private RichTextDocumentSnapshot? TryReadIncrementalNativeChange(UITextView textView)
    {
        if (_pendingNativeChange is not { } pending || VirtualView is null)
        {
            return null;
        }

        var previous = DisplaySourceSnapshot;
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
        start = NativeProjection.ToSource(start);
        end = NativeProjection.ToSource(end);
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
        if (NativeProjection.ContainsDisplayCharacter(position))
            return false;

        position = NativeProjection.ToSource(position);
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
        if (VirtualView is null || string.Equals(PlatformView.TextStorage.Value, DisplaySourceSnapshot.Text, StringComparison.Ordinal))
            return;
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
        if (ReferenceEquals(storage, _observedTextStorage))
            return;
        if (_observedTextStorage is { } previous)
            previous.DidProcessEditing -= OnTextStorageProcessed;

        _observedTextStorage = storage;
        storage.DidProcessEditing += OnTextStorageProcessed;
    }

    private void OnTextStorageProcessed(object? sender, NSTextStorageEventArgs args)
    {
        _inputProjection = null;
        if (!_applyingDocument || _applyingTypingFormat)
            _nativeEditGeneration++;

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
        if (_nativeReadbackQueued)
            return;

        _nativeReadbackQueued = true;
        PlatformView.BeginInvokeOnMainThread(() =>
        {
            _nativeReadbackQueued = false;
            // UITextView's Changed callback normally commits synchronously.
            // Text services can edit storage without issuing that callback;
            // wait until their selection and smart-spacing edits are finished.
            if (!_applyingDocument && ReferenceEquals(VirtualView?.Document, _queuedDocument) &&
                _queuedProjectionGeneration == _projectionGeneration && _observedTextStorage is not null &&
                (_readNativeGeneration != _nativeEditGeneration || _observedTextStorage.Value != DisplaySourceSnapshot.Text))
            {
                OnNativeDocumentChanged(PlatformView, mergeWithPrevious: _queuedNativeContinuation || _queuedDocument!.Version != _queuedVersion);
            }
        });
    }

    private void OnNativeDocumentChanged(UITextView textView, bool mergeWithPrevious = false)
    {
        if (_applyingDocument || VirtualView is null)
        {
            if (_applyingTypingFormat && VirtualView is not null)
                QueueNativeReadback();

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

        UpdateCompositionState();
        var previousSelection = VirtualView.SelectedRange;

        PlatformView.UpdatePlaceholderVisibility();
        var generation = _nativeEditGeneration;
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

        _readNativeGeneration = generation;

        var start = Math.Clamp((int)textView.SelectedRange.Location, 0, document.Text.Length);
        var length = Math.Clamp(
            (int)textView.SelectedRange.Length,
            0,
            document.Text.Length - start);
        var sourceLength = ReadDisplayReservationMetadata(document.Text).ToSource(document.Length);
        if (VirtualView.MaxLength >= 0 && sourceLength > VirtualView.MaxLength && sourceLength > VirtualView.Document.Length)
        {
            ApplyCurrentDocument(previousSelection.Start, previousSelection.Length);
            ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
            return;
        }

        UpdateDocumentFromDisplay(
            document,
            start,
            length,
            _sourceToken,
            mergeWithPrevious: mergeWithPrevious,
            projectedVersion: ReferenceEquals(_projectedDocument, VirtualView.Document) ? _projectedVersion : null,
            selectionState: InferDisplaySelectionState(new RichTextRange(start, length)));
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
        if (!string.Equals(textView.Text, DisplaySourceSnapshot.Text, StringComparison.Ordinal))
        {
            return;
        }

        var start = Math.Clamp(
            (int)textView.SelectedRange.Location,
            0,
            DisplaySourceSnapshot.Text.Length);
        var length = Math.Clamp(
            (int)textView.SelectedRange.Length,
            0,
            DisplaySourceSnapshot.Text.Length - start);
        UpdateSelectionFromDisplay(InferDisplaySelectionState(new RichTextRange(start, length)));
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
        var snapshot = DisplaySourceSnapshot;
        var selectionStart = Math.Clamp(
            (int)PlatformView.SelectedRange.Location,
            0,
            snapshot.Length);
        var selectionLength = Math.Clamp(
            (int)PlatformView.SelectedRange.Length,
            0,
            snapshot.Length - selectionStart);
        if (IsCaretInEmptyParagraph(
            snapshot,
            selectionStart,
            selectionLength))
        {
            ApplyEmptyParagraphTypingFormat(
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

        _nativeTypingFormat = VirtualView.Decorations.RestoreTypingFormat(_nativeTypingFormat);
        VirtualView.UpdateTypingFormatsFromPlatform(
            _nativeTypingFormat,
            _nativeTypingParagraphFormat);
    }

    private void ApplyEmptyParagraphTypingFormat(
        RichTextDocumentSnapshot snapshot,
        int selectionStart,
        int selectionLength)
    {
        if (!IsCaretInEmptyParagraph(
            snapshot,
            selectionStart,
            selectionLength))
        {
            return;
        }

        var paragraphFormat = snapshot.GetParagraphFormat(selectionStart);
        using var paragraphAttributes = CreateParagraphAttributes(paragraphFormat);
        using var typingAttributes = PlatformView.TypingAttributes2 is { } current
            ? new NSMutableDictionary(current)
            : new NSMutableDictionary();
        typingAttributes.AddEntries(paragraphAttributes);
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            // An interior empty paragraph still owns a newline in native storage.
            // Repair its inherited style as well as the attributes for future input.
            if (selectionStart < snapshot.Length)
            {
                var existing = PlatformView.TextStorage.GetAttributes(selectionStart, out _);
                if (existing is null || ReadParagraphFormat(existing, paragraphFormat) != paragraphFormat)
                    PlatformView.TextStorage.AddAttributes(paragraphAttributes, new NSRange(selectionStart, 1));
            }

            PlatformView.TypingAttributes2 = typingAttributes;
            _nativeTypingParagraphFormat = paragraphFormat;
        }
        finally
        {
            _applyingDocument = wasApplying;
        }
    }

    private static bool IsCaretInEmptyParagraph(
        RichTextDocumentSnapshot snapshot,
        int selectionStart,
        int selectionLength) =>
            selectionLength == 0 &&
            (selectionStart == 0 || snapshot.Text[selectionStart - 1] == '\n') &&
            (selectionStart == snapshot.Length || snapshot.Text[selectionStart] == '\n');

    private void OnNativeEditingEnded() => VirtualView?.RaiseCompleted();

    private sealed class RichTextViewDelegate(RichEditorHandler handler) : UITextViewDelegate
    {
        private readonly WeakReference<RichEditorHandler> _handler = new(handler);

        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        public override UIMenu? GetEditMenuForText(UITextView textView, NSRange range, UIMenuElement[] suggestedActions) =>
            _handler.TryGetTarget(out var target) ? target.CreateAppleContextMenu(suggestedActions) : null;

        [SupportedOSPlatform("ios26.0")]
        [SupportedOSPlatform("maccatalyst26.0")]
        public override UIMenu? GetEditMenuForText(UITextView textView, NSValue[] ranges, UIMenuElement[] suggestedActions) =>
            _handler.TryGetTarget(out var target) ? target.CreateAppleContextMenu(suggestedActions) : null;

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
                return !target._updatingDisplayProjection;
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
}
#endif
