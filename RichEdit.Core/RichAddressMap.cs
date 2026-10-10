using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>An immutable sequence mapping rich points through text edits and structural paragraph changes.</summary>
public sealed class RichAddressMap
{
    private readonly ImmutableArray<Step> _steps;
    internal RichAddressMap(IEnumerable<Step> steps) => _steps = [.. steps];

    /// <summary>Maps a point, returning null when its paragraph was removed without a replacement address.</summary>
    public RichTextPoint? Map(RichTextPoint point)
    {
        RichTextPoint? result = point;
        foreach (var step in _steps)
        {
            if (result is not { } current) break;
            result = step.Map(current);
        }
        return result;
    }

    internal abstract record Step
    {
        internal abstract RichTextPoint? Map(RichTextPoint point);
    }
    internal sealed record Text(RichNodeId Paragraph, int Start, int Removed, int Inserted) : Step
    {
        internal override RichTextPoint? Map(RichTextPoint point)
        {
            if (point.Paragraph != Paragraph || point.Offset < Start) return point;
            if (point.Offset > Start + Removed || Removed != 0 && point.Offset == Start + Removed)
                return point with { Offset = point.Offset + Inserted - Removed };
            var end = point.Affinity == CaretAffinity.Upstream ? Start : Start + Inserted;
            return point with { Offset = end };
        }
    }
    internal sealed record Split(RichNodeId Before, RichNodeId After, int Offset) : Step
    {
        internal override RichTextPoint? Map(RichTextPoint point) => point.Paragraph == Before &&
            (point.Offset > Offset || point.Offset == Offset && point.Affinity == CaretAffinity.Downstream)
            ? point with { Paragraph = After, Offset = point.Offset - Offset } : point;
    }
    internal sealed record Join(RichNodeId Removed, RichNodeId Target, int Offset) : Step
    {
        internal override RichTextPoint? Map(RichTextPoint point) => point.Paragraph == Removed
            ? point with { Paragraph = Target, Offset = checked(point.Offset + Offset) } : point;
    }
    internal sealed record Remove(ImmutableHashSet<RichNodeId> Nodes) : Step
    {
        internal override RichTextPoint? Map(RichTextPoint point) => Nodes.Contains(point.Paragraph) ? null : point;
    }
}
