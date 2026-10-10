using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextTrackingTests
{
    [Fact]
    public void AbandonedHandlesAreSweptWithoutKeepingTheirRegistrationAlive()
    {
        var document = new TextDocument("abc");
        var weak = AbandonRange(document);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.TryGetTarget(out _));
        document.Edit(edit => edit.Insert(0, "!"));
        Assert.Equal(0, document.Tracking.Count);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<TextTrackedRange> AbandonRange(TextDocument document) => new(document.Tracking.TrackRange(new(0, 1)));

    [Fact]
    public void DeletionAtAnAnchorInvalidatesItAndResetInvalidatesPreservedHandles()
    {
        var document = new TextDocument("abc");
        using var point = document.Tracking.TrackPosition(1);
        using var empty = document.Tracking.TrackRange(new(1, 0));
        using var preserved = document.Tracking.TrackRange(new(0, 3), deletion: TrackingDeletionBehavior.Preserve);
        document.Edit(edit => edit.Delete(new(1, 1)));
        Assert.Null(point.Position);
        Assert.Null(empty.Range);
        Assert.Equal(new TextSpan(0, 2), preserved.Range);
        document.Tracking.InvalidateAll();
        Assert.False(preserved.IsValid);
        Assert.Equal(0, document.Tracking.Count);
    }

    [Fact]
    public void PrefixInsertionsShiftAnIndexedSuffixWithoutVisitingEveryHandle()
    {
        var document = new TextDocument(new string('x', 100_000));
        var tracked = Enumerable.Range(0, 10_000).Select(i => document.Tracking.TrackRange(new(10 + i * 9, 2))).ToArray();
        for (var i = 0; i < 20; i++)
        {
            document.Edit(edit => edit.Insert(0, "prefix"));
            Assert.True(document.Tracking.LastChangeVisitCount < 100, $"Visited {document.Tracking.LastChangeVisitCount} index nodes.");
        }
        Assert.Equal(new TextSpan(130, 2), tracked[0].Range);
        Assert.Equal(new TextSpan(130 + 9999 * 9, 2), tracked[^1].Range);
        foreach (var handle in tracked)
            handle.Dispose();
        document.Undo();
        Assert.All(tracked, handle => Assert.False(handle.IsValid));
    }

    [Fact]
    public void DeletingInteriorSourceInvalidatesOverlappingRangesAndUndoDoesNotReviveThem()
    {
        var document = new TextDocument("0123456789");
        using var entire = document.Tracking.TrackRange(new(0, 10));
        using var preserved = document.Tracking.TrackRange(new(0, 10), deletion: TrackingDeletionBehavior.Preserve);
        using var after = document.Tracking.TrackPosition(9);
        using var inside = document.Tracking.TrackPosition(5);
        document.Edit(edit => edit.Delete(new(3, 4)));
        Assert.Null(entire.Range);
        Assert.Null(inside.Position);
        Assert.Equal(new TextSpan(0, 6), preserved.Range);
        Assert.Equal(5, after.Position);
        document.Undo();
        Assert.Null(entire.Range);
        Assert.Null(inside.Position);
        Assert.Equal(new TextSpan(0, 10), preserved.Range);
        Assert.Equal(9, after.Position);
    }

    [Fact]
    public void BoundaryAffinitiesAndDisposalSurviveLazyAncestorShifts()
    {
        var document = new TextDocument("abcdef");
        using var before = document.Tracking.TrackPosition(2, PositionAffinity.BeforeInsertion);
        using var after = document.Tracking.TrackPosition(2);
        using var middle = document.Tracking.TrackRange(new(2, 2));
        document.Edit(edit => edit.Insert(0, "prefix"));
        document.Edit(edit => edit.Insert(8, "X"));
        Assert.Equal(8, before.Position);
        Assert.Equal(9, after.Position);
        Assert.Equal(new TextSpan(9, 2), middle.Range);
        after.Dispose();
        document.Edit(edit => edit.Insert(0, "!"));
        Assert.Null(after.Position);
        Assert.Equal(9, before.Position);
        Assert.Equal(new TextSpan(10, 2), middle.Range);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(53)]
    public void RandomIndexedRangesMatchSequentialReferenceMapping(int seed)
    {
        var document = new TextDocument(new string('x', 2000));
        var random = new Random(seed);
        var ranges = Enumerable.Range(0, 400).Select(_ =>
        {
            var start = random.Next(2001);
            return new TextSpan(start, random.Next(Math.Min(30, 2000 - start) + 1));
        }).ToArray();
        var expected = ranges.Select(range => (TextSpan?)range).ToArray();
        var tracked = ranges.Select((range, i) => document.Tracking.TrackRange(range,
            deletion: i % 2 == 0 ? TrackingDeletionBehavior.Preserve : TrackingDeletionBehavior.Invalidate)).ToArray();
        for (var step = 0; step < 400; step++)
        {
            var start = random.Next(document.Length + 1);
            var removed = random.Next(Math.Min(15, document.Length - start) + 1);
            var inserted = new string('y', random.Next(10));
            var map = new PositionMap(document.Length, [new(new(start, removed), inserted.Length)]);
            if (document.CurrentSnapshot.GetText(new(start, removed)) != inserted)
                for (var i = 0; i < expected.Length; i++)
                    if (expected[i] is { } range)
                        expected[i] = map.Map(range, invalidateOnDeletion: i % 2 != 0);
            document.Edit(edit => edit.Replace(new(start, removed), inserted));
            for (var i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], tracked[i].Range);
            if (step % 19 == 0)
            {
                var i = random.Next(tracked.Length);
                tracked[i].Dispose();
                expected[i] = null;
            }
        }
        foreach (var handle in tracked)
            handle.Dispose();
    }
}
