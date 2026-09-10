namespace RichEdit.Maui;

/// <summary>A directional selection expressed as zero-based UTF-16 anchor and active offsets.</summary>
public readonly record struct RichTextSelectionState
{
    /// <summary>Creates a selection. The active offset is the caret end.</summary>
    /// <param name="anchor">The fixed end of the selection.</param>
    /// <param name="active">The moving, or caret, end of the selection.</param>
    public RichTextSelectionState(int anchor, int active)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchor);
        ArgumentOutOfRangeException.ThrowIfNegative(active);
        Anchor = anchor;
        Active = active;
    }

    /// <summary>Gets the fixed selection endpoint.</summary>
    public int Anchor { get; }
    /// <summary>Gets the caret endpoint.</summary>
    public int Active { get; }
    /// <summary>Gets the normalized content range.</summary>
    public RichTextRange Range => new(Math.Min(Anchor, Active), Math.Abs(Active - Anchor));
    /// <summary>Gets whether the caret precedes the anchor.</summary>
    public bool IsReversed => Active < Anchor;
    /// <summary>Gets whether the selection is a caret.</summary>
    public bool IsEmpty => Anchor == Active;

    /// <summary>Creates a forward selection over a content range.</summary>
    /// <param name="range">The selected content.</param>
    /// <returns>A selection with the caret at the end.</returns>
    public static RichTextSelectionState FromRange(RichTextRange range) => new(range.Start, range.End);

    internal RichTextSelectionState Clamp(int length) => new(Math.Min(Anchor, length), Math.Min(Active, length));

    internal RichTextSelectionState Map(IEnumerable<RichTextChange> changes, int length)
    {
        var anchor = Anchor;
        var active = Active;
        foreach (var change in changes.OfType<RichTextTextChange>())
        {
            anchor = MapOffset(anchor, change);
            active = MapOffset(active, change);
        }
        return new RichTextSelectionState(anchor, active).Clamp(length);
    }

    internal static int MapOffset(int offset, RichTextTextChange change) =>
        RichTextPositionMap.Map(offset, change, RichTextTrackingAffinity.AfterInsertion)!.Value;
}
