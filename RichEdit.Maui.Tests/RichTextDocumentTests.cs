namespace RichEdit.Maui.Tests;

public sealed class RichTextDocumentTests
{
    private static readonly RichTextCharacterFormat Bold = new() { FontWeight = 700 };
    private static readonly RichTextCharacterFormat Italic = new() { Italic = true };

    [Fact]
    public void ConstructorNormalizesTextAndCompletesRunsAndParagraphs()
    {
        var centered = RichTextParagraphFormat.Default with
        {
            Alignment = RichTextAlignment.Center,
        };
        var document = new RichTextDocument(
            "a\r\nb\rc",
            [new RichTextRun(2, 1, Bold)],
            [new RichTextParagraph(2, centered)]);

        Assert.Equal("a\nb\nc", document.Text);
        Assert.Collection(
            document.Runs,
            run => Assert.Equal(new RichTextRun(0, 2, RichTextCharacterFormat.Default), run),
            run => Assert.Equal(new RichTextRun(2, 1, Bold), run),
            run => Assert.Equal(new RichTextRun(3, 2, RichTextCharacterFormat.Default), run));
        Assert.Equal([0, 2, 4], document.Paragraphs.Select(paragraph => paragraph.Start));
        Assert.Equal(centered, document.GetParagraphFormat(2));
    }

    [Fact]
    public void ApplyCharacterFormatSplitsAndThenCoalescesRuns()
    {
        var document = RichTextDocument.FromPlainText("abcdef")
            .ApplyCharacterFormat(2..4, _ => Bold);

        Assert.Collection(
            document.Runs,
            run => Assert.Equal(new RichTextRun(0, 2, RichTextCharacterFormat.Default), run),
            run => Assert.Equal(new RichTextRun(2, 2, Bold), run),
            run => Assert.Equal(new RichTextRun(4, 2, RichTextCharacterFormat.Default), run));

        document = document.ApplyCharacterFormat(2..4, _ => RichTextCharacterFormat.Default);
        Assert.Equal(
            [new RichTextRun(0, 6, RichTextCharacterFormat.Default)],
            document.Runs);
    }

    [Fact]
    public void TransparentBackgroundIsNormalizedToAColorReset()
    {
        var transparent = Microsoft.Maui.Graphics.Color.FromRgba(0, 0, 0, 0);
        var document = new RichTextDocument(
            "clear",
            runs:
            [
                new RichTextRun(
                    0,
                    5,
                    RichTextCharacterFormat.Default with { BackgroundColor = transparent }),
            ]);

        Assert.Null(document.GetCharacterFormat(0).BackgroundColor);
    }

    [Fact]
    public void ListPictureResourcesSurviveTextAndNativeSnapshotRemapping()
    {
        var picture = RichTextListPicture.FromBytes(
            "marker",
            "image/png",
            [0x89, 0x50, 0x4E, 0x47],
            10,
            10);
        var list = new RichTextListFormat
        {
            Id = 1,
            Kind = RichListKind.Bulleted,
            PictureId = picture.Id,
        };
        var document = new RichTextDocument(
            "item",
            paragraphs:
            [new RichTextParagraph(0, RichTextParagraphFormat.Default with { List = list })],
            listPictures: [picture]);

        var replaced = document.Replace(4..4, "!");
        var merged = replaced.MergeNativeSnapshot(
            replaced.Text,
            replaced.Runs,
            [new RichTextParagraph(
                0,
                RichTextParagraphFormat.Default with
                {
                    List = list with { PictureId = null },
                })],
            links: null,
            images: null,
            replaced.DefaultCharacterFormat,
            replaced.DefaultParagraphFormat,
            RichEditorHandler.MergeWindowsCharacterFormat,
            RichEditorHandler.MergeWindowsParagraphFormat);

        Assert.Same(picture, replaced.ListPictures[picture.Id]);
        Assert.Same(picture, merged.ListPictures[picture.Id]);
        Assert.Equal(picture.Id, merged.GetParagraphFormat(0).List?.PictureId);

        var switchedToNumbering = RichEditorHandler.MergeWindowsParagraphFormat(
            RichTextParagraphFormat.Default with
            {
                List = new RichTextListFormat
                {
                    Id = 1,
                    Kind = RichListKind.Numbered,
                },
            },
            replaced.GetParagraphFormat(0));
        Assert.Null(switchedToNumbering.List?.PictureId);
    }

    [Fact]
    public void ConstructorRejectsInvalidListPictureReferences()
    {
        var missingPictureList = RichTextParagraphFormat.Default with
        {
            List = new RichTextListFormat
            {
                Id = 1,
                Kind = RichListKind.Bulleted,
                PictureId = "missing",
            },
        };
        Assert.Throws<ArgumentException>(() => new RichTextDocument(
            "item",
            paragraphs: [new RichTextParagraph(0, missingPictureList)]));

        var numberedPictureList = missingPictureList with
        {
            List = missingPictureList.List! with { Kind = RichListKind.Numbered },
        };
        Assert.Throws<ArgumentException>(() => new RichTextDocument(
            "item",
            paragraphs: [new RichTextParagraph(0, numberedPictureList)]));
    }

