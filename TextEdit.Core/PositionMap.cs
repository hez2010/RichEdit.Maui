using System.Collections.Immutable;

namespace TextEdit.Core;

/// <summary>Which side of inserted text retains an endpoint at the insertion boundary.</summary>
public enum PositionAffinity
{
    /// <summary>The endpoint stays before inserted text.</summary>
    BeforeInsertion,
    /// <summary>The endpoint moves after inserted text.</summary>
    AfterInsertion,
}

/// <summary>One replacement's geometry, in coordinates after all preceding replacements.</summary>
public readonly record struct PositionChange(TextSpan OldSpan, int NewLength);

/// <summary>Maps exact UTF-16 positions through a validated sequence of replacements.</summary>
public sealed class PositionMap
{
    /// <summary>Creates a map from sequential replacement coordinates.</summary>
    public PositionMap(int oldLength, IEnumerable<PositionChange> changes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(oldLength);
        ArgumentNullException.ThrowIfNull(changes);
        OldLength = oldLength;
        Changes = changes.ToImmutableArray();
        var length = oldLength;
        foreach (var change in Changes)
        {
            change.OldSpan.Validate(length);
            ArgumentOutOfRangeException.ThrowIfNegative(change.NewLength);
            length = checked(length - change.OldSpan.Length + change.NewLength);
        }
        NewLength = length;
    }

    /// <summary>Gets source length before the first replacement.</summary>
    public int OldLength { get; }
    /// <summary>Gets source length after the last replacement.</summary>
    public int NewLength { get; }
    /// <summary>Gets sequential replacement geometry.</summary>
    public ImmutableArray<PositionChange> Changes { get; }

    /// <summary>Maps an endpoint. Replaced interior positions collapse according to affinity, or return null when invalidation is requested.</summary>
    public int? Map(int position, PositionAffinity affinity = PositionAffinity.AfterInsertion, bool invalidateOnDeletion = false)
    {
        new TextSpan(position, 0).Validate(OldLength);
        if (!Enum.IsDefined(affinity))
            throw new ArgumentOutOfRangeException(nameof(affinity));
        foreach (var change in Changes)
        {
            var range = change.OldSpan;
            if (invalidateOnDeletion && position > range.Start && position < range.End)
                return null;
            position = MapEndpoint(position, change, affinity);
        }
        return position;
    }

    /// <summary>Maps a range with independent endpoint affinities. Optional invalidation detects deletion of any covered source.</summary>
    public TextSpan? Map(TextSpan range, PositionAffinity startAffinity = PositionAffinity.AfterInsertion,
        PositionAffinity endAffinity = PositionAffinity.BeforeInsertion, bool invalidateOnDeletion = false)
    {
        range.Validate(OldLength);
        if (!Enum.IsDefined(startAffinity) || !Enum.IsDefined(endAffinity))
            throw new ArgumentOutOfRangeException(nameof(startAffinity));
        foreach (var change in Changes)
        {
            if (invalidateOnDeletion && Math.Max(range.Start, change.OldSpan.Start) < Math.Min(range.End, change.OldSpan.End))
                return null;
            var start = MapEndpoint(range.Start, change, startAffinity);
            var end = MapEndpoint(range.End, change, endAffinity);
            range = new(start, Math.Max(0, end - start));
        }
        return range;
    }

    /// <summary>Maps directional selections while retaining identities and primary ownership.</summary>
    public TextSelectionSet Map(TextSelectionSet selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        selections.Validate(OldLength);
        return new(selections.Select(selection => selection with
        {
            Anchor = Map(selection.Anchor)!.Value,
            Active = Map(selection.Active)!.Value,
            PreferredVisualX = null,
        }), selections.PrimaryIndex);
    }

    /// <summary>Creates the inverse geometry in reverse operation order. Deleted interior positions cannot be reconstructed from coordinates alone.</summary>
    public PositionMap Invert() => new(NewLength, Changes.Reverse().Select(change =>
        new PositionChange(new(change.OldSpan.Start, change.NewLength), change.OldSpan.Length)));

    /// <summary>Appends a subsequent map whose input length matches this map's output.</summary>
    public PositionMap Then(PositionMap next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.OldLength != NewLength)
            throw new ArgumentException("Composed maps must share their intermediate length.", nameof(next));
        return new(OldLength, Changes.AddRange(next.Changes));
    }

    private static int MapEndpoint(int position, PositionChange change, PositionAffinity affinity)
    {
        if (position < change.OldSpan.Start)
            return position;
        if (position > change.OldSpan.End || position == change.OldSpan.End && !change.OldSpan.IsEmpty)
            return checked(position + change.NewLength - change.OldSpan.Length);
        return change.OldSpan.Start + (affinity == PositionAffinity.AfterInsertion ? change.NewLength : 0);
    }
}
