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

    /// <summary>Gets the preferred device-independent X coordinate for native vertical navigation, or null after other movement.</summary>
    public double? PreferredVisualX { get; init; }

    /// <summary>Gets the number of unmaterialized spaces beyond the anchor's logical line end.</summary>
    public int AnchorVirtualSpaces { get; init; }

    /// <summary>Gets the number of unmaterialized spaces beyond the active endpoint's logical line end.</summary>
    public int ActiveVirtualSpaces { get; init; }

    /// <summary>Gets whether both real and virtual endpoints coincide.</summary>
    public bool IsEmpty => Anchor == Active && AnchorVirtualSpaces == ActiveVirtualSpaces;

    internal bool IsReversed => Anchor > Active || Anchor == Active && AnchorVirtualSpaces > ActiveVirtualSpaces;
    internal int StartVirtualSpaces => IsReversed ? ActiveVirtualSpaces : AnchorVirtualSpaces;
    internal int EndVirtualSpaces => IsReversed ? AnchorVirtualSpaces : ActiveVirtualSpaces;

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
            ArgumentOutOfRangeException.ThrowIfNegative(item.AnchorVirtualSpaces);
            ArgumentOutOfRangeException.ThrowIfNegative(item.ActiveVirtualSpaces);
            if (item.Id == Guid.Empty || !identities.Add(item.Id))
                throw new ArgumentException("Each selection must have a unique, nonempty identity.", nameof(selections));
            if (!Enum.IsDefined(item.Affinity))
                throw new ArgumentException("A selection has an invalid caret affinity.", nameof(selections));
            if (item.PreferredVisualX is { } x && !double.IsFinite(x))
                throw new ArgumentException("Preferred visual coordinates must be finite.", nameof(selections));
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
            var startSpaces = previous.Range.Start == item.Range.Start ? Math.Min(previous.StartVirtualSpaces, item.StartVirtualSpaces) : previous.StartVirtualSpaces;
            var endSpaces = previous.Range.End == item.Range.End ? Math.Max(previous.EndVirtualSpaces, item.EndVirtualSpaces) :
                end == previous.Range.End ? previous.EndVirtualSpaces : item.EndVirtualSpaces;
            normalized[^1] = winner with
            {
                Anchor = winner.IsReversed ? end : start,
                Active = winner.IsReversed ? start : end,
                AnchorVirtualSpaces = winner.IsReversed ? endSpaces : startSpaces,
                ActiveVirtualSpaces = winner.IsReversed ? startSpaces : endSpaces,
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

    /// <summary>Validates real endpoints and requires virtual endpoints to be at a logical line end.</summary>
    public void Validate(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot.Length);
        foreach (var item in _items)
        {
            Check(item.Anchor, item.AnchorVirtualSpaces);
            Check(item.Active, item.ActiveVirtualSpaces);
        }
        void Check(int position, int spaces)
        {
            if (spaces != 0 && snapshot.GetLine(snapshot.GetPosition(position).Line).Span.End != position)
                throw new ArgumentException("Virtual space is only valid at a logical line end.");
        }
    }

    /// <inheritdoc />
    public IEnumerator<TextSelection> GetEnumerator() => ((IEnumerable<TextSelection>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
