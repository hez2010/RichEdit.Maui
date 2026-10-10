using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class PositionMapTests
{
    [Fact]
    public void ReplacementBoundariesAndDeletedInteriorHaveDistinctAffinity()
    {
        var map = new PositionMap(10, [new(new(2, 3), 1), new(new(7, 0), 2)]);
        Assert.Equal(10, map.NewLength);
        Assert.Equal(2, map.Map(2, PositionAffinity.BeforeInsertion));
        Assert.Equal(3, map.Map(2, PositionAffinity.AfterInsertion));
        Assert.Equal(3, map.Map(5, PositionAffinity.BeforeInsertion));
        Assert.Null(map.Map(3, invalidateOnDeletion: true));
        Assert.Equal(9, map.Map(9));
        Assert.Equal(9, map.Invert().Map(9));
        Assert.Equal(new TextSpan(1, 5), map.Map(new TextSpan(1, 7)));
        Assert.Null(map.Map(new TextSpan(1, 7), invalidateOnDeletion: true));
    }

    [Fact]
    public void RangeAffinitiesExcludeBothBoundaryInsertions()
    {
        var first = new PositionMap(10, [new(new(2, 0), 2)]);
        var second = new PositionMap(12, [new(new(8, 0), 1)]);
        var both = first.Then(second);
        Assert.Equal(new TextSpan(4, 4), both.Map(new TextSpan(2, 4)));
        Assert.Equal(new TextSpan(2, 7), both.Map(new TextSpan(2, 4), PositionAffinity.BeforeInsertion, PositionAffinity.AfterInsertion));
        Assert.Throws<ArgumentException>(() => first.Then(first));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionMap(2, [new(new(1, 2), 0)]));
    }

    [Fact]
    public void UnaffectedPositionsSurviveRandomForwardAndInverseMaps()
    {
        var random = new Random(1997);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            const int length = 1000;
            var current = length;
            var changes = new List<PositionChange>();
            for (var i = 0; i < 20; i++)
            {
                var start = random.Next(current + 1);
                var removed = random.Next(Math.Min(10, current - start) + 1);
                var inserted = random.Next(12);
                changes.Add(new(new(start, removed), inserted));
                current += inserted - removed;
            }
            var map = new PositionMap(length, changes);
            var inverse = map.Invert();
            for (var sample = 0; sample < 30; sample++)
            {
                var original = random.Next(length + 1);
                var position = original;
                var affected = false;
                foreach (var change in changes)
                {
                    if (position >= change.OldSpan.Start && position <= change.OldSpan.End)
                    {
                        affected = true;
                        break;
                    }
                    if (position > change.OldSpan.End)
                        position += change.NewLength - change.OldSpan.Length;
                }
                if (!affected)
                {
                    Assert.Equal(position, map.Map(original));
                    Assert.Equal(original, inverse.Map(position));
                }
            }
        }
    }
}
