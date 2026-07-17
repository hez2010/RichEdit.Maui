namespace RichEdit.Maui.Tests;

public sealed class RtfArchitectureTests
{
    [Fact]
    public void NumberedListRoundTripKeepsMarkersOutOfLogicalText()
    {
        var list = new RichTextListFormat
        {
            Id = 42,
            Kind = RichListKind.Numbered,
            NumberStyle = RichListNumberStyle.UpperRoman,
            StartAt = 4,
            Suffix = ")",
        };
        var document = new RichTextDocument(
            "First\nSecond",
            paragraphs:
            [
                new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = list }),
                new RichTextParagraph(6, RichTextParagraphFormat.Default with { List = list }),
            ]);

        var rtf = document.ToRtf();
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal("First\nSecond", parsed.Text);
        Assert.Equal(2, parsed.Paragraphs.Count);
        Assert.All(parsed.Paragraphs, paragraph => Assert.Equal(RichListKind.Numbered, paragraph.Format.List?.Kind));
        Assert.Equal(RichListNumberStyle.UpperRoman, parsed.Paragraphs[0].Format.List?.NumberStyle);
        Assert.Contains("IV)", rtf, StringComparison.Ordinal);
        Assert.Contains("V)", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void BulletListRoundTripKeepsCustomBulletAsMetadata()
    {
        var list = new RichTextListFormat
        {
            Id = 7,
            Kind = RichListKind.Bulleted,
            BulletText = "▪",
            Suffix = string.Empty,
        };
        var document = new RichTextDocument(
            "Alpha",
            paragraphs:
            [new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = list })]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal("Alpha", parsed.Text);
        Assert.Equal(RichListKind.Bulleted, parsed.Paragraphs[0].Format.List?.Kind);
        Assert.Equal("▪", parsed.Paragraphs[0].Format.List?.BulletText);
    }

    [Fact]
    public void RtfLineControlProducesSoftLineBreakNotParagraphBreak()
    {
        var parsed = RichTextDocument.FromRtf(@"{\rtf1\ansi one\line two\par three}");

        Assert.Equal($"one{RichTextDocument.SoftLineBreakCharacter}two\nthree", parsed.Text);
        Assert.Equal([0, 8], parsed.Paragraphs.Select(paragraph => paragraph.Start));
    }

    [Fact]
    public void BasicCharacterAndParagraphFormattingRoundTrips()
    {
        var defaultFormat = new RichTextCharacterFormat
        {
            FontFamily = "Arial",
            FontSize = 12,
        };
        var emphasized = defaultFormat with
        {
            FontWeight = 700,
            Italic = true,
            Underline = RichTextUnderlineStyle.Single,
            Script = RichTextScript.Superscript,
        };
        var centered = RichTextParagraphFormat.Default with
        {
            Alignment = RichTextAlignment.Center,
        };
        var document = new RichTextDocument(
            "plain rich",
            [
                new RichTextRun(0, 6, defaultFormat),
                new RichTextRun(6, 4, emphasized),
            ],
            [new RichTextParagraph(0, centered)],
            defaultCharacterFormat: defaultFormat);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal(document.Text, parsed.Text);
        Assert.Equal(RichTextAlignment.Center, parsed.GetParagraphFormat(0).Alignment);
        var actual = parsed.GetCharacterFormat(6);
        Assert.True(actual.Bold);
        Assert.True(actual.Italic);
        Assert.Equal(RichTextUnderlineStyle.Single, actual.Underline);
        Assert.Equal(RichTextScript.Superscript, actual.Script);
    }
}
