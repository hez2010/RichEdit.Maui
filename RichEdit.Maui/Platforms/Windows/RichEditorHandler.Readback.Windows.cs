using RichEdit.Maui.Platforms.Windows.NativeText;
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

    private void OnNativeDocumentChanged(object? sender, NativeTextChangingEventArgs eventArgs)
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
                if (VirtualView.SourceSession is not null && !_isComposing)
                {
                    ApplyCurrentDocument(VirtualView.SelectedRange.Start, VirtualView.SelectedRange.Length);
                    ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
                    return;
                }
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
        if (_structuredNativeMap is not null)
        {
            UpdateCompositionState();
            if (TryReadStructuredNativeChange(ReadNativeStoryText(PlatformView.Document),
                (selection.Options & SelectionOptions.StartActive) != 0 ? new(nativeEnd, nativeStart) : new(nativeStart, nativeEnd)))
            {
                PlatformView.Document.ClearUndoRedoHistory();
                VirtualView.UpdateUndoStateFromPlatform();
                return;
            }
        }
        RichTextDocumentSnapshot document;
        int start;
        int end;
        TextSpan? replacement = null;
        if (!_hasNativeLinks &&
            TryReadIncrementalNativeDocument(nativeStart, nativeEnd, out document, out start, out end, out replacement))
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
        var sourceLength = VirtualView.MaxLength < 0 || NativeProjection.Reservations.IsEmpty ? document.Length :
            ReadDisplayReservationMetadata(document.Text).ToSource(document.Length);
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
            (selection.Options & SelectionOptions.StartActive) != 0 ? new TextSelectionState(end, start) : new TextSelectionState(start, end), nativeReplacement: replacement);
        PlatformView.Document.ClearUndoRedoHistory();
        VirtualView.UpdateUndoStateFromPlatform();
        UpdateTypingFormatsFromPlatform();
    }

    private bool TryReadIncrementalNativeDocument(
        int nativeSelectionStart,
        int nativeSelectionEnd,
        out RichTextDocumentSnapshot document,
        out int selectionStart,
        out int selectionEnd,
        out TextSpan? replacement)
    {
        replacement = null;
        var previous = DisplaySourceSnapshot;
        if (VirtualView.SourceSession is not null)
        {
            var raw = ReadNativeStoryText(PlatformView.Document);
            var nativeLength = raw.EndsWith('\r') ? raw.Length - 1 : raw.Length;
            var change = FindNativeSourceReplacement(previous.TextBuffer, raw, NativeProjection.ToDisplay(VirtualView.SelectedRange));
            selectionStart = Math.Clamp(nativeSelectionStart, 0, nativeLength);
            selectionEnd = Math.Clamp(nativeSelectionEnd, selectionStart, nativeLength);
            replacement = change.Range;
            document = change.Range.IsEmpty && change.Text.Length == 0 ? previous :
                previous.Replace(change.Range.Start..change.Range.End, change.Text, _nativeTypingFormat);
            return true;
        }
        var text = ReadNativePlainText();
        selectionStart = Math.Clamp(nativeSelectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(nativeSelectionEnd, selectionStart, text.Length);

        var previousText = previous.Text;
        var replacedRange = NativeProjection.ToDisplay(VirtualView.SelectedRange);
        int prefixLength, oldEnd;
        string insertedText;
        // Most native input replaces the captured selection. Validate both
        // unchanged sides with span comparisons before falling back to a diff;
        // do not first walk a multi-megabyte unchanged suffix character by character.
        if (RichTextDocumentSnapshot.TryGetReplacement(previousText, text, replacedRange, out var knownInsertion))
        {
            prefixLength = replacedRange.Start;
            oldEnd = replacedRange.End;
            insertedText = knownInsertion;
        }
        else
        {
            prefixLength = previousText.AsSpan().CommonPrefixLength(text);
            var suffixLength = 0;
            var maximumSuffixLength = Math.Min(previous.Length, text.Length) - prefixLength;
            const int block = 4096;
            while (maximumSuffixLength - suffixLength >= block &&
                previousText.AsSpan(previous.Length - suffixLength - block, block).SequenceEqual(text.AsSpan(text.Length - suffixLength - block, block)))
                suffixLength += block;
            while (suffixLength < maximumSuffixLength && previousText[^(suffixLength + 1)] == text[^(suffixLength + 1)]) suffixLength++;
            oldEnd = previous.Length - suffixLength;
            insertedText = text.Substring(prefixLength, text.Length - suffixLength - prefixLength);
        }

        var textChanged = !previousText.AsSpan(prefixLength, oldEnd - prefixLength).SequenceEqual(insertedText);
        document = textChanged
            ? previous.Replace(prefixLength..oldEnd, insertedText, _nativeTypingFormat)
            : previous;

        // A content callback with unchanged text can represent a native formatting
        // command. In that case inspect only the current selection (or its caret
        // neighbor), never the complete story.
        var characterRange = new TextSpan(prefixLength, insertedText.Length);
        if (characterRange.IsEmpty)
        {
            characterRange = selectionEnd > selectionStart
                ? new TextSpan(selectionStart, selectionEnd - selectionStart)
                : GetCaretInspectionRange(selectionStart, text.Length);
        }

        if (!TryOverlayNativeCharacterFormats(document, characterRange, out document))
        {
            return false;
        }

        var paragraphSeed = insertedText.Length != 0
            ? characterRange
            : textChanged
                ? new TextSpan(Math.Min(prefixLength, text.Length), 0)
                : selectionEnd > selectionStart
                    ? new TextSpan(selectionStart, selectionEnd - selectionStart)
                    : new TextSpan(selectionStart, 0);
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
            source.AsSpan(0, destination.Length).CopyTo(destination);
            destination.Replace('\r', '\n');
            destination.Replace('\v', RichTextDocument.SoftLineBreakCharacter);
        });
    }

    private static string ReadNativeStoryText(WindowsTextDocument nativeDocument)
    {
        var range = nativeDocument.GetRange(0, 0);
        range.SetRange(0, range.StoryLength);
        return range.Text ?? string.Empty;
    }

    private bool TryOverlayNativeCharacterFormats(
        RichTextDocumentSnapshot source,
        TextSpan range,
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
        TextSpan range,
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

    private static TextSpan GetCaretInspectionRange(int position, int textLength)
    {
        if (textLength == 0)
        {
            return TextSpan.Empty;
        }

        var start = position == textLength ? textLength - 1 : position;
        return new TextSpan(start, 1);
    }

    private void OnNativeSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var nativeStart = Math.Min(selection.StartPosition, selection.EndPosition);
        var nativeEnd = Math.Max(selection.StartPosition, selection.EndPosition);
        if (_structuredNativeMap is { } rich)
        {
            if (ReadNativeStoryText(PlatformView.Document) != rich.NativeText) return;
            if (TryUpdateStructuredSelection((selection.Options & SelectionOptions.StartActive) != 0 ? new(nativeEnd, nativeStart) : new(nativeStart, nativeEnd))) return;
        }
        var start = nativeStart;
        var end = nativeEnd;
        if (_hasNativeLinks)
        {
            var snapshot = GetNativeTextSnapshot();
            start = snapshot.ToLogicalPosition(nativeStart);
            end = snapshot.ToLogicalPosition(nativeEnd);
        }

        start = Math.Clamp(start, 0, DisplaySourceSnapshot.Length);
        end = Math.Clamp(end, start, DisplaySourceSnapshot.Length);
        var length = end - start;
        UpdateSelectionFromDisplay((selection.Options & SelectionOptions.StartActive) != 0 ?
            new TextSelectionState(end, start) : new TextSelectionState(start, end));
        UpdateTypingFormatsFromPlatform();
    }

    private void UpdateTypingFormatsFromPlatform()
    {
        if (VirtualView is null)
        {
            return;
        }

        if (UpdateSourceTypingFormats()) return;
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