    [Fact]
    public void ReplacePreservesAndRemapsSemanticRanges()
    {
        var document = new RichTextDocument(
            $"ab{RichTextDocument.ObjectReplacementCharacter}cdef",
            [
                new RichTextRun(0, 2, Bold),
                new RichTextRun(2, 5, Italic),
            ],
            links: [new RichTextLink(3, 4, "https://example.test", "tip")],
            fields: [new RichTextField(0, 2, "DATE")],
            images:
            [
                RichTextImage.FromBytes(
                    2,
                    "image/png",
                    [1, 2, 3],
                    10,
                    20),
            ]);

        var replaced = document.Replace(4..6, "XYZ", Bold);

        Assert.Equal($"ab{RichTextDocument.ObjectReplacementCharacter}cXYZf", replaced.Text);
        Assert.Equal(new RichTextField(0, 2, "DATE"), Assert.Single(replaced.Fields));
        Assert.Equal(2, Assert.Single(replaced.Images).Position);
        Assert.Equal(new RichTextLink(3, 5, "https://example.test", "tip"), Assert.Single(replaced.Links));
        Assert.Equal(Bold, replaced.GetCharacterFormat(4));
    }

    [Fact]
    public void NativeSnapshotCanRemapModelOwnedSemanticRanges()
    {
        const string originalText = "Field Link \uFFFC";
        var document = new RichTextDocument(
            originalText,
            links: [new RichTextLink(6, 4, "https://example.test", "tip")],
            fields: [new RichTextField(0, 5, "DATE")],
            images:
            [
                RichTextImage.FromBytes(
                    originalText.Length - 1,
                    "image/png",
                    [1, 2, 3],
                    10,
                    20),
            ]);

        var updatedText = $"X{originalText}";
        var merged = document.MergeNativeSnapshot(
            updatedText,
            [new RichTextRun(0, updatedText.Length, RichTextCharacterFormat.Default)],
            [new RichTextParagraph(0, RichTextParagraphFormat.Default)],
            links: null,
            images: null,
            RichTextCharacterFormat.Default,
            RichTextParagraphFormat.Default);

        Assert.Equal(7, Assert.Single(merged.Links).Start);
        Assert.Equal("tip", merged.Links[0].ToolTip);
        Assert.Equal(1, Assert.Single(merged.Fields).Start);
        Assert.Equal(originalText.Length, Assert.Single(merged.Images).Position);
    }

    [Fact]
    public void NativeSnapshotMergeSplitsRunsAtPreservedMetadataBoundaries()
    {
        var shadow = RichTextCharacterFormat.Default with { Shadow = true };
        var paragraphBackground = Microsoft.Maui.Graphics.Color.FromRgb(0x12, 0x34, 0x56);
        var document = new RichTextDocument(
            "ab\ncd",
            [
                new RichTextRun(0, 2, shadow),
                new RichTextRun(2, 3, RichTextCharacterFormat.Default),
            ],
            [
                new RichTextParagraph(
                    0,
                    RichTextParagraphFormat.Default with
                    {
                        BackgroundColor = paragraphBackground,
                    }),
                new RichTextParagraph(3, RichTextParagraphFormat.Default),
            ]);

        var merged = document.MergeNativeSnapshot(
            document.Text,
            [new RichTextRun(0, document.Text.Length, RichTextCharacterFormat.Default)],
            document.Paragraphs.Select(paragraph =>
                new RichTextParagraph(paragraph.Start, RichTextParagraphFormat.Default)),
            links: null,
            images: null,
            RichTextCharacterFormat.Default,
            RichTextParagraphFormat.Default,
            RichEditorHandler.MergeWindowsCharacterFormat,
            RichEditorHandler.MergeWindowsParagraphFormat);

        Assert.True(merged.GetCharacterFormat(0).Shadow);
        Assert.False(merged.GetCharacterFormat(3).Shadow);
        Assert.Equal(paragraphBackground, merged.GetParagraphFormat(0).BackgroundColor);
        Assert.Null(merged.GetParagraphFormat(3).BackgroundColor);
    }

