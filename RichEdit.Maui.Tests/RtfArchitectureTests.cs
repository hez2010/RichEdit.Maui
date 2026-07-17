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

    [Theory]
    [InlineData(RichTextUnderlineStyle.Single)]
    [InlineData(RichTextUnderlineStyle.Words)]
    [InlineData(RichTextUnderlineStyle.Double)]
    [InlineData(RichTextUnderlineStyle.Dotted)]
    [InlineData(RichTextUnderlineStyle.Dash)]
    [InlineData(RichTextUnderlineStyle.DashDot)]
    [InlineData(RichTextUnderlineStyle.DashDotDot)]
    [InlineData(RichTextUnderlineStyle.Wave)]
    [InlineData(RichTextUnderlineStyle.Thick)]
    [InlineData(RichTextUnderlineStyle.DoubleWave)]
    [InlineData(RichTextUnderlineStyle.HeavyWave)]
    [InlineData(RichTextUnderlineStyle.LongDash)]
    public void UnderlineStylesRoundTrip(RichTextUnderlineStyle underline)
    {
        var format = RichTextCharacterFormat.Default with { Underline = underline };
        var document = new RichTextDocument(
            "x",
            [new RichTextRun(0, 1, format)]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal(underline, parsed.GetCharacterFormat(0).Underline);
    }

    [Fact]
    public void AdvancedCharacterFormattingRoundTrips()
    {
        var format = new RichTextCharacterFormat
        {
            FontFamily = "Georgia",
            FontSize = 15.5,
            FontWeight = 700,
            Italic = true,
            Underline = RichTextUnderlineStyle.DoubleWave,
            UnderlineColor = Color.FromRgb(0x12, 0x34, 0x56),
            Strikethrough = RichTextStrikethroughStyle.Double,
            ForegroundColor = Color.FromRgb(0x78, 0x56, 0x34),
            BackgroundColor = Color.FromRgb(0xFE, 0xDC, 0xBA),
            Script = RichTextScript.Subscript,
            BaselineOffset = 1.5,
            CharacterSpacing = 0.75,
            HorizontalScale = 1.25,
            SmallCaps = true,
            AllCaps = true,
            Outline = true,
            Shadow = true,
            Hidden = true,
            LanguageTag = "ja-JP",
            Direction = RichTextDirection.RightToLeft,
            Kerning = RichTextFeatureMode.Enabled,
            Shading = 2500,
            ShadingForegroundColor = Color.FromRgb(0x22, 0x44, 0x66),
            ShadingBackgroundColor = Color.FromRgb(0xEE, 0xCC, 0xAA),
        };
        var document = new RichTextDocument(
            "x",
            [new RichTextRun(0, 1, format)]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf()).GetCharacterFormat(0);

        Assert.Equal(format.FontFamily, parsed.FontFamily);
        Assert.Equal(format.FontSize, parsed.FontSize);
        Assert.True(parsed.Bold);
        Assert.True(parsed.Italic);
        Assert.Equal(format.Underline, parsed.Underline);
        AssertColor(format.UnderlineColor, parsed.UnderlineColor);
        Assert.Equal(format.Strikethrough, parsed.Strikethrough);
        AssertColor(format.ForegroundColor, parsed.ForegroundColor);
        AssertColor(format.BackgroundColor, parsed.BackgroundColor);
        Assert.Equal(format.Script, parsed.Script);
        Assert.Equal(format.BaselineOffset, parsed.BaselineOffset);
        Assert.Equal(format.CharacterSpacing, parsed.CharacterSpacing, 3);
        Assert.Equal(format.HorizontalScale, parsed.HorizontalScale, 2);
        Assert.True(parsed.SmallCaps);
        Assert.True(parsed.AllCaps);
        Assert.True(parsed.Outline);
        Assert.True(parsed.Shadow);
        Assert.True(parsed.Hidden);
        Assert.Equal(format.LanguageTag, parsed.LanguageTag, ignoreCase: true);
        Assert.Equal(format.Direction, parsed.Direction);
        Assert.Equal(format.Kerning, parsed.Kerning);
        Assert.Equal(format.Shading, parsed.Shading);
        AssertColor(format.ShadingForegroundColor, parsed.ShadingForegroundColor);
        AssertColor(format.ShadingBackgroundColor, parsed.ShadingBackgroundColor);
    }

    [Theory]
    [InlineData(RichTextLineSpacingRule.Single, 0)]
    [InlineData(RichTextLineSpacingRule.OneAndHalf, 0)]
    [InlineData(RichTextLineSpacingRule.Double, 0)]
    [InlineData(RichTextLineSpacingRule.AtLeast, 18)]
    [InlineData(RichTextLineSpacingRule.Exactly, 18)]
    [InlineData(RichTextLineSpacingRule.Multiple, 1.75)]
    public void LineSpacingRulesRoundTrip(RichTextLineSpacingRule rule, double spacing)
    {
        var format = RichTextParagraphFormat.Default with
        {
            LineSpacingRule = rule,
            LineSpacing = spacing,
        };
        var document = new RichTextDocument(
            "x",
            paragraphs: [new RichTextParagraph(0, format)]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf()).GetParagraphFormat(0);

        Assert.Equal(rule, parsed.LineSpacingRule);
        Assert.Equal(spacing, parsed.LineSpacing, 3);
    }

    [Fact]
    public void AdvancedParagraphFormattingRoundTrips()
    {
        var format = new RichTextParagraphFormat
        {
            Alignment = RichTextAlignment.Distributed,
            Direction = RichTextDirection.RightToLeft,
            LeadingIndent = 12.5,
            TrailingIndent = 4,
            FirstLineIndent = -6,
            SpaceBefore = 3,
            SpaceAfter = 5,
            LineSpacingRule = RichTextLineSpacingRule.Exactly,
            LineSpacing = 18,
            TabStops =
            [
                new RichTextTabStop(36, RichTextTabAlignment.Left, RichTextTabLeader.Dots),
                new RichTextTabStop(72, RichTextTabAlignment.Center, RichTextTabLeader.Hyphens),
                new RichTextTabStop(108, RichTextTabAlignment.Right, RichTextTabLeader.Underline),
                new RichTextTabStop(144, RichTextTabAlignment.Decimal, RichTextTabLeader.Equals),
            ],
            Hyphenation = true,
            Shading = 3500,
            ShadingForegroundColor = Color.FromRgb(0x20, 0x40, 0x60),
            ShadingBackgroundColor = Color.FromRgb(0xE0, 0xC0, 0xA0),
            Border = new RichTextBorder(
                RichTextBorderSides.Top | RichTextBorderSides.Bottom,
                RichTextBorderStyle.Dashed,
                1.5,
                Color.FromRgb(0x11, 0x33, 0x55)),
        };
        var document = new RichTextDocument(
            "x",
            paragraphs: [new RichTextParagraph(0, format)]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf()).GetParagraphFormat(0);

        Assert.Equal(format.Alignment, parsed.Alignment);
        Assert.Equal(format.Direction, parsed.Direction);
        Assert.Equal(format.LeadingIndent, parsed.LeadingIndent, 3);
        Assert.Equal(format.TrailingIndent, parsed.TrailingIndent, 3);
        Assert.Equal(format.FirstLineIndent, parsed.FirstLineIndent, 3);
        Assert.Equal(format.SpaceBefore, parsed.SpaceBefore, 3);
        Assert.Equal(format.SpaceAfter, parsed.SpaceAfter, 3);
        Assert.Equal(format.LineSpacingRule, parsed.LineSpacingRule);
        Assert.Equal(format.LineSpacing, parsed.LineSpacing, 3);
        Assert.True(format.TabStops.AsSpan().SequenceEqual(parsed.TabStops.AsSpan()));
        Assert.True(parsed.Hyphenation);
        Assert.Equal(format.Shading, parsed.Shading);
        AssertColor(format.ShadingForegroundColor, parsed.ShadingForegroundColor);
        AssertColor(format.ShadingBackgroundColor, parsed.ShadingBackgroundColor);
        Assert.Equal(format.Border?.Sides, parsed.Border?.Sides);
        Assert.Equal(format.Border?.Style, parsed.Border?.Style);
        Assert.NotNull(parsed.Border);
        Assert.Equal(format.Border!.Width, parsed.Border.Width, 3);
        AssertColor(format.Border?.Color, parsed.Border?.Color);
    }

    [Fact]
    public void SolidParagraphBackgroundRoundTripsAsShading()
    {
        var background = Color.FromRgb(0xFA, 0xE0, 0x90);
        var format = RichTextParagraphFormat.Default with { BackgroundColor = background };
        var document = new RichTextDocument(
            "x",
            paragraphs: [new RichTextParagraph(0, format)]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf()).GetParagraphFormat(0);

        AssertColor(background, parsed.BackgroundColor);
    }

    private static void AssertColor(Color? expected, Color? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected, actual);
            return;
        }

        Assert.Equal(expected.Red, actual.Red, 3);
        Assert.Equal(expected.Green, actual.Green, 3);
        Assert.Equal(expected.Blue, actual.Blue, 3);
        Assert.Equal(expected.Alpha, actual.Alpha, 3);
    }
}
