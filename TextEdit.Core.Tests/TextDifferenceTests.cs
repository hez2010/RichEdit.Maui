using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextDifferenceTests
{
    [Fact]
    public void SharedTenMiBSnapshotsCompareLocalEditsWithoutScanningUnchangedLeaves()
    {
        var before = TextSnapshot.FromText(new string('x', 10 * 1024 * 1024));
        foreach (var position in new[] { 0, before.Length / 2, before.Length })
        {
            var after = before.Replace(new(position, 0), "new\r\ntext");
            var difference = before.FindDifference(after, out var visited)!.Value;
            Assert.Equal(new(position, 0), difference.Before);
            Assert.Equal(new(position, 9), difference.After);
            Assert.InRange(visited, 1, 256);
            Assert.Equal("new\r\ntext", after.GetText(difference.After));
            Assert.Null(after.FindDifference(after.WithRevision(default), out _));
        }
    }

    [Fact]
    public void ExactDifferencesMatchFlatStringsAcrossRotationsAndIndependentRoots()
    {
        var random = new Random(27182);
        var current = TextSnapshot.FromText(string.Concat(Enumerable.Range(0, 3000).Select(index => $"{index}\r\n")));
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var start = random.Next(current.Length + 1);
            var length = random.Next(Math.Min(100, current.Length - start) + 1);
            var value = new string((char)('a' + iteration % 26), random.Next(70));
            var next = current.Replace(new(start, length), value);
            var first = current.ToString(); var second = next.ToString();
            var prefix = first.AsSpan().CommonPrefixLength(second);
            var suffix = 0;
            while (suffix < Math.Min(first.Length, second.Length) - prefix && first[^(suffix + 1)] == second[^(suffix + 1)]) suffix++;
            foreach (var candidate in new[] { next, TextSnapshot.FromText(second) })
            {
                var result = current.FindDifference(candidate, out _);
                if (first == second) Assert.Null(result);
                else
                {
                    Assert.Equal(new(prefix, first.Length - prefix - suffix), result!.Value.Before);
                    Assert.Equal(new(prefix, second.Length - prefix - suffix), result.Value.After);
                }
                Assert.Equal(first == second, current.ContentEquals(candidate));
            }
            current = next;
        }
    }
}
