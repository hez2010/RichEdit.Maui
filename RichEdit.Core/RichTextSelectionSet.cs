using System.Collections;
using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>A rich text selection with a stable view-local identity.</summary>
public sealed record RichTextSelectionItem(RichTextSelection Selection)
{
    /// <summary>Gets a stable, nonempty selection identity.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Gets a preferred native visual X position for vertical movement.</summary>
    public double? PreferredVisualX { get; init; }
}

/// <summary>A normalized set of directional text selections within one rich story.</summary>
/// <remarks>Object and table-cell selections remain separate structural selection kinds. This set contains no snapshot reference.</remarks>
public sealed record RichTextSelectionSet : RichSelection, IReadOnlyList<RichTextSelectionItem>
{
    private readonly ImmutableArray<RichTextSelectionItem> _items;

    /// <summary>Validates and normalizes against a frozen snapshot. The primary index addresses the input enumeration before sorting.</summary>
    public RichTextSelectionSet(RichDocumentSnapshot snapshot, IEnumerable<RichTextSelectionItem> selections, int primaryIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selections);
        var items = selections.ToArray();
        if (primaryIndex < 0 || primaryIndex >= items.Length) throw new ArgumentOutOfRangeException(nameof(primaryIndex));
        var story = items[primaryIndex]?.Selection.Anchor.Story ?? throw new ArgumentException("Selections cannot be null.", nameof(selections));
        var identities = new HashSet<Guid>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            snapshot.NormalizeSelection(item.Selection);
            if (item.Selection.Anchor.Story != story) throw new ArgumentException("Every selection must belong to the same story.", nameof(selections));
            if (item.Id == Guid.Empty || !identities.Add(item.Id) || item.PreferredVisualX is { } x && !double.IsFinite(x))
                throw new ArgumentException("Selection identities must be unique and nonempty, and visual coordinates finite.", nameof(selections));
        }
        var primary = items[primaryIndex].Id;
        var normalized = new List<RichTextSelectionItem>();
        var comparer = Comparer<RichTextPoint>.Create(snapshot.ComparePoints);
        foreach (var item in items.OrderBy(item => Start(item.Selection), comparer).ThenBy(item => End(item.Selection), comparer))
        {
            if (normalized.Count == 0 || snapshot.ComparePoints(End(normalized[^1].Selection), Start(item.Selection)) < 0)
            { normalized.Add(item); continue; }
            var previous = normalized[^1];
            var winner = item.Id == primary ? item : previous;
            var start = Start(previous.Selection);
            var end = snapshot.ComparePoints(End(previous.Selection), End(item.Selection)) > 0 ? End(previous.Selection) : End(item.Selection);
            var reverse = snapshot.ComparePoints(winner.Selection.Anchor, winner.Selection.Active) > 0;
            normalized[^1] = winner with { Selection = reverse ? new(end, start) : new(start, end) };
        }
        _items = [.. normalized];
        PrimaryIndex = normalized.FindIndex(item => item.Id == primary);
        RichTextPoint Start(RichTextSelection selection) => snapshot.ComparePoints(selection.Anchor, selection.Active) <= 0 ? selection.Anchor : selection.Active;
        RichTextPoint End(RichTextSelection selection) => snapshot.ComparePoints(selection.Anchor, selection.Active) >= 0 ? selection.Anchor : selection.Active;
    }

    /// <summary>Gets the primary selection's normalized index.</summary>
    public int PrimaryIndex { get; }
    /// <summary>Gets the selection represented by native selection and composition UI.</summary>
    public RichTextSelectionItem Primary => _items[PrimaryIndex];
    /// <inheritdoc />
    public int Count => _items.Length;
    /// <inheritdoc />
    public RichTextSelectionItem this[int index] => _items[index];
    /// <summary>Compares value-only selections and primary identity.</summary>
    public bool Equals(RichTextSelectionSet? other) => other is not null && PrimaryIndex == other.PrimaryIndex && _items.SequenceEqual(other._items);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(PrimaryIndex);
        foreach (var item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
    /// <inheritdoc />
    public IEnumerator<RichTextSelectionItem> GetEnumerator() => ((IEnumerable<RichTextSelectionItem>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
