using RichEdit.Core;

namespace RichEdit.Maui;

public sealed partial class RichTextDocumentSnapshot
{
    // A render description only. RichDocument owns authored nodes, history,
    // selections, resources, and persistence; this cache never authors a second root.
    internal static RichTextDocumentSnapshot CreateStructuredProjection(RichTextProjection projection)
    {
        var snapshot = projection.Snapshot;
        var runs = new List<RichTextRun>();
        var paragraphs = new List<RichTextParagraph>();
        var links = new List<RichTextLink>();
        var images = new List<RichTextImage>();
        foreach (var projected in projection.Paragraphs)
        {
            var paragraph = snapshot.Get<RichParagraph>(projected.Paragraph);
            // This flat description addresses shared presentation APIs. Cell
            // paragraphs retain their own formats in the structured/native map;
            // a tab in this explicit projection is not a paragraph terminator.
            if (projected.Range.Start == 0 || projection.Text.GetText(new(projected.Range.Start - 1, 1)) == "\n")
                paragraphs.Add(new(projected.Range.Start, ToNative(snapshot.ResolveParagraph(paragraph.Id))));
            var position = projected.Range.Start;
            foreach (var id in paragraph.Inlines)
            {
                var inline = snapshot.Get<RichInline>(id);
                var length = RichDocumentSnapshot.InlineLength(inline);
                if (length > 0) runs.Add(new(position, length, ToNative(snapshot.ResolveCharacter(id))));
                if (inline.Link is { } link && length > 0) links.Add(new(position, length, link.Target, link.ToolTip));
                if (inline is RichImage image)
                {
                    var asset = snapshot.Assets[image.Asset];
                    images.Add(new()
                    {
                        Position = position, Data = asset.Data, Width = image.Width.Points, Height = image.Height.Points,
                        AlternativeText = image.AlternativeText,
                        MediaType = asset.Kind switch { RichImageKind.Png => "image/png", RichImageKind.Jpeg => "image/jpeg",
                            RichImageKind.Dib => "image/bmp", RichImageKind.Wmf => "image/wmf", _ => "image/emf" },
                    });
                }
                position += length;
            }
        }
        return new(projection.Text.ToString(), runs, paragraphs, links, images: images,
            defaultCharacterFormat: ToNative(snapshot.DefaultCharacter.Apply(new())),
            defaultParagraphFormat: ToNative(snapshot.DefaultParagraph.Apply(new())),
            version: snapshot.Revision.IsValid ? snapshot.Revision.Version : 0, textBuffer: projection.Text) { Revision = snapshot.Revision };
    }

    internal static RichTextCharacterFormat ToNative(ResolvedCharacterStyle value) => new()
    {
        FontFamily = value.FontFamily, FontSize = value.FontSize?.Points, FontWeight = value.Bold ? 700 : 400, Italic = value.Italic,
        Underline = value.Underline switch { RichUnderline.None => RichTextUnderlineStyle.None, RichUnderline.Double => RichTextUnderlineStyle.Double,
            RichUnderline.Dotted => RichTextUnderlineStyle.Dotted, RichUnderline.Dashed => RichTextUnderlineStyle.Dash,
            RichUnderline.Wave => RichTextUnderlineStyle.Wave, RichUnderline.Words => RichTextUnderlineStyle.Words, _ => RichTextUnderlineStyle.Single },
        UnderlineColor = ToColor(value.UnderlineColor), Strikethrough = value.Strike switch { RichStrike.Single => RichTextStrikethroughStyle.Single,
            RichStrike.Double => RichTextStrikethroughStyle.Double, _ => RichTextStrikethroughStyle.None },
        ForegroundColor = ToColor(value.Foreground), BackgroundColor = ToColor(value.Background),
        Script = value.Script switch { RichScript.Subscript => RichTextScript.Subscript, RichScript.Superscript => RichTextScript.Superscript, _ => RichTextScript.Normal },
        CharacterSpacing = value.CharacterSpacing.Points, SmallCaps = value.SmallCaps, AllCaps = value.AllCaps,
        Outline = value.Outline, Shadow = value.Shadow, Hidden = value.Hidden, LanguageTag = value.Language,
        Direction = ToDirection(value.Direction),
    };

    internal static RichTextParagraphFormat ToNative(ResolvedParagraphStyle value) => new()
    {
        Alignment = value.Alignment switch { RichAlignment.Center => RichTextAlignment.Center, RichAlignment.Right => RichTextAlignment.Right,
            RichAlignment.Justify => RichTextAlignment.Justified, _ => RichTextAlignment.Left },
        Direction = ToDirection(value.Direction), LeadingIndent = value.LeadingIndent.Points, TrailingIndent = value.TrailingIndent.Points,
        FirstLineIndent = value.FirstLineIndent.Points, SpaceBefore = value.SpaceBefore.Points, SpaceAfter = value.SpaceAfter.Points,
        BackgroundColor = ToColor(value.Background),
    };
    private static Color? ToColor(RichColor? value) => value is { } color ? Color.FromRgb(color.Red, color.Green, color.Blue) : null;
    private static RichTextDirection ToDirection(RichDirection value) => value switch
    { RichDirection.LeftToRight => RichTextDirection.LeftToRight, RichDirection.RightToLeft => RichTextDirection.RightToLeft, _ => RichTextDirection.Automatic };
}
