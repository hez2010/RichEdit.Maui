using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public class TextSnapshotTests
{
    [Fact]
    public void ChunkTraversalKeepsOrderAndBoundedAllocationInBothDirections()
    {
        var text = string.Concat(Enumerable.Repeat("a\r\n日本😀", 200_000));
        var snapshot = TextSnapshot.FromText(text).Replace(new(2040, 20), "replaced");
        var range = new TextSpan(13, snapshot.Length - 29);
        var expected = snapshot.GetText(range);
        Assert.Equal(expected, string.Concat(snapshot.GetChunksReverse(range).Reverse().Select(chunk => chunk.ToString())));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        foreach (var chunk in snapshot.GetChunks(range)) count += chunk.Length;
        foreach (var chunk in snapshot.GetChunksReverse(range)) count -= chunk.Length;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(0, count);
        Assert.True(allocated < 16 * 1024, $"Chunk traversal allocated {allocated} bytes.");
        Assert.Empty(snapshot.GetChunksReverse(new(snapshot.Length, 0)));
    }

    [Fact]
    public void ConcatenationSharesRootsAndCountsCrLfAcrossTheirBoundary()
    {
        var first = new TextDocument(new string('a', 100_000) + "\r").CurrentSnapshot;
        var second = new TextDocument("\nb\rnext").CurrentSnapshot;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var joined = first.Concat(second);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 32_000);
        Assert.False(joined.Revision.IsValid);
        Assert.Equal(3, joined.LineCount);
        Assert.Equal(new TextLine(new(0, 100_000), 2), joined.GetLine(0));
        Assert.Equal("b", joined.GetText(joined.GetLine(1).Span));
        Assert.Equal(100_001, first.Length);
        Assert.Equal("\nb\rnext", second.ToString());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(51)]
    [InlineData(129)]
    public void RandomEditsAndRetainedSnapshotsMatchAStringReference(int seed)
    {
        var random = new Random(seed);
        var reference = string.Concat(Enumerable.Repeat("abc\r\n日本\n😀\r", 600));
        var snapshot = TextSnapshot.FromText(reference);
        var retained = new List<(TextSnapshot Snapshot, string Text)>();
        var insertions = new[] { "", "x", "\n", "\r", "\r\n", "abc\r\nDEF", "😀", "e\u0301", new string('z', 3000) };
        for (var step = 0; step < 600; step++)
        {
            if (step % 100 == 0)
                retained.Add((snapshot, reference));
            var start = random.Next(reference.Length + 1);
            var count = random.Next(Math.Min(40, reference.Length - start) + 1);
            var inserted = insertions[random.Next(insertions.Length)];
            snapshot = snapshot.Replace(new(start, count), inserted);
            reference = reference.Remove(start, count).Insert(start, inserted);
            Assert.Equal(reference.Length, snapshot.Length);
            Assert.Equal(reference, snapshot.ToString());
            var lines = ReferenceLines(reference);
            Assert.Equal(lines.Count, snapshot.LineCount);
            for (var sample = 0; sample < 8; sample++)
            {
                var line = random.Next(lines.Count);
                Assert.Equal(lines[line], snapshot.GetLine(line));
                var offset = random.Next(reference.Length + 1);
                var position = snapshot.GetPosition(offset);
                Assert.Equal(offset, snapshot.GetOffset(position));
                Assert.True(lines[position.Line].Span.Start <= offset);
                if (position.Line + 1 < lines.Count)
                    Assert.True(offset < lines[position.Line + 1].Span.Start);
            }
            var sliceStart = random.Next(reference.Length + 1);
            var sliceLength = random.Next(Math.Min(reference.Length - sliceStart, 100) + 1);
            Assert.Equal(reference.Substring(sliceStart, sliceLength), snapshot.Slice(new(sliceStart, sliceLength)).ToString());
        }
        foreach (var item in retained)
            Assert.Equal(item.Text, item.Snapshot.ToString());
    }

    [Fact]
    public void CrLfAcrossChunkBoundariesHasExactLineAndOffsetMapping()
    {
        var text = new string('x', 2047) + "\r\nz\ry\n";
        var snapshot = TextSnapshot.FromText(text);
        Assert.Equal(4, snapshot.LineCount);
        Assert.Equal(new TextLine(new(0, 2047), 2), snapshot.GetLine(0));
        Assert.Equal(new TextPosition(0, 2048), snapshot.GetPosition(2048));
        Assert.Equal(new TextPosition(1, 0), snapshot.GetPosition(2049));
        Assert.Equal(2048, snapshot.GetOffset(new(0, 2048)));
        var splitPair = snapshot.Replace(new(2048, 0), "q");
        Assert.Equal(5, splitPair.LineCount);
        Assert.Equal(new TextLine(new(2048, 1), 1), splitPair.GetLine(1));
        var lf = snapshot.Replace(new(2047, 1), "");
        Assert.Equal(4, lf.LineCount);
        Assert.Equal(1, lf.GetLine(0).LineBreakLength);
        Assert.Equal(text, snapshot.ToString());
    }

    [Fact]
    public void EmptyAndTrailingLinesHaveBoundedPositions()
    {
        Assert.Equal(1, TextSnapshot.Empty.LineCount);
        Assert.Equal(new TextLine(new(0, 0), 0), TextSnapshot.Empty.GetLine(0));
        foreach (var text in new[] { "", "a", "\r", "\n", "\r\n", "\n\r\n\r", "a\r\nb\r\nc\n" })
        {
            var snapshot = TextSnapshot.FromText(text);
            Assert.Equal(ReferenceLines(text), Enumerable.Range(0, snapshot.LineCount).Select(snapshot.GetLine).ToList());
            for (var offset = 0; offset <= snapshot.Length; offset++)
                Assert.Equal(offset, snapshot.GetOffset(snapshot.GetPosition(offset)));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSnapshot.Empty.GetLine(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSnapshot.Empty.GetPosition(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSnapshot.Empty.Replace(new(0, 1), ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSnapshot.FromText("a\n").GetOffset(new(0, 2)));
    }

    [Fact]
    public void LocalEditsShareUntouchedChunksAndAvoidFullTextCopies()
    {
        var snapshot = TextSnapshot.FromText(new string('a', 1024 * 1024));
        var original = snapshot;
        var untouched = snapshot.GetChunks(new(0, snapshot.Length)).ElementAt(100);
        _ = snapshot.Replace(new(0, 0), "x");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var history = new List<TextSnapshot>();
        for (var i = 0; i < 100; i++)
        {
            history.Add(snapshot);
            snapshot = snapshot.Replace(new(0, 0), "x");
        }
        var delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(delta < 8 * 1024 * 1024, $"Local edits allocated {delta:N0} bytes.");
        Assert.Contains(untouched, snapshot.GetChunks(new(0, snapshot.Length)));
        Assert.Equal(1024 * 1024, original.Length);
        Assert.Equal('a', original[0]);
        Assert.Equal(1024 * 1024 + 100, snapshot.Length);
        GC.KeepAlive(history);
    }

    [Fact]
    public void NoOpAndStreamingPreserveExactSource()
    {
        var snapshot = TextSnapshot.FromText("abc\r\n日本\r😀\n");
        Assert.Same(snapshot, snapshot.Replace(new(3, 2), "\r\n"));
        Assert.Same(snapshot, snapshot.Replace(new(0, 0), ""));
        using var writer = new StringWriter();
        snapshot.WriteTo(writer);
        Assert.Equal(snapshot.ToString(), writer.ToString());
        Assert.Equal("日本", snapshot.GetText(new(5, 2)));
    }

    private static List<TextLine> ReferenceLines(string text)
    {
        var result = new List<TextLine>();
        var start = 0;
        for (var position = 0; position < text.Length; position++)
            if (text[position] is '\r' or '\n')
            {
                var size = text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
                result.Add(new(new(start, position - start), size));
                position += size - 1;
                start = position + 1;
            }
        result.Add(new(new(start, text.Length - start), 0));
        return result;
    }
}
