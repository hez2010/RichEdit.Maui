namespace RichEdit.Maui.Tests;

public sealed class RtfArchitectureTests
{
    [Theory]
    [InlineData(4, RichListNumberStyle.Arabic, "4")]
    [InlineData(4, RichListNumberStyle.UpperRoman, "IV")]
    [InlineData(14, RichListNumberStyle.LowerRoman, "xiv")]
    [InlineData(27, RichListNumberStyle.UpperLetter, "AA")]
    [InlineData(52, RichListNumberStyle.LowerLetter, "az")]
    public void SharedListNumberFormatterMatchesRtfMarkers(
        int number,
        RichListNumberStyle style,
        string expected)
    {
        Assert.Equal(expected, RichTextListFormatter.FormatNumber(number, style));
    }

    [Fact]
    public void SharedListMarkerFormatterKeepsPrefixAndSuffixOutOfText()
    {
        var list = new RichTextListFormat
        {
            Id = 1,
            Kind = RichListKind.Numbered,
            NumberStyle = RichListNumberStyle.UpperRoman,
            Prefix = "(",
            Suffix = ")",
        };

        Assert.Equal("(IV)", RichTextListFormatter.FormatMarker(list, 4));
    }

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
            BulletText = "→",
            Suffix = string.Empty,
        };
        var document = new RichTextDocument(
            "Alpha",
            paragraphs:
            [new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = list })]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal("Alpha", parsed.Text);
        Assert.Equal(RichListKind.Bulleted, parsed.Paragraphs[0].Format.List?.Kind);
        Assert.Equal("→", parsed.Paragraphs[0].Format.List?.BulletText);
    }

    [Fact]
    public void PictureBulletRoundTripUsesStandardListPictureTable()
    {
        var picture = RichTextListPicture.FromBytes(
            "round-marker",
            "image/png",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            12,
            12,
            "Round marker");
        var list = new RichTextListFormat
        {
            Id = 8,
            Kind = RichListKind.Bulleted,
            BulletText = "•",
            Suffix = string.Empty,
            PictureId = picture.Id,
        };
        var document = new RichTextDocument(
            "Alpha\nBeta",
            paragraphs:
            [
                new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = list }),
                new RichTextParagraph(6, RichTextParagraphFormat.Default with { List = list }),
            ],
            listPictures: [picture]);

        var rtf = document.ToRtf();
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Contains(@"{\*\listpicture", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\levelpicture0", rtf, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(rtf, @"{\*\shppict{\pict"));
        var parsedPicture = Assert.Single(parsed.ListPictures).Value;
        Assert.Equal("0", parsedPicture.Id);
        Assert.Equal(picture.MediaType, parsedPicture.MediaType);
        Assert.True(picture.Data.AsSpan().SequenceEqual(parsedPicture.Data.AsSpan()));
        Assert.Equal(picture.Width, parsedPicture.Width, 2);
        Assert.Equal(picture.Height, parsedPicture.Height, 2);
        Assert.Equal(picture.AlternativeText, parsedPicture.AlternativeText);
        Assert.All(
            parsed.Paragraphs,
            paragraph => Assert.Equal(parsedPicture.Id, paragraph.Format.List?.PictureId));
    }

    [Fact]
    public void NumberedListIgnoresMalformedPictureReference()
    {
        const string rtf = @"{\rtf1\ansi" +
            @"{\*\listtable{\*\listpicture{\*\shppict{\pict\pngblip 89504e47}}}" +
            @"{\list\listtemplateid1{\listlevel\levelnfc0\levelstartat1" +
            @"{\leveltext\'02\'00.;}{\levelnumbers\'01;}\levelpicture0}" +
            @"\listid1}}{\*\listoverridetable{\listoverride\listid1\ls1}}" +
            @"\pard\ls1\ilvl0{\listtext 1.\tab }Item}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Single(parsed.ListPictures);
        Assert.Equal(RichListKind.Numbered, parsed.GetParagraphFormat(0).List?.Kind);
        Assert.Null(parsed.GetParagraphFormat(0).List?.PictureId);
    }

    [Fact]
    public void MultilevelListRoundTripPreservesEachLevelDefinition()
    {
        var topLevel = new RichTextListFormat
        {
            Id = 9,
            Level = 0,
            Kind = RichListKind.Numbered,
            NumberStyle = RichListNumberStyle.Arabic,
            StartAt = 3,
            Prefix = "(",
            Suffix = ")",
        };
        var bulletLevel = new RichTextListFormat
        {
            Id = 9,
            Level = 1,
            Kind = RichListKind.Bulleted,
            BulletText = "▪",
            Suffix = string.Empty,
        };
        var letterLevel = new RichTextListFormat
        {
            Id = 9,
            Level = 2,
            Kind = RichListKind.Numbered,
            NumberStyle = RichListNumberStyle.LowerLetter,
            StartAt = 2,
            Prefix = "[",
            Suffix = "]",
        };
        var document = new RichTextDocument(
            "Top\nChild\nPeer\nAgain",
            paragraphs:
            [
                new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = topLevel }),
                new RichTextParagraph(4, RichTextParagraphFormat.Default with { List = bulletLevel }),
                new RichTextParagraph(10, RichTextParagraphFormat.Default with { List = letterLevel }),
                new RichTextParagraph(15, RichTextParagraphFormat.Default with { List = bulletLevel }),
            ]);

        var rtf = document.ToRtf();
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal(document.Text, parsed.Text);
        Assert.Contains(@"\listhybrid", rtf, StringComparison.Ordinal);
        Assert.Equal(9, rtf.Split(@"\listlevel", StringSplitOptions.None).Length - 1);
        Assert.Contains(@"\levelfollow0", rtf, StringComparison.Ordinal);
        Assert.Contains("(3)", rtf, StringComparison.Ordinal);
        Assert.Contains("[b]", rtf, StringComparison.Ordinal);

        Assert.Equal(topLevel with { Id = 1 }, parsed.Paragraphs[0].Format.List);
        Assert.Equal(bulletLevel with { Id = 1 }, parsed.Paragraphs[1].Format.List);
        Assert.Equal(letterLevel with { Id = 1 }, parsed.Paragraphs[2].Format.List);
        Assert.Equal(bulletLevel with { Id = 1 }, parsed.Paragraphs[3].Format.List);
    }

    [Fact]
    public void ListCountersAreIndependentPerLevelAndHonorExplicitRestart()
    {
        static RichTextListFormat List(int level, int startAt = 1, bool restart = false) => new()
        {
            Id = 5,
            Level = level,
            Kind = RichListKind.Numbered,
            StartAt = startAt,
            Restart = restart,
        };

        var document = new RichTextDocument(
            "A\nB\nC\nD\nE\nF",
            paragraphs:
            [
                new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = List(0) }),
                new RichTextParagraph(2, RichTextParagraphFormat.Default with { List = List(1, 5) }),
                new RichTextParagraph(4, RichTextParagraphFormat.Default with { List = List(1, 5) }),
                new RichTextParagraph(6, RichTextParagraphFormat.Default with { List = List(0) }),
                new RichTextParagraph(8, RichTextParagraphFormat.Default with { List = List(1, 5, restart: true) }),
                new RichTextParagraph(10, RichTextParagraphFormat.Default with { List = List(0) }),
            ]);

        var rtf = document.ToRtf();
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal(1, CountOccurrences(rtf, @"{\listtext 1.\tab }"));
        Assert.Equal(1, CountOccurrences(rtf, @"{\listtext 2.\tab }"));
        Assert.Equal(1, CountOccurrences(rtf, @"{\listtext 3.\tab }"));
        Assert.Equal(2, CountOccurrences(rtf, @"{\listtext 5.\tab }"));
        Assert.Equal(1, CountOccurrences(rtf, @"{\listtext 6.\tab }"));
        Assert.Contains(@"\listoverridecount9", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\listoverridestartat\levelstartat5", rtf, StringComparison.Ordinal);
        Assert.All(parsed.Paragraphs, paragraph => Assert.Equal(1, paragraph.Format.List?.Id));
        Assert.False(parsed.Paragraphs[1].Format.List?.Restart);
        Assert.False(parsed.Paragraphs[2].Format.List?.Restart);
        Assert.True(parsed.Paragraphs[4].Format.List?.Restart);
        Assert.Equal(5, parsed.Paragraphs[4].Format.List?.StartAt);
        Assert.False(parsed.Paragraphs[5].Format.List?.Restart);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }

    [Fact]
    public void LargeSingleParagraphImportAllocatesLinearly()
    {
        _ = RichTextDocument.FromRtf(@"{\rtf1\ansi warmup}");
        var expected = new string('x', 20_000);
        var rtf = @"{\rtf1\ansi " + expected + "}";

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = RichTextDocument.FromRtf(rtf);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, parsed.Text);
        Assert.True(
            allocated < 25_000_000,
            $"A 20,000-character import allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void RtfLineControlProducesSoftLineBreakNotParagraphBreak()
    {
        var parsed = RichTextDocument.FromRtf(@"{\rtf1\ansi one\line two\par three}");

        Assert.Equal($"one{RichTextDocument.SoftLineBreakCharacter}two\nthree", parsed.Text);
        Assert.Equal([0, 8], parsed.Paragraphs.Select(paragraph => paragraph.Start));
    }

    [Fact]
    public void SoftLineBreakSerializesAsThePortableLineControl()
    {
        var document = RichTextDocument.FromPlainText(
            $"one{RichTextDocument.SoftLineBreakCharacter}two");

        var rtf = document.ToRtf();

        Assert.Contains(@"\line ", rtf, StringComparison.Ordinal);
        Assert.Equal(document.Text, RichTextDocument.FromRtf(rtf).Text);
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
    public void NonpositiveTabStopsAreIgnoredOnImport()
    {
        var document = RichTextDocument.FromRtf(@"{\rtf1\ansi\tx0\tx-20\tx720 x}");

        var tab = Assert.Single(document.GetParagraphFormat(0).TabStops);
        Assert.Equal(36, tab.Position);
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

    [Fact]
    public void HyperlinkRoundTripPreservesDisplayTextTargetAndToolTip()
    {
        var linkFormat = RichTextCharacterFormat.Default with
        {
            Underline = RichTextUnderlineStyle.Single,
            ForegroundColor = Colors.Blue,
        };
        var document = new RichTextDocument(
            "OpenAI",
            [new RichTextRun(0, 6, linkFormat)],
            links: [new RichTextLink(0, 6, "https://openai.com/docs?q=rtf", "Open docs")]);

        var rtf = document.ToRtf();
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal(document.Text, parsed.Text);
        var link = Assert.Single(parsed.Links);
        Assert.Equal(document.Links[0], link);
        Assert.Empty(parsed.Fields);
        Assert.Equal(RichTextUnderlineStyle.Single, parsed.GetCharacterFormat(0).Underline);
        AssertColor(Colors.Blue, parsed.GetCharacterFormat(0).ForegroundColor);
        Assert.Contains("HYPERLINK", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralFieldRoundTripPreservesInstructionAndResult()
    {
        var document = new RichTextDocument(
            "2026-07-18",
            fields: [new RichTextField(0, 10, "DATE \\@ \"yyyy-MM-dd\"")]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal(document.Text, parsed.Text);
        Assert.Equal(document.Fields[0], Assert.Single(parsed.Fields));
        Assert.Empty(parsed.Links);
    }

    [Fact]
    public void EmptyFieldResultRoundTripsAtItsLogicalPosition()
    {
        var document = new RichTextDocument(
            "abc",
            fields: [new RichTextField(1, 0, "PAGE")]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal(document.Text, parsed.Text);
        Assert.Equal(document.Fields[0], Assert.Single(parsed.Fields));
    }

    [Fact]
    public void PartiallyOverlappingFieldAndLinkRoundTripWithoutLosingEitherRange()
    {
        var document = new RichTextDocument(
            "abcdefgh",
            links: [new RichTextLink(3, 5, "https://example.test")],
            fields: [new RichTextField(0, 5, "MERGEFIELD Name")]);

        var parsed = RichTextDocument.FromRtf(document.ToRtf());

        Assert.Equal(document.Text, parsed.Text);
        Assert.Equal(document.Fields[0], Assert.Single(parsed.Fields));
        Assert.Equal(document.Links[0], Assert.Single(parsed.Links));
    }

    [Fact]
    public void PngImageRoundTripPreservesOwnedBytesSizeCropAndCharacterFormat()
    {
        var imageFormat = RichTextCharacterFormat.Default with
        {
            BaselineOffset = 2,
        };
        var image = RichTextImage.FromBytes(
            1,
            "image/png",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A],
            32.5,
            24.25) with
        {
            Crop = new RichTextImageCrop(1, 2, 3, 4),
            AlternativeText = "Q1 {chart}",
            Rotation = 22.5,
        };
        var document = new RichTextDocument(
            $"a{RichTextDocument.ObjectReplacementCharacter}b",
            [
                new RichTextRun(0, 1, RichTextCharacterFormat.Default),
                new RichTextRun(1, 1, imageFormat),
                new RichTextRun(2, 1, RichTextCharacterFormat.Default),
            ],
            images: [image]);

        var rtf = document.ToRtf();
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal(document.Text, parsed.Text);
        var actual = Assert.Single(parsed.Images);
        Assert.Equal(image.Position, actual.Position);
        Assert.Equal(image.MediaType, actual.MediaType);
        Assert.True(image.Data.AsSpan().SequenceEqual(actual.Data.AsSpan()));
        Assert.Equal(image.Width, actual.Width, 2);
        Assert.Equal(image.Height, actual.Height, 2);
        Assert.Equal(image.Crop, actual.Crop);
        Assert.Equal(image.AlternativeText, actual.AlternativeText);
        Assert.Equal(image.Rotation, actual.Rotation);
        Assert.Equal(imageFormat.BaselineOffset, parsed.GetCharacterFormat(1).BaselineOffset);
        Assert.Contains(@"\pngblip", rtf, StringComparison.Ordinal);
        Assert.Contains("wzDescription", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\sn rotation", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeProjectionLeavesFieldsAndLinksAsPlainText()
    {
        const string text = "Field Link \uFFFC";
        var document = new RichTextDocument(
            text,
            links: [new RichTextLink(6, 4, "https://example.test")],
            fields: [new RichTextField(0, 5, "DATE")],
            images:
            [
                RichTextImage.FromBytes(
                    text.Length - 1,
                    "image/png",
                    [0x89, 0x50, 0x4E, 0x47],
                    16,
                    16),
            ]);

        var rtf = RtfCodec.SerializeForNativeProjection(document);
        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Contains(@"\pict", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\field", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain("HYPERLINK", rtf, StringComparison.Ordinal);
        Assert.Equal(document.Text, parsed.Text);
        Assert.Empty(parsed.Fields);
        Assert.Empty(parsed.Links);
        Assert.Single(parsed.Images);
    }

    [Fact]
    public void BinaryPictureDataIsImported()
    {
        var rtf = "{\\rtf1{\\pict\\jpegblip\\picwgoal200\\pichgoal100\\bin3 " +
            new string([(char)0xFF, (char)0xD8, (char)0xFF]) + "}}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal(RichTextDocument.ObjectReplacementCharacter.ToString(), parsed.Text);
        var image = Assert.Single(parsed.Images);
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.True(image.Data.AsSpan().SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }));
        Assert.Equal(10, image.Width);
        Assert.Equal(5, image.Height);
    }

    [Fact]
    public void PictureScaleIsAppliedToPixelDimensionsOnImport()
    {
        const string rtf = @"{\rtf1{\pict\pngblip\picw100\pich50" +
            @"\picscalex50\picscaley200 8950}}";

        var image = Assert.Single(RichTextDocument.FromRtf(rtf).Images);

        Assert.Equal(37.5, image.Width, 2);
        Assert.Equal(75, image.Height, 2);
    }

    [Fact]
    public void PreferredWordPictureWrapperIsImportedWithoutCompatibilityDuplicate()
    {
        const string rtf = @"{\rtf1{\*\shppict{\pict\pngblip 89504e47}}" +
            @"{\nonshppict{\pict\wmetafile8 0102}}}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal(RichTextDocument.ObjectReplacementCharacter.ToString(), parsed.Text);
        var image = Assert.Single(parsed.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.True(image.Data.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Fact]
    public void OleObjectImportsItsResultAndSkipsPayload()
    {
        const string rtf = @"{\rtf1\ansi before {\object\objemb" +
            @"{\*\objclass Package}{\*\objdata 010203}" +
            @"{\result Result text}} after}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal("before Result text after", parsed.Text);
        Assert.DoesNotContain("010203", parsed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OleObjectWithoutAResultUsesPortablePlaceholder()
    {
        const string rtf = @"{\rtf1\ansi a{\object\objemb" +
            @"{\*\objclass Package}{\*\objdata 010203}}b}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal("a[Embedded object]b", parsed.Text);
    }

    [Fact]
    public void AttachmentPlaceholderIsFlattenedToReadableText()
    {
        var parsed = RichTextDocument.FromRtf(@"{\rtf1\ansi a\objattph b}");

        Assert.Equal("a[Attachment]b", parsed.Text);
    }

    [Fact]
    public void ShapeTextIsFlattenedAndShapePropertiesAreIgnored()
    {
        const string rtf = @"{\rtf1\ansi a{\shp" +
            @"{\*\shpinst{\sp{\sn shapeType}{\sv 202}}}" +
            @"{\shptxt Box text}}b}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal("aBox textb", parsed.Text);
        Assert.Empty(parsed.Images);
    }

    [Fact]
    public void FloatingShapePictureIsFlattenedInline()
    {
        const string rtf = @"{\rtf1\ansi a{\shp\shpleft100\shptop200\shpwr3" +
            @"{\*\shpinst{\sp{\sn pib}{\sv{\pict\pngblip 89504e47}}}}}b}";

        var parsed = RichTextDocument.FromRtf(rtf);

        Assert.Equal($"a{RichTextDocument.ObjectReplacementCharacter}b", parsed.Text);
        var image = Assert.Single(parsed.Images);
        Assert.Equal(1, image.Position);
        Assert.Equal("image/png", image.MediaType);
        Assert.True(image.Data.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
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
