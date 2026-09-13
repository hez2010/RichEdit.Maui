using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichTextDocumentSnapshot ReadDocumentFromPlatform()
    {
        var snapshot = GetNativeTextSnapshot();
        var text = snapshot.Text;
        var nativeDocument = PlatformView.Document;
        var previous = DisplaySourceSnapshot;
        var remappedPrevious = previous.RemapText(text, NativeProjection.ToDisplay(VirtualView.SelectedRange));
        var defaultCharacterFormat = ReadCharacterFormat(
            nativeDocument.GetDefaultCharacterFormat()) with
        {
            FontFamily = previous.DefaultCharacterFormat.FontFamily,
            FontSize = previous.DefaultCharacterFormat.FontSize,
            ForegroundColor = previous.DefaultCharacterFormat.ForegroundColor,
        };
        var defaultParagraphFormat = ReadParagraphFormat(
            nativeDocument.GetDefaultParagraphFormat(), null);
        var scanRange = nativeDocument.GetRange(0, 0);
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
            scanRange.SetRange(
                snapshot.ToNativePosition(start),
                snapshot.ToNativePosition(end));
            var native = scanRange.ParagraphFormat;
            var paragraphFormat = ReadParagraphFormat(native, previousList);
            if (paragraphFormat.NativeList is { } list)
            {
                var continues = previousList is not null &&
                    previousList.Kind == list.Kind &&
                    previousList.NumberStyle == list.NumberStyle &&
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
                paragraphFormat = paragraphFormat with { NativeList = list };
                previousList = list;
            }
            else
            {
                previousList = null;
            }

            paragraphs.Add(new RichTextParagraph(start, paragraphFormat));
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }

        var fontFamily = previous.DefaultCharacterFormat.FontFamily ?? ResolveFontFamily();
        var fontSize = previous.DefaultCharacterFormat.FontSize ?? ResolveFontSize();
        var textColor = previous.DefaultCharacterFormat.ForegroundColor ?? ResolveTextColor();
        return previous.MergeNativeSnapshot(
            text,
            snapshot.Runs,
            paragraphs,
            null,
            null,
            defaultCharacterFormat,
            defaultParagraphFormat,
            (native, prior) => MergeWindowsCharacterFormat(
                native,
                prior,
                fontFamily,
                fontSize,
                textColor),
            MergeWindowsParagraphFormat,
            remappedPrevious);
    }

    private NativeTextSnapshot GetNativeTextSnapshot() =>
        _nativeTextSnapshot ??= CreateNativeTextSnapshot();

    private NativeTextSnapshot CreateNativeTextSnapshot()
    {
        var nativeDocument = PlatformView.Document;
        var rawText = ReadNativeStoryText(nativeDocument);
        var nativeContentLength = rawText.EndsWith('\r')
            ? rawText.Length - 1
            : rawText.Length;
        var text = new StringBuilder(nativeContentLength);
        var nativeToLogical = new int[rawText.Length + 1];
        var logicalToNative = new List<int>(nativeContentLength + 1);
        var runs = new List<RichTextRun>();
        var scanRange = nativeDocument.GetRange(0, 0);
        for (var nativePosition = 0; nativePosition < nativeContentLength;)
        {
            scanRange.SetRange(nativePosition, nativePosition + 1);
            scanRange.Expand(TextRangeUnit.CharacterFormat);
            var nativeEnd = Math.Clamp(
                scanRange.EndPosition,
                nativePosition + 1,
                nativeContentLength);
            var characterFormat = scanRange.CharacterFormat;
            var omitFromLogicalText = IsHiddenLinkInstruction(scanRange, characterFormat);
            var logicalStart = text.Length;
            for (var position = nativePosition; position < nativeEnd; position++)
            {
                nativeToLogical[position] = text.Length;
                if (!omitFromLogicalText)
                {
                    logicalToNative.Add(position);
                    text.Append(rawText[position] switch
                    {
                        '\r' => '\n',
                        '\v' => RichTextDocument.SoftLineBreakCharacter,
                        var character => character,
                    });
                }

                nativeToLogical[position + 1] = text.Length;
            }

            if (!omitFromLogicalText && text.Length > logicalStart)
            {
                runs.Add(new RichTextRun(
                    logicalStart,
                    text.Length - logicalStart,
                    ReadCharacterFormat(characterFormat)));
            }

            nativePosition = nativeEnd;
        }

        for (var position = nativeContentLength; position < nativeToLogical.Length; position++)
        {
            nativeToLogical[position] = text.Length;
        }

        logicalToNative.Add(nativeContentLength);
        return new NativeTextSnapshot(
            text.ToString(),
            [.. runs],
            nativeToLogical,
            [.. logicalToNative]);
    }

    private static bool IsHiddenLinkInstruction(
        ITextRange range,
        ITextCharacterFormat format)
    {
        if (format.Hidden != FormatEffect.On)
        {
            return false;
        }

        try
        {
            return !string.IsNullOrWhiteSpace(range.Link);
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static string ToNativeLink(string target) =>
        $"\"{target.Replace("\"", "%22", StringComparison.Ordinal)}\"";

    private void OnNativeDocumentChanged(RichEditBox sender, RichEditBoxTextChangingEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }
        if (!eventArgs.IsContentChanging)
        {
            QueueNativeFormatReadback();
            return;
        }

        _nativeTextSnapshot = null;
        // TextChanging is synchronous. TextChanged runs after rendering, when
        // the projection guard has already been released and edits can coalesce.
        ReadNativeDocumentChange();
    }

    private void QueueNativeFormatReadback()
    {
        if (_nativeFormatReadbackQueued)
            return;

        var document = VirtualView.Document;
        var version = document.Version;
        _nativeFormatReadbackQueued = true;
        PlatformView.DispatcherQueue.TryEnqueue(() =>
        {
            _nativeFormatReadbackQueued = false;
            if (_applyingDocument || !ReferenceEquals(VirtualView?.Document, document) || document.Version != version)
                return;
            // Formatting also raises IsContentChanging=false. Once the native
            // operation finishes, only a real edit adds native undo state;
            // focus/selection notifications do not. Projection clears that state.
            if (PlatformView.Document.CanUndo())
            {
                _nativeTextSnapshot = null;
                ReadNativeDocumentChange();
            }
        });
    }

    private void ReadNativeDocumentChange()
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var nativeStart = Math.Min(selection.StartPosition, selection.EndPosition);
        var nativeEnd = Math.Max(selection.StartPosition, selection.EndPosition);
        RichTextDocumentSnapshot document;
        int start;
        int end;
        if (!_hasNativeLinks &&
            TryReadIncrementalNativeDocument(nativeStart, nativeEnd, out document, out start, out end))
        {
            _nativeTextSnapshot = null;
        }
        else
        {
            document = ReadDocumentFromPlatform();
            var snapshot = GetNativeTextSnapshot();
            start = snapshot.ToLogicalPosition(nativeStart);
            end = snapshot.ToLogicalPosition(nativeEnd);
        }

        _hasNativeLinks = document.Links.Length != 0;
        var sourceLength = ReadDisplayReservationMetadata(document.Text).ToSource(document.Length);
        if (VirtualView.MaxLength >= 0 && sourceLength > VirtualView.MaxLength && sourceLength > VirtualView.Document.Length)
        {
            // WinUI interprets MaxLength=0 as unlimited; the portable API uses -1.
            // Also cover text services that bypass the native input length limit.
            ApplyCurrentDocument(
                VirtualView.SelectedRange.Start, VirtualView.SelectedRange.Length);
            ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
            return;
        }

        var length = end - start;
        UpdateCompositionState();
        UpdateDocumentFromDisplay(document, start, length, _sourceToken, selectionState:
            (selection.Options & SelectionOptions.StartActive) != 0 ? new RichTextSelectionState(end, start) : new RichTextSelectionState(start, end));
        PlatformView.Document.ClearUndoRedoHistory();
        VirtualView.UpdateUndoStateFromPlatform();
        UpdateTypingFormatsFromPlatform();
    }

    private bool TryReadIncrementalNativeDocument(
        int nativeSelectionStart,
        int nativeSelectionEnd,
        out RichTextDocumentSnapshot document,
        out int selectionStart,
        out int selectionEnd)
    {
        var previous = DisplaySourceSnapshot;
        var text = ReadNativePlainText();
        selectionStart = Math.Clamp(nativeSelectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(nativeSelectionEnd, selectionStart, text.Length);

        var prefixLength = previous.Text.AsSpan().CommonPrefixLength(text);
        var suffixLength = 0;
        var maximumSuffixLength = Math.Min(previous.Length, text.Length) - prefixLength;
        while (suffixLength < maximumSuffixLength &&
               previous.Text[^(suffixLength + 1)] == text[^(suffixLength + 1)])
        {
            suffixLength++;
        }

        var oldEnd = previous.Length - suffixLength;
        var newEnd = text.Length - suffixLength;
        var insertedText = text.Substring(prefixLength, newEnd - prefixLength);
        var replacedRange = NativeProjection.ToDisplay(VirtualView.SelectedRange);
        if (RichTextDocumentSnapshot.TryGetReplacement(previous.Text, text, replacedRange, out var knownInsertion))
        {
            prefixLength = replacedRange.Start;
            oldEnd = replacedRange.End;
            insertedText = knownInsertion;
        }

        var textChanged = !string.Equals(previous.Text, text, StringComparison.Ordinal);
        document = textChanged
            ? previous.Replace(prefixLength..oldEnd, insertedText, _nativeTypingFormat)
            : previous;

        // A content callback with unchanged text can represent a native formatting
        // command. In that case inspect only the current selection (or its caret
        // neighbor), never the complete story.
        var characterRange = new RichTextRange(prefixLength, insertedText.Length);
        if (characterRange.IsEmpty)
        {
            characterRange = selectionEnd > selectionStart
                ? new RichTextRange(selectionStart, selectionEnd - selectionStart)
                : GetCaretInspectionRange(selectionStart, text.Length);
        }

        if (!TryOverlayNativeCharacterFormats(document, characterRange, out document))
        {
            return false;
        }

        var paragraphSeed = insertedText.Length != 0
            ? characterRange
            : textChanged
                ? new RichTextRange(Math.Min(prefixLength, text.Length), 0)
                : selectionEnd > selectionStart
                    ? new RichTextRange(selectionStart, selectionEnd - selectionStart)
                    : new RichTextRange(selectionStart, 0);
        var paragraphRange = GetAffectedParagraphRange(paragraphSeed, text);
        return TryOverlayNativeParagraphFormats(document, paragraphRange, out document);
    }

    private string ReadNativePlainText()
    {
        var rawText = ReadNativeStoryText(PlatformView.Document);
        var length = rawText.EndsWith('\r') ? rawText.Length - 1 : rawText.Length;
        var content = rawText.AsSpan(0, length);
        if (content.IndexOfAny('\r', '\v') < 0)
        {
            return length == rawText.Length ? rawText : rawText[..length];
        }

        return string.Create(length, rawText, static (destination, source) =>
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[index] switch
                {
                    '\r' => '\n',
                    '\v' => RichTextDocument.SoftLineBreakCharacter,
                    var character => character,
                };
            }
        });
    }

    private static string ReadNativeStoryText(RichEditTextDocument nativeDocument)
    {
        var range = nativeDocument.GetRange(0, 0);
        range.SetRange(0, range.StoryLength);
        return range.Text ?? string.Empty;
    }

    private bool TryOverlayNativeCharacterFormats(
        RichTextDocumentSnapshot source,
        RichTextRange range,
        out RichTextDocumentSnapshot result)
    {
        result = source;
        range = range.Clamp(source.Length);
        if (range.IsEmpty)
        {
            return true;
        }

        var nativeRange = PlatformView.Document.GetRange(0, 0);
        var fontFamily = source.DefaultCharacterFormat.FontFamily ?? ResolveFontFamily();
        var fontSize = source.DefaultCharacterFormat.FontSize ?? ResolveFontSize();
        var textColor = source.DefaultCharacterFormat.ForegroundColor ?? ResolveTextColor();
        for (var position = range.Start; position < range.End;)
        {
            nativeRange.SetRange(position, position + 1);
            nativeRange.Expand(TextRangeUnit.CharacterFormat);
            var end = Math.Clamp(nativeRange.EndPosition, position + 1, range.End);
            var nativeFormat = nativeRange.CharacterFormat;
            if (IsHiddenLinkInstruction(nativeRange, nativeFormat))
            {
                return false;
            }

            var previousFormat = result.GetCharacterFormat(position);
            var merged = MergeWindowsCharacterFormat(
                ReadCharacterFormat(nativeFormat),
                previousFormat,
                fontFamily,
                fontSize,
                textColor);
            result = result.ApplyCharacterFormat(position..end, _ => merged);
            position = end;
        }

        return true;
    }

    private bool TryOverlayNativeParagraphFormats(
        RichTextDocumentSnapshot source,
        RichTextRange range,
        out RichTextDocumentSnapshot result)
    {
        result = source;
        var text = source.Text;
        var scanRange = PlatformView.Document.GetRange(0, 0);
        var lastParagraphStart = range.End == 0
            ? 0
            : text.LastIndexOf('\n', Math.Min(range.End, text.Length) - 1) + 1;
        for (var start = range.Start; ;)
        {
            var end = GetParagraphEnd(text, start);
            var previousFormat = result.GetParagraphFormat(start);
            scanRange.SetRange(start, end);
            var nativeFormat = ReadParagraphFormat(
                scanRange.ParagraphFormat,
                previousFormat.NativeList);
            var merged = MergeWindowsParagraphFormat(nativeFormat, previousFormat);

            RichTextListItemFormat? item = null;
            if (nativeFormat.NativeList is { } nativeList)
            {
                if (previousFormat.List is not { } previousItem ||
                    !result.Lists.TryGetValue(previousItem.ListId, out var definition) ||
                    (uint)nativeList.Level >= (uint)definition.Levels.Length)
                {
                    // A newly introduced native list needs the full list-definition
                    // discovery path. Ordinary continuation, termination, level, and
                    // restart edits remain bounded here.
                    return false;
                }

                var preservesListIdentity =
                    previousFormat.NativeList is { } previousNativeList &&
                    previousItem.ListId.Value == nativeList.Id &&
                    previousNativeList.Id == nativeList.Id &&
                    previousNativeList.Kind == nativeList.Kind &&
                    previousNativeList.NumberStyle == nativeList.NumberStyle &&
                    previousItem.Level == nativeList.Level;
                item = new RichTextListItemFormat(
                    previousItem.ListId,
                    nativeList.Level,
                    nativeList.Restart
                        ? nativeList.StartAt
                        : preservesListIdentity
                            ? previousItem.RestartAt
                            : null);
            }

            merged = merged with { List = item };
            var paragraphRange = end == start ? start..start : start..end;
            result = result.ApplyParagraphFormat(paragraphRange, _ => merged);
            if (start >= lastParagraphStart)
            {
                break;
            }

            if (end == start)
            {
                break;
            }

            start = end;
        }

        return true;
    }

    private static RichTextRange GetCaretInspectionRange(int position, int textLength)
    {
        if (textLength == 0)
        {
            return RichTextRange.Empty;
        }

        var start = position == textLength ? textLength - 1 : position;
        return new RichTextRange(start, 1);
    }

    private void OnNativeSelectionChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var nativeStart = Math.Min(selection.StartPosition, selection.EndPosition);
        var nativeEnd = Math.Max(selection.StartPosition, selection.EndPosition);
        var start = nativeStart;
        var end = nativeEnd;
        if (_hasNativeLinks)
        {
            var snapshot = GetNativeTextSnapshot();
            start = snapshot.ToLogicalPosition(nativeStart);
            end = snapshot.ToLogicalPosition(nativeEnd);
        }

        start = Math.Clamp(start, 0, DisplaySourceSnapshot.Text.Length);
        end = Math.Clamp(end, start, DisplaySourceSnapshot.Text.Length);
        var length = end - start;
        UpdateSelectionFromDisplay((selection.Options & SelectionOptions.StartActive) != 0 ?
            new RichTextSelectionState(end, start) : new RichTextSelectionState(start, end));
        UpdateTypingFormatsFromPlatform();
    }

    private void UpdateTypingFormatsFromPlatform()
    {
        if (VirtualView is null)
        {
            return;
        }

        var previousCharacter = VirtualView.TypingCharacterFormat;
        var nativeCharacter = ReadCharacterFormat(PlatformView.Document.Selection.CharacterFormat);
        var defaultCharacter = VirtualView.Document.DefaultCharacterFormat;
        _nativeTypingFormat = MergeWindowsCharacterFormat(
            nativeCharacter,
            previousCharacter,
            defaultCharacter.FontFamily ?? ResolveFontFamily(),
            defaultCharacter.FontSize ?? ResolveFontSize(),
            defaultCharacter.ForegroundColor ?? ResolveTextColor());
        var previousParagraph = VirtualView.TypingParagraphFormat;
        var nativeParagraph = ReadParagraphFormat(
            PlatformView.Document.Selection.ParagraphFormat,
            previousParagraph.NativeList);
        _nativeTypingParagraphFormat = MergeWindowsParagraphFormat(
            nativeParagraph,
            previousParagraph);
        _nativeTypingFormat = VirtualView.Decorations.RestoreTypingFormat(_nativeTypingFormat);
        VirtualView.UpdateTypingFormatsFromPlatform(
            _nativeTypingFormat,
            _nativeTypingParagraphFormat);
    }

    private sealed class NativeTextSnapshot(
        string text,
        RichTextRun[] runs,
        int[] nativeToLogical,
        int[] logicalToNative)
    {
        public string Text { get; } = text;

        public IReadOnlyList<RichTextRun> Runs { get; } = runs;

        public int ToLogicalPosition(int nativePosition) =>
            nativeToLogical[Math.Clamp(nativePosition, 0, nativeToLogical.Length - 1)];

        public int ToNativePosition(int logicalPosition) =>
            logicalToNative[Math.Clamp(logicalPosition, 0, logicalToNative.Length - 1)];
    }
}
