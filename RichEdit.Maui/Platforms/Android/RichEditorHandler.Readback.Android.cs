using Android.Text;
using Android.Text.Style;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichTextDocumentSnapshot ReadDocumentFromPlatform(string text, RichTextRange? replacedRange = null)
    {
        if (PlatformView.EditableText is not { } editable)
        {
            return RichTextDocumentSnapshot.FromPlainText(text);
        }

        var previous = DisplaySourceSnapshot;
        var remappedPrevious = previous.RemapText(text, replacedRange);
        var defaultCharacterFormat = previous.DefaultCharacterFormat;
        var inheritedCharacterFormat =
            RichTextDocumentSnapshot.CreateInheritedCharacterFormat(defaultCharacterFormat);
        var runs = new List<RichTextRun>();
        for (var position = 0; position < text.Length;)
        {
            var end = editable.NextSpanTransition(
                position,
                text.Length,
                SpanType<CharacterStyle>.Value);
            end = Math.Min(end, editable.NextSpanTransition(position, text.Length, SpanType<RichCharacterMetadataSpan>.Value));
            if (end <= position)
            {
                end = position + 1;
            }

            var format = ReadCharacterFormat(editable, position, inheritedCharacterFormat);
            if (runs.Count > 0 && runs[^1].Format == format)
            {
                runs[^1] = runs[^1] with { Length = runs[^1].Length + end - position };
            }
            else
            {
                runs.Add(new RichTextRun(position, end - position, format));
            }

            position = end;
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
            var end = GetParagraphEnd(text, start);
            var format = ReadParagraphFormat(
                editable,
                start,
                end,
                previous.DefaultParagraphFormat);
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

        var links = ReadLinks(editable, text.Length);
        var images = ReadImages(editable, text);
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

    internal void BeginNativeTextChange() => _nativeTextChangeDepth++;

    internal void EndNativeTextChange()
    {
        if (_nativeTextChangeDepth > 0 && --_nativeTextChangeDepth == 0)
            UpdateCompositionState();
    }

    private void OnNativeDocumentChanged(object? sender, Android.Text.TextChangedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null || PlatformView?.EditableText is not { } editable)
        {
            return;
        }

        var previousDocument = DisplaySourceSnapshot;
        UpdateCompositionState();
        var previousText = previousDocument.Text;
        var removedStart = Math.Clamp(eventArgs.Start, 0, previousText.Length);
        var removedLength = Math.Clamp(
            eventArgs.BeforeCount,
            0,
            previousText.Length - removedStart);
        var nativeLength = editable.Length();
        var insertedStart = Math.Clamp(eventArgs.Start, 0, nativeLength);
        var insertedLength = Math.Clamp(eventArgs.AfterCount, 0, nativeLength - insertedStart);
        var insertedText = Java.Lang.ICharSequenceExtensions.SubSequence(
            editable,
            insertedStart,
            insertedStart + insertedLength);

        var paragraphStructureChanged = previousText
            .AsSpan(removedStart, removedLength)
            .ContainsAny('\n', RichTextDocument.SoftLineBreakCharacter) ||
            insertedText.AsSpan().ContainsAny('\n', RichTextDocument.SoftLineBreakCharacter);

        var containsPastedRichContent = insertedLength > 0 &&
            ContainsPastedRichContent(editable, insertedStart, insertedStart + insertedLength);
        var containsPastedListContent = insertedLength > 0 &&
            ContainsPastedListContent(editable, insertedStart, insertedStart + insertedLength);
        var requiresNativeSnapshot = containsPastedRichContent ||
            containsPastedListContent;
        RichTextDocumentSnapshot document;
        if (requiresNativeSnapshot)
        {
            document = ReadDocumentFromPlatform(editable.ToString() ?? string.Empty, new RichTextRange(removedStart, removedLength));
        }
        else
        {
            document = previousDocument.Replace(
                removedStart..(removedStart + removedLength),
                insertedText,
                insertedLength == 0 ? null : _nativeTypingFormat);
        }

        insertedStart = Math.Clamp(insertedStart, 0, document.Text.Length);
        insertedLength = Math.Clamp(insertedLength, 0, document.Text.Length - insertedStart);
        // Keep native metadata bounded by the painted runs. Normalizing against an
        // authored run spanning the whole document would rebuild every decorated run.
        var presentation = PreserveNativePresentation(document);

        if (!requiresNativeSnapshot && insertedLength > 0)
        {
            ApplyInsertedTypingFormat(
                editable,
                presentation,
                new RichTextRange(insertedStart, insertedLength));
        }

        if (paragraphStructureChanged)
        {
            _applyingDocument = true;
            try
            {
                // EditText moves Paragraph spans but does not split our custom
                // list markers, spacing, and borders into new paragraph objects.
                ApplyParagraphFormatsIncrementally(editable, document,
                    GetAffectedParagraphRange(new RichTextRange(insertedStart, insertedLength), document.Text));
                UpdateGlobalParagraphProjection(document);
            }
            finally
            {
                _applyingDocument = false;
            }
        }

        var start = Math.Clamp(Math.Min(PlatformView.SelectionStart, PlatformView.SelectionEnd), 0, document.Text.Length);
        var end = Math.Clamp(Math.Max(PlatformView.SelectionStart, PlatformView.SelectionEnd), start, document.Text.Length);
        UpdateDocumentFromDisplay(document, start, end - start, _sourceToken, selectionState: new RichTextSelectionState(
            Math.Clamp(PlatformView.SelectionStart, 0, document.Length), Math.Clamp(PlatformView.SelectionEnd, 0, document.Length)), presentation: presentation);
        WatchNativeFormats();
        UpdateTypingFormatsFromPlatform();
    }

    private void WatchNativeFormats()
    {
        if (_formatWatcher is null || PlatformView.EditableText is not { } text || ReferenceEquals(text, _watchedText))
            return;

        _watchedText?.RemoveSpan(_formatWatcher);
        _watchedText = text;
        text.SetSpan(_formatWatcher, 0, text.Length(), SpanTypes.InclusiveInclusive);
    }

    private void QueueNativeFormatReadback(ISpannable text, Java.Lang.Object span)
    {
        if (_applyingDocument || VirtualView is null ||
            span is not (CharacterStyle or IParagraphStyle) ||
            span is RichCharacterMetadataSpan or RichCharacterEffectsSpan or RichSmallCapsSpan or RichParagraphMetadataSpan ||
            (text.GetSpanFlags(span) & SpanTypes.Composing) != 0)
        {
            return;
        }

        if (!_nativeFormatReadbackQueued || !ReferenceEquals(_queuedDocument, VirtualView.Document) || _queuedProjectionGeneration != _projectionGeneration)
        {
            _queuedDocument = VirtualView.Document;
            _queuedVersion = _queuedDocument.Version;
            _queuedProjectionGeneration = _projectionGeneration;
        }
        if (_nativeFormatReadbackQueued)
            return;

        _nativeFormatReadbackQueued = true;
        PlatformView.Post(() =>
        {
            _nativeFormatReadbackQueued = false;
            if (_applyingDocument || _formatWatcher is null || !ReferenceEquals(VirtualView?.Document, _queuedDocument) || _queuedProjectionGeneration != _projectionGeneration)
                return;

            var snapshot = ReadDocumentFromPlatform(PlatformView.Text ?? string.Empty);
            var start = Math.Clamp(Math.Min(PlatformView.SelectionStart, PlatformView.SelectionEnd), 0, snapshot.Length);
            var end = Math.Clamp(Math.Max(PlatformView.SelectionStart, PlatformView.SelectionEnd), start, snapshot.Length);
            UpdateDocumentFromDisplay(snapshot, start, end - start, _sourceToken, mergeWithPrevious: _queuedDocument!.Version != _queuedVersion);
            UpdateTypingFormatsFromPlatform();
        });
    }

    private sealed class NativeFormatWatcher(RichEditorHandler handler) : Java.Lang.Object, ISpanWatcher, INoCopySpan
    {
        private readonly WeakReference<RichEditorHandler> _handler = new(handler);

        public void OnSpanAdded(ISpannable? text, Java.Lang.Object? what, int start, int end) => Changed(text, what);

        public void OnSpanRemoved(ISpannable? text, Java.Lang.Object? what, int start, int end) => Changed(text, what);

        public void OnSpanChanged(ISpannable? text, Java.Lang.Object? what, int oldStart, int oldEnd, int newStart, int newEnd) => Changed(text, what, moved: true);

        private void Changed(ISpannable? text, Java.Lang.Object? what, bool moved = false)
        {
            if (text is not null && what is not null && _handler.TryGetTarget(out var handler))
            {
                // Text edits already reconcile source and composition once. Moving
                // every later span is not a separate native formatting operation.
                if (handler._applyingDocument || moved && handler._nativeTextChangeDepth != 0)
                    return;

                handler.UpdateCompositionState();
                handler.QueueNativeFormatReadback(text, what);
            }
        }
    }

    private void OnNativeSelectionChanged(object? sender, NativeSelectionChangedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var start = Math.Clamp(
            Math.Min(eventArgs.Start, eventArgs.End),
            0,
            DisplaySourceSnapshot.Text.Length);
        var end = Math.Clamp(
            Math.Max(eventArgs.Start, eventArgs.End),
            start,
            DisplaySourceSnapshot.Text.Length);
        UpdateSelectionFromDisplay(new RichTextSelectionState(
            Math.Clamp(eventArgs.Start, 0, DisplaySourceSnapshot.Length), Math.Clamp(eventArgs.End, 0, DisplaySourceSnapshot.Length)));
        UpdateTypingFormatsFromPlatform();
    }

    private void OnNativeEditingCompleted(object? sender, EventArgs eventArgs) =>
        VirtualView?.RaiseCompleted();

    private void UpdateTypingFormatsFromPlatform()
    {
        if (VirtualView is null || PlatformView.EditableText is not { } editable)
        {
            return;
        }

        if (editable.Length() == 0)
        {
            _nativeTypingFormat = VirtualView.TypingCharacterFormat;
            _nativeTypingParagraphFormat = VirtualView.TypingParagraphFormat;
        }
        else
        {
            var caret = Math.Clamp(PlatformView.SelectionEnd, 0, editable.Length());
            var characterPosition = caret == editable.Length() ? caret - 1 : caret;
            _nativeTypingFormat = ReadCharacterFormat(
                editable,
                characterPosition,
                VirtualView.TypingCharacterFormat);
            var paragraphStart = GetParagraphStart(editable, caret);
            var paragraphEnd = GetParagraphEnd(editable, paragraphStart);
            _nativeTypingParagraphFormat = ReadParagraphFormat(
                editable,
                paragraphStart,
                paragraphEnd,
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

    private static bool ContainsPastedRichContent(ISpanned text, int start, int end)
    {
        if (end <= start)
        {
            return false;
        }

        return GetSpans<CharacterStyle>(text, start, end)
            .Where(span => span is not (RichCharacterEffectsSpan or RichSmallCapsSpan))
            .Where(span => (text.GetSpanFlags(span) & SpanTypes.Composing) == 0)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end);
    }

    private static bool ContainsPastedListContent(ISpanned text, int start, int end) =>
        GetSpans<RichListMarkerSpan>(text, start, end)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end) ||
        GetSpans<BulletSpan>(text, start, end)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end);

    private static int GetParagraphStart(Java.Lang.ICharSequence text, int position)
    {
        var value = text.ToString() ?? string.Empty;
        return GetParagraphStart(value, Math.Clamp(position, 0, value.Length));
    }

    private static int GetParagraphEnd(Java.Lang.ICharSequence text, int start)
    {
        var value = text.ToString() ?? string.Empty;
        return GetParagraphEnd(value, Math.Clamp(start, 0, value.Length));
    }

    private void ApplyInsertedTypingFormat(
        ISpannable text,
        RichTextDocumentSnapshot document,
        RichTextRange range)
    {
        _applyingDocument = true;
        try
        {
            ApplyCharacterFormatsIncrementally(text, document, range);
            if (!GetSpans<RichParagraphMetadataSpan>(text, range.Start, range.End)
                    .Any(span =>
                    {
                        var spanStart = text.GetSpanStart(span);
                        var spanEnd = text.GetSpanEnd(span);
                        return spanStart <= range.Start && spanEnd >= range.End;
                    }))
            {
                // A final empty paragraph has no character on which Android can
                // retain a Paragraph span. Materialize its document format once
                // the first character arrives; existing paragraphs remain wholly
                // under Android's native span-editing behavior.
                ApplyParagraphFormatsIncrementally(
                    text,
                    document,
                    GetAffectedParagraphRange(range, document.Text));
                UpdateGlobalParagraphProjection(document);
            }
        }
        finally
        {
            _applyingDocument = false;
        }
    }
}
