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
