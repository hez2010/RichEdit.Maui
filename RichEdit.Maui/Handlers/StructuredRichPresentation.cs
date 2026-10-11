using System.Collections.Immutable;
using RichEdit.Core;

namespace RichEdit.Maui;

// A disposable native-render description. Its synthetic images never enter the
// canonical document, persistence, history, or application-visible rich addresses.
internal sealed class StructuredRichPresentation
{
    internal sealed record Widget(RichTextAdornment Adornment, RichTextPoint Anchor, RichNodeId Image, RichNodeId? Paragraph = null);
    private StructuredRichPresentation(RichDocumentSnapshot source, RichDocumentSnapshot render, RichAddressMap forward, Widget[] widgets)
    {
        Source = source; Render = render; Forward = forward; Backward = forward.Invert(); Widgets = widgets;
        PrivateParagraphs = widgets.Where(item => item.Paragraph is not null).Select(item => item.Paragraph!.Value).ToHashSet();
    }
    internal RichDocumentSnapshot Source { get; }
    internal RichDocumentSnapshot Render { get; }
    internal RichAddressMap Forward { get; }
    internal RichAddressMap Backward { get; }
    internal Widget[] Widgets { get; }
    internal IReadOnlySet<RichNodeId> PrivateParagraphs { get; }

    internal static StructuredRichPresentation? Create(NativeEditorView view, RichDocumentSnapshot snapshot)
    {
        var projection = view.ProjectStructuredInput(snapshot);
        var items = view.Adornments.Where(item => item.IsValid && item.ReservesSpace)
            .Select(item => (Item: item, Point: item.Position <= projection.Text.Length ? projection.GetPoint(item.Position) : null))
            .Where(item => item.Point is not null).ToArray();
        if (items.Length == 0) return null;
        var source = snapshot.CreateReviewProjection(RichReviewMode.Final).Snapshot;
        var maximum = Math.Max(view.StructuredSession!.Document.NextNodeId, source.Nodes.Keys.Max(id => id.Value));
        maximum = Math.Max(maximum, source.Assets.Keys.Select(id => id.Value).DefaultIfEmpty().Max());
        var edit = new RichDocumentEdit(source, maximum);
        try
        {
            var asset = edit.AddImageAsset(RichImageKind.Png, RichTextDisplayProjection.TransparentPixel.AsMemory());
            var widgets = new List<Widget>();
            foreach (var group in items.Where(entry => entry.Item.Options.Placement != RichTextAdornmentPlacement.Inline)
                .GroupBy(entry => entry.Point!.Value.Paragraph).OrderByDescending(group => projection.GetOffset(group.First().Point!.Value with { Offset = 0 })))
            {
                var paragraph = source.Get<RichParagraph>(group.Key);
                var parent = paragraph.Parent!.Value;
                var index = RichDocumentSnapshot.Children(source.Get<RichNode>(parent)).ToList().IndexOf(paragraph.Id);
                foreach (var placement in new[] { RichTextAdornmentPlacement.BelowLine, RichTextAdornmentPlacement.AboveLine })
                    foreach (var entry in group.Where(entry => entry.Item.Options.Placement == placement).Reverse())
                    {
                        var block = edit.InsertParagraph(parent, index + (placement == RichTextAdornmentPlacement.BelowLine ? 1 : 0));
                        edit.SetParagraphStyle(block, paragraph.Style);
                        edit.SetParagraphFormat(block, paragraph.Format with
                        {
                            SpaceBefore = default(RichLength), SpaceAfter = default(RichLength), FirstLineIndent = default(RichLength),
                            LineSpacing = default(RichLineSpacing), Border = (RichBorder?)null, Shading = (RichShading?)null,
                            Background = (RichColor?)null, HeadingLevel = (int?)null,
                        });
                        edit.SetParagraphCharacterFormat(block, new RichCharacterStyle { FontSize = new RichFontSize(1), Hidden = false });
                        var point = new RichTextPoint(source.Body, block, 0);
                        var image = Insert(entry.Item, point);
                        var anchor = entry.Point!.Value with { Offset = placement == RichTextAdornmentPlacement.AboveLine ? 0 : source.GetParagraphLength(paragraph.Id) };
                        widgets.Add(new(entry.Item, anchor, image, block));
                    }
            }
            foreach (var entry in items.Where(entry => entry.Item.Options.Placement == RichTextAdornmentPlacement.Inline)
                .Reverse().OrderByDescending(entry => projection.GetOffset(entry.Point!.Value)))
            {
                var point = entry.Point!.Value;
                widgets.Add(new(entry.Item, point, Insert(entry.Item, point)));
            }
            var result = edit.Finish();
            return new(snapshot, result.Snapshot, result.Map, widgets.ToArray());

            RichNodeId Insert(RichTextAdornment item, RichTextPoint point)
            {
                var size = item.MeasuredSize;
#if WINDOWS
                const double pointsPerUnit = 0.75;
#else
                const double pointsPerUnit = 1;
#endif
                var image = edit.InsertImage(point, asset,
                    RichLength.FromPoints(Math.Max(0.1, size.Width) * pointsPerUnit),
                    RichLength.FromPoints(Math.Max(0.1, size.Height) * pointsPerUnit));
                edit.SetCharacterStyle(point, 1, null);
                edit.SetHyperlink(point, 1, null);
                edit.SetCharacterFormat(point, 1, new RichCharacterStyle
                {
                    Hidden = false, Bold = false, Italic = false, Underline = RichUnderline.None, Strike = RichStrike.None,
                    CharacterSpacing = default(RichLength), HorizontalScalePercent = 100,
                    VerticalPosition = RichVerticalPosition.Normal,
                });
                return image;
            }
        }
        finally { edit.Close(); }
    }
}
