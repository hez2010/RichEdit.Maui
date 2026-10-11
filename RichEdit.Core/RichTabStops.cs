using System.Collections;
using System.Collections.Immutable;

namespace RichEdit.Core;

/// <summary>Alignment of text following an authored tab stop.</summary>
public enum RichTabAlignment
{
    /// <summary>Align the leading edge.</summary>
    Left,
    /// <summary>Center around the stop.</summary>
    Center,
    /// <summary>Align the trailing edge.</summary>
    Right,
    /// <summary>Align the decimal separator.</summary>
    Decimal,
}

/// <summary>The leader before an authored tab stop.</summary>
public enum RichTabLeader
{
    /// <summary>No leader.</summary>
    None,
    /// <summary>Dotted leader.</summary>
    Dots,
    /// <summary>Hyphen leader.</summary>
    Hyphens,
    /// <summary>Continuous underline leader.</summary>
    Underline,
    /// <summary>Thick continuous leader.</summary>
    ThickLine,
    /// <summary>Equals-sign leader.</summary>
    Equals,
}

/// <summary>A positive tab position measured from the paragraph's leading edge.</summary>
public readonly record struct RichTabStop(RichLength Position, RichTabAlignment Alignment = RichTabAlignment.Left, RichTabLeader Leader = RichTabLeader.None);

/// <summary>An immutable, value-equal collection of unique tab stops ordered by position.</summary>
public sealed class RichTabStops : IReadOnlyCollection<RichTabStop>, IEquatable<RichTabStops>
{
    private readonly ImmutableSortedDictionary<int, RichTabStop> _items;
    /// <summary>Gets the empty set of explicit stops; the native default tab interval remains available.</summary>
    public static RichTabStops Empty { get; } = new(ImmutableSortedDictionary<int, RichTabStop>.Empty);
    private RichTabStops(ImmutableSortedDictionary<int, RichTabStop> items) => _items = items;
    /// <summary>Copies and validates unique positive positions, alignments and leaders.</summary>
    public RichTabStops(IEnumerable<RichTabStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var items = ImmutableSortedDictionary.CreateBuilder<int, RichTabStop>();
        foreach (var stop in stops)
        {
            Validate(stop);
            if (items.ContainsKey(stop.Position.Twips)) throw new ArgumentException("Tab positions must be unique.", nameof(stops));
            items.Add(stop.Position.Twips, stop);
        }
        _items = items.ToImmutable();
    }
    /// <inheritdoc />
    public int Count => _items.Count;
    /// <inheritdoc />
    public IEnumerator<RichTabStop> GetEnumerator() => _items.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    /// <inheritdoc />
    public bool Equals(RichTabStops? other) => ReferenceEquals(this, other) || other is not null && Count == other.Count && this.SequenceEqual(other);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RichTabStops value && Equals(value);
    /// <inheritdoc />
    public override int GetHashCode() { var hash = new HashCode(); foreach (var item in this) hash.Add(item); return hash.ToHashCode(); }
    internal RichTabStops Set(RichTabStop stop) { Validate(stop); return new(_items.SetItem(stop.Position.Twips, stop)); }
    private static void Validate(RichTabStop stop)
    {
        if (stop.Position.Twips <= 0 || !Enum.IsDefined(stop.Alignment) || !Enum.IsDefined(stop.Leader))
            throw new ArgumentException("Tab stops require positive positions and valid alignment and leader values.", nameof(stop));
    }
}
