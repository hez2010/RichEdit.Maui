using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>An immutable sequence mapping rich points through text edits and structural paragraph changes.</summary>
public sealed class RichAddressMap
{
    private readonly ImmutableArray<Step> _steps;
    internal RichAddressMap(IEnumerable<Step> steps) => _steps = [.. steps];
    internal RichAddressMap Invert() => new(_steps.Reverse().Select(step => step switch
    {
        Text text => (Step)new Text(text.Paragraph, text.Start, text.Inserted, text.Removed),
        Split split => new Join(split.After, split.Before, split.Offset),
        Join join => new Split(join.Target, join.Removed, join.Offset),
        TableAxis axis => new TableAxis(axis.Table, axis.Rows, axis.Index, axis.Inserted, axis.Removed),
        _ => new Remove([]),
    }));
    internal RichAddressMap Then(RichAddressMap next) => new(_steps.AddRange(next._steps));

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

    internal RichCellSelection MapCells(RichCellSelection selection)
    {
        foreach (var change in _steps.OfType<TableAxis>())
        {
            if (change.Table != selection.Table) continue;
            var start = change.Rows ? selection.Row : selection.Column;
            var end = start + (change.Rows ? selection.RowCount : selection.ColumnCount);
            var mappedStart = Endpoint(start, true);
            var mappedEnd = Endpoint(end, false);
            var count = Math.Max(1, mappedEnd - mappedStart);
            selection = change.Rows ? selection with { Row = mappedStart, RowCount = count } :
                selection with { Column = mappedStart, ColumnCount = count };

            int Endpoint(int value, bool after)
            {
                if (value < change.Index) return value;
                if (value > change.Index + change.Removed || change.Removed != 0 && value == change.Index + change.Removed)
                    return value + change.Inserted - change.Removed;
                return change.Index + (after ? change.Inserted : 0);
            }
        }
        return selection;
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
    internal sealed record TableAxis(RichNodeId Table, bool Rows, int Index, int Removed, int Inserted) : Step
    {
        internal override RichTextPoint? Map(RichTextPoint point) => point;
    }
}
