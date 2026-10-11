#if IOS || MACCATALYST
using Foundation;
using RichEdit.Core;
using Microsoft.Maui.Platform;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial StructuredNativeFormats CaptureStructuredFormatsCore(StructuredRichNativeMap map)
        => CaptureStructuredFormats(PlatformView.TextStorage, map);

    private StructuredNativeFormats CaptureStructuredFormats(NSAttributedString text, StructuredRichNativeMap map)
    {
        var runs = new List<RichTextRun>();
        var paragraphs = new Dictionary<RichNodeId, RichTextParagraphFormat>();
        foreach (var paragraph in map.Paragraphs)
        {
            var prior = RichTextDocumentSnapshot.ToNative(map.Snapshot.ResolveParagraph(paragraph.Id));
            if (paragraph.Start < text.Length)
                paragraphs.Add(paragraph.Id, ReadParagraphFormat(text.GetAttributes(paragraph.Start, out _) ?? new NSDictionary(), prior));
            else paragraphs.Add(paragraph.Id, prior);
            for (var offset = paragraph.Start; offset < paragraph.End;)
            {
                var attributes = text.GetAttributes(offset, out var effective) ?? new NSDictionary();
                var end = Math.Clamp(checked((int)(effective.Location + effective.Length)), offset + 1, paragraph.End);
                runs.Add(new(offset, end - offset, ReadCharacterFormat(attributes, RichTextCharacterFormat.Default)));
                offset = end;
            }
        }
        return new(runs, paragraphs);
    }

    private partial StructuredRichNativeMap CreateStructuredNativeDocumentCore(RichDocumentSnapshot snapshot, bool apply)
    {
        var font = ResolveFont(RichTextCharacterFormat.Default);
        var color = VirtualView.TextColor ?? _defaultTextColor.ToColor() ?? Colors.Black;
        var selectionColors = StructuredSelectionColors();
        var prepared = StructuredRichNativeMap.PrepareAsync(snapshot, fallbackFont: font.Name, fallbackSize: RichFontSize.FromPoints(font.PointSize),
            fallbackColor: new((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255)),
            selectedCells: (VirtualView.StructuredSession!.Composition?.Selection ?? VirtualView.StructuredSession.Selection) as RichCellSelection,
            selectionBackground: selectionColors.Background, selectionForeground: selectionColors.Foreground).GetAwaiter().GetResult();
        using var data = NSData.FromArray(System.Text.Encoding.ASCII.GetBytes(prepared.Rtf));
        using var imported = NSAttributedString.Create(data,
            new NSAttributedStringDocumentAttributes { DocumentType = NSDocumentType.RTF }, out var error);
        if (imported is null || error is not null) throw new InvalidOperationException(error?.Description ?? "Native rich import returned no content.");
        using var content = new NSMutableAttributedString(imported);
        var resolved = prepared.Resolve(content.Value ?? "");
        foreach (var removal in resolved.Removals.Reverse()) content.Replace(new NSRange(removal.Start, removal.Length), "");
        if (content.Value != resolved.Map.NativeText) throw new InvalidOperationException("Native rich marker removal changed document coordinates.");
        if (apply)
        {
            var applying = _applyingDocument;
            _applyingDocument = true;
            _projectionGeneration++;
            _pendingNativeChange = null;
            try
            {
                PlatformView.AttributedText = content;
                ObserveTextStorage();
                PlatformView.UpdatePlaceholderVisibility();
                PlatformView.UndoManager?.RemoveAllActions();
            }
            finally { _applyingDocument = applying; }
        }
        return resolved.Map.WithAuthoredDisplay(RichTextDisplayProjection.NativeDescription(resolved.Map)
            .With(runs: CaptureStructuredFormats(content, resolved.Map).Characters));
    }

    private partial void ApplyStructuredDecorationFormatsCore(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after)
    {
        var applying = _applyingDocument;
        _applyingDocument = true;
        PlatformView.TextStorage.BeginEditing();
        try { ApplyCharacterFormatsIncrementally(after, new(0, after.Length), before); }
        finally { PlatformView.TextStorage.EndEditing(); _applyingDocument = applying; }
        PlatformView.UndoManager?.RemoveAllActions();
    }
}
#endif