    [Fact]
    public void WindowsReadbackPreservesOnlyPropertiesMissingFromTom()
    {
        var underlineColor = Microsoft.Maui.Graphics.Color.FromRgb(0x11, 0x22, 0x33);
        var strikeColor = Microsoft.Maui.Graphics.Color.FromRgb(0x44, 0x55, 0x66);
        var shadingColor = Microsoft.Maui.Graphics.Color.FromRgb(0x77, 0x88, 0x99);
        var previousCharacter = RichTextCharacterFormat.Default with
        {
            UnderlineColor = underlineColor,
            Strikethrough = RichTextStrikethroughStyle.Double,
            StrikethroughColor = strikeColor,
            HorizontalScale = 1.17,
            Shadow = true,
            LanguageTag = "ja-JP",
            Direction = RichTextDirection.RightToLeft,
            Kerning = RichTextFeatureMode.Automatic,
            Ligatures = RichTextFeatureMode.Enabled,
            Shading = 2500,
            ShadingForegroundColor = shadingColor,
            StyleName = "Emphasis",
        };
        var nativeCharacter = RichTextCharacterFormat.Default with
        {
            FontFamily = "Segoe UI",
            Strikethrough = RichTextStrikethroughStyle.Single,
            HorizontalScale = 1.125,
            Kerning = RichTextFeatureMode.Disabled,
        };

        var character = RichEditorHandler.MergeWindowsCharacterFormat(
            nativeCharacter,
            previousCharacter);

        Assert.Equal("Segoe UI", character.FontFamily);
        Assert.Equal(underlineColor, character.UnderlineColor);
        Assert.Equal(RichTextStrikethroughStyle.Double, character.Strikethrough);
        Assert.Equal(strikeColor, character.StrikethroughColor);
        Assert.Equal(1.17, character.HorizontalScale, 3);
        Assert.True(character.Shadow);
        Assert.Equal("ja-JP", character.LanguageTag);
        Assert.Equal(RichTextDirection.RightToLeft, character.Direction);
        Assert.Equal(RichTextFeatureMode.Automatic, character.Kerning);
        Assert.Equal(RichTextFeatureMode.Enabled, character.Ligatures);
        Assert.Equal(2500, character.Shading);
        Assert.Equal(shadingColor, character.ShadingForegroundColor);
        Assert.Equal("Emphasis", character.StyleName);

        var background = Microsoft.Maui.Graphics.Color.FromRgb(0xaa, 0xbb, 0xcc);
        var previousList = new RichTextListFormat
        {
            Id = 23,
            Kind = RichListKind.Numbered,
            NumberStyle = RichListNumberStyle.UpperRoman,
            StartAt = 4,
            Restart = true,
            Prefix = "(",
            Suffix = ").",
        };
        var previousParagraph = RichTextParagraphFormat.Default with
        {
            Alignment = RichTextAlignment.Distributed,
            Direction = RichTextDirection.Automatic,
            MinimumLineHeight = 12,
            MaximumLineHeight = 24,
            Hyphenation = true,
            BackgroundColor = background,
            Border = new RichTextBorder(
                RichTextBorderSides.Bottom,
                RichTextBorderStyle.Single,
                1,
                background),
            StyleName = "Body",
            List = previousList,
        };
        var nativeParagraph = RichTextParagraphFormat.Default with
        {
            Alignment = RichTextAlignment.Justified,
            Direction = RichTextDirection.LeftToRight,
            SpaceAfter = 6,
            List = new RichTextListFormat
            {
                Id = 1,
                Kind = RichListKind.Numbered,
                NumberStyle = RichListNumberStyle.UpperRoman,
                StartAt = 4,
                Suffix = ".",
            },
        };

        var paragraph = RichEditorHandler.MergeWindowsParagraphFormat(
            nativeParagraph,
            previousParagraph);

        Assert.Equal(RichTextAlignment.Distributed, paragraph.Alignment);
        Assert.Equal(RichTextDirection.Automatic, paragraph.Direction);
        Assert.Equal(6, paragraph.SpaceAfter);
        Assert.Equal(12, paragraph.MinimumLineHeight);
        Assert.Equal(24, paragraph.MaximumLineHeight);
        Assert.True(paragraph.Hyphenation);
        Assert.Equal(background, paragraph.BackgroundColor);
        Assert.Equal(previousParagraph.Border, paragraph.Border);
        Assert.Equal("Body", paragraph.StyleName);
        Assert.Equal(23, paragraph.List?.Id);
        Assert.True(paragraph.List?.Restart);
        Assert.Equal("(", paragraph.List?.Prefix);
        Assert.Equal(").", paragraph.List?.Suffix);
        Assert.Null(paragraph.List?.PictureId);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1e-100, false)]
    [InlineData(36, true)]
    [InlineData(double.MaxValue, false)]
    public void WindowsTabPositionsMustBePositiveFiniteFloats(double position, bool expected)
    {
        Assert.Equal(
            expected,
            RichEditorHandler.TryConvertWindowsTabPosition(position, out _));
    }

    [Fact]
    public void InsertImageCreatesExactlyOneObjectCharacter()
    {
        var image = RichTextImage.FromBytes(0, "image/png", [0x89, 0x50], 32, 24);
        var document = RichTextDocument.FromPlainText("ab").InsertImage(1, image);

        Assert.Equal($"a{RichTextDocument.ObjectReplacementCharacter}b", document.Text);
        var stored = Assert.Single(document.Images);
        Assert.Equal(1, stored.Position);
        Assert.True(stored.Data.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50 }));
    }

    [Fact]
    public void ConstructorRejectsOverlappingSemanticRanges()
    {
        Assert.Throws<ArgumentException>(() => new RichTextDocument(
            "abcd",
            links:
            [
                new RichTextLink(0, 3, "https://one.test"),
                new RichTextLink(2, 2, "https://two.test"),
            ]));
    }
}
