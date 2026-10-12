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
        var presentation = StructuredRichPresentation.Create(VirtualView, snapshot);
        var sourceProjection = VirtualView.ProjectStructuredInput(snapshot);
        snapshot = presentation?.Render ?? snapshot;
        var font = ResolveFont(RichTextCharacterFormat.Default);
        var color = VirtualView.TextColor ?? _defaultTextColor.ToColor() ?? Colors.Black;
        var selectionColors = StructuredSelectionColors();
        var prepared = StructuredRichNativeMap.PrepareAsync(snapshot, fallbackFont: font.Name, fallbackSize: RichFontSize.FromPoints(font.PointSize),
            fallbackColor: new((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255)),
            selectedCells: (VirtualView.StructuredSession!.Composition?.Selection ?? VirtualView.StructuredSession.Selection) as RichCellSelection,
            selectionBackground: selectionColors.Background, selectionForeground: selectionColors.Foreground,
            nativePrivateParagraphs: presentation?.PrivateParagraphs, nativeImagePlaceholders: true).GetAwaiter().GetResult();
        using var data = NSData.FromArray(System.Text.Encoding.ASCII.GetBytes(prepared.Rtf));
        using var imported = NSAttributedString.Create(data,
            new NSAttributedStringDocumentAttributes { DocumentType = NSDocumentType.RTF }, out var error);
        if (imported is null || error is not null) throw new InvalidOperationException(error?.Description ?? "Native rich import returned no content.");
        using var content = new NSMutableAttributedString(imported);
        var resolved = prepared.Resolve(content.Value ?? "");
        foreach (var removal in resolved.Removals.Reverse()) content.Replace(new NSRange(removal.Start, removal.Length), "");
        if (content.Value != resolved.Map.NativeText) throw new InvalidOperationException("Native rich marker removal changed document coordinates.");
        var hasListMarkers = ApplyStructuredParagraphPresentation(content, resolved.Map, presentation?.PrivateParagraphs);
        // Apple's RTF importer can omit pictures inside table cells. Import the
        // native table/paragraph structure with addressed image characters, then
        // attach the original resources through NSTextAttachment in that storage.
        foreach (var segment in resolved.Map.Paragraphs.SelectMany(paragraph => paragraph.Segments))
            if (segment.Inline is { } id && snapshot.Get<RichInline>(id) is RichImage image)
            {
                if (segment.NativeLength != 1) throw new InvalidOperationException("Native image projection must retain one addressed character.");
                var asset = snapshot.Assets[image.Asset];
                ApplyImage(content, new RichTextImage
                {
                    Position = segment.NativeStart, Data = asset.Data, Width = image.Width.Points, Height = image.Height.Points,
                    AlternativeText = image.AlternativeText,
                    MediaType = asset.Kind switch { RichImageKind.Png => "image/png", RichImageKind.Jpeg => "image/jpeg",
                        RichImageKind.Dib => "image/bmp", RichImageKind.Wmf => "image/wmf", _ => "image/emf" },
                });
            }
        if (apply)
        {
            var applying = _applyingDocument;
            _applyingDocument = true;
            _projectionGeneration++;
            _pendingNativeChange = null;
            try
            {
                _structuredHasListMarkers = hasListMarkers;
                PlatformView.AttributedText = content;
                ObserveTextStorage();
                PlatformView.UpdatePlaceholderVisibility();
                PlatformView.UndoManager?.RemoveAllActions();
            }
            finally { _applyingDocument = applying; }
        }
        var result = resolved.Map.WithAuthoredDisplay(RichTextDisplayProjection.NativeDescription(resolved.Map)
            .With(runs: CaptureStructuredFormats(content, resolved.Map).Characters));
        return presentation is null ? result : result.WithPresentation(presentation, sourceProjection);
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

    private partial void ApplyStructuredReservationsCore(IReadOnlyList<RichTextImage> images)
    {
        var applying = _applyingDocument;
        _applyingDocument = true;
        PlatformView.TextStorage.BeginEditing();
        try { foreach (var image in images) ApplyImage(PlatformView.TextStorage, image); }
        finally { PlatformView.TextStorage.EndEditing(); _applyingDocument = applying; }
        PlatformView.UndoManager?.RemoveAllActions();
    }
}
#endif
