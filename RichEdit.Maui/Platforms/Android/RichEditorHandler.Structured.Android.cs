using Android.Text.Style;
using RichEdit.Core;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial void ApplyStructuredDecorationFormatsCore(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after)
    {
        var applying = _applyingDocument;
        _applyingDocument = true;
        PlatformView.BeginBatchEdit();
        try { ApplyCharacterFormatsIncrementally(PlatformView.EditableText!, after, new(0, after.Length), before); }
        finally { PlatformView.EndBatchEdit(); _applyingDocument = applying; }
    }

    private partial StructuredRichNativeMap CreateStructuredNativeDocumentCore(RichDocumentSnapshot snapshot, bool apply)
    {
        var map = RichTextDocumentSnapshot.CreateExpandedFields(VirtualView.ProjectStructuredInput(snapshot));
        if (apply)
        {
            SetStructuredNativeMap(map, captureFormats: false);
            ApplyDisplayDocumentCore(map.Display!, VirtualView.SelectedRange.Start, VirtualView.SelectedRange.Length);
        }
        return map;
    }

    private partial StructuredNativeFormats CaptureStructuredFormatsCore(StructuredRichNativeMap map)
    {
        var runs = new List<RichTextRun>();
        var paragraphs = new Dictionary<RichNodeId, RichTextParagraphFormat>();
        var text = PlatformView.EditableText!;
        foreach (var paragraph in map.Paragraphs)
        {
            paragraphs.Add(paragraph.Id, ReadParagraphFormat(text, paragraph.Start, paragraph.End,
                RichTextDocumentSnapshot.ToNative(map.Snapshot.ResolveParagraph(paragraph.Id))));
            for (var offset = paragraph.Start; offset < paragraph.End;)
            {
                var next = Math.Min(text.NextSpanTransition(offset, paragraph.End, SpanType<CharacterStyle>.Value),
                    text.NextSpanTransition(offset, paragraph.End, SpanType<RichCharacterMetadataSpan>.Value));
                next = Math.Clamp(next, offset + 1, paragraph.End);
                runs.Add(new(offset, next - offset, ReadCharacterFormat(text, offset, RichTextCharacterFormat.Default)));
                offset = next;
            }
        }
        return new(runs, paragraphs);
    }
}
