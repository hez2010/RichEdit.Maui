using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class LfTextProjectionTests
{
    [Fact]
    public void CrLfAcrossChunksHasExplicitInteriorAffinityAndExactNativeBoundaries()
    {
        var raw = new string('x', 2047) + "\r\na\rb\n";
        var projection = new LfTextProjection(TextSnapshot.FromText(raw));
        Assert.Equal(Normalize(raw), projection.Display.ToString());
        Assert.Equal(2047, projection.ToDisplay(2048, afterCrLf: false));
        Assert.Equal(2048, projection.ToDisplay(2048));
        Assert.Equal(2049, projection.ToSource(2048));
        Assert.Equal(new TextSpan(2047, 1), projection.ToDisplay(new TextSpan(2048, 1)));
        for (var position = 0; position <= projection.Display.Length; position++)
            Assert.Equal(position, projection.ToDisplay(projection.ToSource(position)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(43)]
    public void IncrementalProjectionMatchesFullNormalizationThroughMixedEndingEditsAndUndo(int seed)
    {
        var random = new Random(seed);
        var document = new TextDocument(string.Concat(Enumerable.Repeat("ab\r\ncd\ref\n", 600)));
        var projection = new LfTextProjection(document.CurrentSnapshot);
        var choices = new[] { "", "x", "\r", "\n", "\r\n", "日本\r\n", new string('z', 2049) };
        document.Changed += (_, change) =>
        {
            var oldDisplay = projection.Display;
            projection = projection.Apply(change, out var replacements);
            foreach (var replacement in replacements)
                oldDisplay = oldDisplay.Replace(replacement.Range, replacement.Text);
            Assert.Equal(projection.Display.ToString(), oldDisplay.ToString());
            Assert.Equal(Normalize(document.Text), projection.Display.ToString());
            for (var sample = 0; sample < 6; sample++)
            {
                var position = random.Next(projection.Display.Length + 1);
                var source = projection.ToSource(position);
                Assert.Equal(position, projection.ToDisplay(source));
                Assert.False(source > 0 && source < document.Length && document.CurrentSnapshot[source - 1] == '\r' && document.CurrentSnapshot[source] == '\n');
            }
        };
        for (var step = 0; step < 300; step++)
        {
            var start = random.Next(document.Length + 1);
            var length = random.Next(Math.Min(8, document.Length - start) + 1);
            document.Edit(edit => edit.Replace(new(start, length), choices[random.Next(choices.Length)]));
            if (step % 9 == 0 && document.CanUndo)
            {
                document.Undo();
                document.Redo();
            }
        }
    }

    [Fact]
    public void SplittingAndJoiningCrLfOnlyRewritesTheAffectedDisplaySlice()
    {
        var document = new TextDocument("first\r\nlast");
        var projection = new LfTextProjection(document.CurrentSnapshot);
        var change = document.Edit(edit => edit.Insert(6, "x"))!;
        projection = projection.Apply(change, out var edits);
        Assert.Equal("first\rx\nlast", document.Text);
        Assert.Equal("first\nx\nlast", projection.Display.ToString());
        Assert.Single(edits);
        Assert.Equal(new TextReplacement(new(6, 0), "x\n"), edits[0]);
        var remove = document.Edit(edit => edit.Delete(new(6, 1)))!;
        projection = projection.Apply(remove, out edits);
        Assert.Equal("first\r\nlast", document.Text);
        Assert.Equal("first\nlast", projection.Display.ToString());
        Assert.Single(edits);
        Assert.Equal(new TextReplacement(new(6, 2), ""), edits[0]);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
