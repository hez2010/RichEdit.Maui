using System.Collections;
using System.Collections.Immutable;

namespace TextEdit.Core;

/// <summary>The preferred visual edge at a wrap or bidirectional boundary.</summary>
public enum CaretAffinity
{
    /// <summary>The edge adjacent to preceding text.</summary>
    Upstream,
    /// <summary>The edge adjacent to following text.</summary>
    Downstream,
}

/// <summary>A directional selection with an identity retained through edits.</summary>
public sealed record TextSelection
{
    /// <summary>Creates a selection with a new view-local identity.</summary>
    public TextSelection(int anchor, int active)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchor);
        ArgumentOutOfRangeException.ThrowIfNegative(active);
        Id = Guid.NewGuid();
        Anchor = anchor;
        Active = active;
    }

    /// <summary>Gets the stable selection identity.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the fixed UTF-16 endpoint.</summary>
    public int Anchor { get; init; }

    /// <summary>Gets the moving UTF-16 endpoint.</summary>
    public int Active { get; init; }

    /// <summary>Gets the preferred native visual edge.</summary>
    public CaretAffinity Affinity { get; init; } = CaretAffinity.Downstream;

    /// <summary>Gets the normalized source range.</summary>
    public TextSpan Range => new(Math.Min(Anchor, Active), Math.Abs(Active - Anchor));
}

/// <summary>An immutable, normalized set of selections with one primary native selection.</summary>
/// <remarks>Overlapping or touching ranges and duplicate carets merge. The primary selection's identity and direction win when it participates in a merge.</remarks>
public sealed class TextSelectionSet : IReadOnlyList<TextSelection>
{
    private readonly ImmutableArray<TextSelection> _items;

    /// <summary>Copies and normalizes selections. The primary index addresses the supplied enumeration before sorting.</summary>
    public TextSelectionSet(IEnumerable<TextSelection> selections, int primaryIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(selections);
        var items = selections.ToArray();
        ArgumentOutOfRangeException.ThrowIfNegative(primaryIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(primaryIndex, items.Length);
        var identities = new HashSet<Guid>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentOutOfRangeException.ThrowIfNegative(item.Anchor);
            ArgumentOutOfRangeException.ThrowIfNegative(item.Active);
            if (item.Id == Guid.Empty || !identities.Add(item.Id))
                throw new ArgumentException("Each selection must have a unique, nonempty identity.", nameof(selections));
            if (!Enum.IsDefined(item.Affinity))
                throw new ArgumentException("A selection has an invalid caret affinity.", nameof(selections));
        }

        var primaryId = items[primaryIndex].Id;
        var normalized = new List<TextSelection>();
        foreach (var item in items.OrderBy(static item => item.Range.Start).ThenBy(static item => item.Range.End))
        {
            if (normalized.Count == 0 || normalized[^1].Range.End < item.Range.Start)
            {
                normalized.Add(item);
                continue;
            }

            var previous = normalized[^1];
            var winner = item.Id == primaryId ? item : previous;
            var start = previous.Range.Start;
            var end = Math.Max(previous.Range.End, item.Range.End);
            normalized[^1] = winner with
            {
                Anchor = winner.Anchor > winner.Active ? end : start,
                Active = winner.Anchor > winner.Active ? start : end,
            };
        }

        _items = [.. normalized];
        PrimaryIndex = normalized.FindIndex(item => item.Id == primaryId);
    }

    /// <summary>Gets the normalized primary index.</summary>
    public int PrimaryIndex { get; }

    /// <summary>Gets the primary selection controlled by native input.</summary>
    public TextSelection Primary => _items[PrimaryIndex];

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public TextSelection this[int index] => _items[index];

    /// <summary>Creates one collapsed selection.</summary>
    public static TextSelectionSet Caret(int position) => new((TextSelection[])[new(position, position)]);

    /// <summary>Validates every selection against the document length.</summary>
    public void Validate(int textLength)
    {
        foreach (var item in _items)
            item.Range.Validate(textLength);
    }

    /// <inheritdoc />
    public IEnumerator<TextSelection> GetEnumerator() => ((IEnumerable<TextSelection>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
