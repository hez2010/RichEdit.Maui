using System.Collections.Immutable;
using System.Text;
using RichEdit.Core;

namespace RichEdit.Maui;

public sealed partial class RichTextDocumentSnapshot
{
    internal static StructuredRichNativeMap CreateExpandedFields(RichTextProjection projection)
    {
        var model = projection.Snapshot;
        var text = new StringBuilder();
        var runs = new List<RichTextRun>();
        var paragraphs = new List<RichTextParagraph>();
        var nativeParagraphs = ImmutableArray.CreateBuilder<StructuredRichNativeMap.Paragraph>();
        var links = new List<RichTextLink>();
        var images = new List<RichTextImage>();
        var sourceEnd = 0;
        foreach (var projected in projection.Paragraphs)
        {
            text.Append(projection.Text.GetText(new(sourceEnd, projected.Range.Start - sourceEnd)));
            var start = text.Length;
            if (start == 0 || text[start - 1] == '\n') paragraphs.Add(new(start, ToNative(model.ResolveParagraph(projected.Paragraph))));
            var local = 0;
            var segments = ImmutableArray.CreateBuilder<StructuredRichNativeMap.Segment>();
            foreach (var id in model.Get<RichParagraph>(projected.Paragraph).Inlines)
            {
                var inline = model.Get<RichInline>(id);
                var nativeStart = text.Length;
                var value = inline switch
                {
                    RichText run => run.Content.ToString(), RichSoftBreak => "\u2028",
                    RichField field => field.Result.Length == 0 ? "□" : field.Result,
                    _ => "\uFFFC",
                };
                text.Append(value);
                var length = RichDocumentSnapshot.InlineLength(inline);
                segments.Add(new(projected.Paragraph, local, length, nativeStart, value.Length, inline is RichField or RichImage, inline.Id));
                runs.Add(new(nativeStart, value.Length, ToNative(model.ResolveCharacter(id))));
                if (inline.Link is { } link) links.Add(new(nativeStart, value.Length, link.Target, link.ToolTip));
                if (inline is RichImage image)
                {
                    var asset = model.Assets[image.Asset];
                    images.Add(new() { Position = nativeStart, Data = asset.Data, Width = image.Width.Points, Height = image.Height.Points,
                        AlternativeText = image.AlternativeText, MediaType = asset.Kind switch { RichImageKind.Png => "image/png", RichImageKind.Jpeg => "image/jpeg",
                            RichImageKind.Dib => "image/bmp", RichImageKind.Wmf => "image/wmf", _ => "image/emf" } });
                }
                local += length;
            }
            nativeParagraphs.Add(new(projected.Paragraph, start, text.Length, segments.ToImmutable()));
            sourceEnd = projected.Range.End;
        }
        text.Append(projection.Text.GetText(new(sourceEnd, projection.Text.Length - sourceEnd)));
        var display = new RichTextDocumentSnapshot(text.ToString(), runs, paragraphs, links, images: images,
            defaultCharacterFormat: ToNative(model.DefaultCharacter.Apply(new())), defaultParagraphFormat: ToNative(model.DefaultParagraph.Apply(new())),
            version: model.Revision.IsValid ? model.Revision.Version : 0) { Revision = model.Revision };
        return StructuredRichNativeMap.FromDisplay(projection, display, nativeParagraphs.ToImmutable());
    }
}
