using Xunit;

namespace RichEdit.Core.Tests;

public class RichStyleTests
{
    [Fact]
    public void CascadeSeparatesExplicitNeutralValuesFromInheritance()
    {
        var sheet = new RichStyleSheet([
            new(new(1), "Heading", RichStyleKind.Paragraph)
            {
                Character = new() { Bold = true, FontSize = RichFontSize.FromPoints(18), Underline = RichUnderline.Single },
                Paragraph = new() { HeadingLevel = 1, SpaceAfter = RichLength.FromPoints(6) },
            },
            new(new(2), "Child heading", RichStyleKind.Paragraph)
            { BasedOn = new(1), Character = new() { Bold = false }, Paragraph = new() { HeadingLevel = 2 } },
            new(new(3), "Emphasis", RichStyleKind.Character)
            { Character = new() { Italic = true, Underline = StyleValue<RichUnderline>.Initial } },
        ]);
        var result = sheet.ResolveCharacter(new(2), new(3), defaults: new() { FontFamily = "Example", Foreground = new RichColor(1, 2, 3) });
        Assert.False(result.Bold);
        Assert.True(result.Italic);
        Assert.Equal(RichUnderline.None, result.Underline);
        Assert.Equal("Example", result.FontFamily);
        Assert.Equal(18, result.FontSize!.Value.Points);
        Assert.Equal(new RichColor(1, 2, 3), result.Foreground);
        var reset = sheet.ResolveCharacter(new(1), direct: new() { Bold = StyleValue<bool>.Initial, Foreground = StyleValue<RichColor?>.Initial },
            defaults: new() { Foreground = new RichColor(255, 0, 0) });
        Assert.False(reset.Bold);
        Assert.Null(reset.Foreground);
        var paragraph = sheet.ResolveParagraph(new(2));
        Assert.Equal(2, paragraph.HeadingLevel);
        Assert.Equal(6, paragraph.SpaceAfter.Points);
    }

    [Fact]
    public void StyleGraphRejectsDanglingReferencesCyclesAndKindMismatches()
    {
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([new(new(1), "Missing", RichStyleKind.Character) { BasedOn = new(2) }]));
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([
            new(new(1), "A", RichStyleKind.Character) { BasedOn = new(2) },
            new(new(2), "B", RichStyleKind.Character) { BasedOn = new(1) }]));
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([
            new(new(1), "A", RichStyleKind.Character),
            new(new(2), "B", RichStyleKind.Paragraph) { BasedOn = new(1) }]));
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([
            new(new(1), "A", RichStyleKind.Character),
            new(new(2), "B", RichStyleKind.Paragraph) { NextStyle = new(1) }]));
    }

    [Fact]
    public void ValuesAreValidatedBeforeAStyleGraphCanBeAdopted()
    {
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([new(new(1), "Invalid", RichStyleKind.Character)
        { Character = new() { FontSize = default(RichFontSize) } }]));
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([new(new(1), "Invalid", RichStyleKind.Paragraph)
        { Paragraph = new() { HeadingLevel = 10 } }]));
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([new(new(1), "Invalid", RichStyleKind.Character)
        { Paragraph = new() { Alignment = RichAlignment.Center } }]));
        Assert.Throws<ArgumentException>(() => new RichStyleSheet([new(new(1), "Invalid", RichStyleKind.Paragraph)
        { Character = new() { Underline = (RichUnderline)99 } }]));
    }

    [Fact]
    public void LongAncestryResolvesWithoutRecursionAndImmutableSheetsStayIndependent()
    {
        var definitions = Enumerable.Range(1, 10_000).Select(index => new RichStyleDefinition(new(index), $"Style {index}", RichStyleKind.Character)
        { BasedOn = index == 1 ? null : new(index - 1), Character = index == 1 ? new() { Bold = true } : new() }).ToArray();
        var before = new RichStyleSheet(definitions);
        Assert.True(before.ResolveCharacter(characterStyle: new(10_000)).Bold);
        definitions[0] = definitions[0] with { Character = new() { Bold = false } };
        var after = new RichStyleSheet(definitions);
        Assert.False(after.ResolveCharacter(characterStyle: new(10_000)).Bold);
        Assert.True(before.ResolveCharacter(characterStyle: new(10_000)).Bold);
        Parallel.For(1, 100, index => Assert.True(before.ResolveCharacter(characterStyle: new(index)).Bold));
    }
}
