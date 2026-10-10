using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using RichEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichEditBox? _structuredScratch;

    private partial StructuredNativeFormats CaptureStructuredFormatsCore(StructuredRichNativeMap map)
    {
        var runs = new List<RichTextRun>();
        var paragraphs = new Dictionary<RichNodeId, RichTextParagraphFormat>();
        var range = PlatformView.Document.GetRange(0, 0);
        foreach (var paragraph in map.Paragraphs)
        {
            range.SetRange(paragraph.Start, paragraph.Start);
            paragraphs.Add(paragraph.Id, ReadParagraphFormat(range.ParagraphFormat, null));
            for (var offset = paragraph.Start; offset < paragraph.End;)
            {
                range.SetRange(offset, offset + 1);
                var format = ReadCharacterFormat(range.CharacterFormat);
                range.Expand(TextRangeUnit.CharacterFormat);
                var end = Math.Clamp(range.EndPosition, offset + 1, paragraph.End);
                runs.Add(new(offset, end - offset, format));
                offset = end;
            }
        }
        return new(runs, paragraphs);
    }

    private partial StructuredRichNativeMap CreateStructuredNativeDocumentCore(RichDocumentSnapshot snapshot, bool apply)
    {
        // The writer uses an owned MemoryStream, so preparation has no pending
        // asynchronous I/O. All native work remains on the document's UI thread.
        var defaults = PlatformView.Document.GetDefaultCharacterFormat();
        var color = ResolveTextColor();
        var selectionColors = StructuredSelectionColors();
        var prepared = StructuredRichNativeMap.PrepareAsync(snapshot, fallbackFont: VirtualView.FontFamily ?? defaults.Name,
            fallbackSize: RichFontSize.FromPoints(VirtualView.FontSize ?? defaults.Size),
            fallbackColor: color is null ? null : new((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255)),
            selectedCells: (VirtualView.StructuredSession!.Composition?.Selection ?? VirtualView.StructuredSession.Selection) as RichCellSelection,
            selectionBackground: selectionColors.Background, selectionForeground: selectionColors.Foreground).GetAwaiter().GetResult();
        var scratch = (_structuredScratch ??= new RichEditBox()).Document;
        scratch.SetText(TextSetOptions.FormatRtf | TextSetOptions.ApplyRtfDocumentDefaults, prepared.Rtf);
        var text = ReadNativeStoryText(scratch);
        var resolved = prepared.Resolve(text);
        scratch.BatchDisplayUpdates();
        try
        {
            foreach (var removal in resolved.Removals.Reverse()) scratch.GetRange(removal.Start, removal.End).Delete(TextRangeUnit.Character, 0);
        }
        finally { scratch.ApplyDisplayUpdates(); }
        scratch.ClearUndoRedoHistory();
        if (ReadNativeStoryText(scratch) != resolved.Map.NativeText) throw new InvalidOperationException("Native rich marker removal changed document coordinates.");
        if (apply)
        {
            var applying = _applyingDocument;
            var readOnly = PlatformView.IsReadOnly;
            _applyingDocument = true;
            try
            {
                PlatformView.IsReadOnly = false;
                var incoming = scratch.GetRange(0, Math.Max(0, resolved.Map.NativeText.Length - 1));
                var target = PlatformView.Document.GetRange(0, 0);
                target.SetRange(0, Math.Max(0, target.StoryLength - 1));
                target.FormattedText = incoming;
                _hasNativeLinks = false; // The structured map addresses raw TOM positions, including field gaps.
                _nativeTextSnapshot = null;
                PlatformView.Document.ClearUndoRedoHistory();
                var actual = ReadNativeStoryText(PlatformView.Document);
                if (actual != resolved.Map.NativeText)
                    throw new InvalidOperationException($"The native rich document changed coordinates during transfer (expected {resolved.Map.NativeText.Length}, actual {actual.Length}, common prefix {actual.AsSpan().CommonPrefixLength(resolved.Map.NativeText)}).");
            }
            finally { PlatformView.IsReadOnly = readOnly; _applyingDocument = applying; }
        }
        return resolved.Map;
    }
}
