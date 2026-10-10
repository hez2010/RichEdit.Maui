namespace TextEdit.Core;

/// <summary>A directional selection expressed as zero-based UTF-16 anchor and active offsets.</summary>
public readonly record struct TextSelectionState
{
    /// <summary>Creates a selection whose active endpoint is the caret.</summary>
    public TextSelectionState(int anchor, int active)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchor);
        ArgumentOutOfRangeException.ThrowIfNegative(active);
        Anchor = anchor;
        Active = active;
    }
    /// <summary>Gets the fixed endpoint.</summary>
    public int Anchor { get; }
    /// <summary>Gets the moving endpoint.</summary>
    public int Active { get; }
    /// <summary>Gets the normalized selected range.</summary>
    public TextSpan Range => new(Math.Min(Anchor, Active), Math.Abs(Active - Anchor));
    /// <summary>Gets whether the active endpoint precedes the anchor.</summary>
    public bool IsReversed => Active < Anchor;
    /// <summary>Gets whether this selection is collapsed.</summary>
    public bool IsEmpty => Anchor == Active;
    /// <summary>Creates a forward selection over a bounded range.</summary>
    public static TextSelectionState FromRange(TextSpan range) => new(range.Start, range.End);
    /// <summary>Clamps both endpoints to the supplied source length.</summary>
    public TextSelectionState Clamp(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new(Math.Min(Anchor, length), Math.Min(Active, length));
    }
    /// <summary>Maps both endpoints after inserted text through an exact position map.</summary>
    public TextSelectionState Map(PositionMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new(map.Map(Anchor)!.Value, map.Map(Active)!.Value);
    }
}
