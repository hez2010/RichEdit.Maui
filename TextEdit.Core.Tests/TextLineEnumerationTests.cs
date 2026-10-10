using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextLineEnumerationTests
{
    [Fact]
    public void StreamingLinesMatchIndexedLinesAcrossChunkBoundariesAndAllTerminators()
    {
        foreach (var value in new[] { "", "a", "\r", "\n", "\r\n", "\r\r\n\n", new string('x', 2047) + "\r\na\rb\n", new string('x', 1024 * 1024) })
        {
            var snapshot = TextSnapshot.FromText(value);
            Assert.Equal(Enumerable.Range(0, snapshot.LineCount).Select(snapshot.GetLine), snapshot.GetLines());
        }
        var random = new Random(16180);
        var text = TextSnapshot.FromText(string.Concat(Enumerable.Repeat("line\r\nnext\rlast\n", 1000)));
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var position = random.Next(text.Length + 1);
            text = text.Replace(new(position, random.Next(Math.Min(30, text.Length - position) + 1)), iteration % 2 == 0 ? "\r\n" : "");
            Assert.Equal(Enumerable.Range(0, text.LineCount).Select(text.GetLine), text.GetLines());
        }
    }
}
