using System.Collections.Immutable;

namespace RichEdit.Core;

/// <summary>An immutable table grid that resolves merged cells and normalizes cell-selection rectangles.</summary>
public sealed class RichTableGrid
{
    private readonly RichNodeId[,] _cells;
    private readonly ImmutableDictionary<RichNodeId, RichCellSelection> _extents;
    internal RichTableGrid(RichDocumentSnapshot snapshot, RichTable table)
    {
        Table = table.Id;
        Rows = table.Rows.Count;
        Columns = table.Columns.Length;
        // Bound allocation before multiplying dimensions; malformed imports must not
        // turn span validation into an unbounded allocation.
        if (Rows <= 0 || Columns <= 0 || (long)Rows * Columns > 1_000_000)
            throw new ArgumentException("A table grid must contain between one and one million positions.");
        _cells = new RichNodeId[Rows, Columns];
        var extents = ImmutableDictionary.CreateBuilder<RichNodeId, RichCellSelection>();
        for (var row = 0; row < Rows; row++)
        {
            var previous = -1;
            foreach (var id in snapshot.Get<RichTableRow>(table.Rows[row]).Cells)
            {
                var cell = snapshot.Get<RichTableCell>(id);
                if (cell.Column <= previous || cell.ColumnSpan <= 0 || cell.RowSpan <= 0 || cell.Column < 0 ||
                    cell.ColumnSpan > Columns - cell.Column || cell.RowSpan > Rows - row)
                    throw new ArgumentException("Cells must be column ordered with positive, bounded spans.");
                previous = cell.Column;
                extents.Add(id, new(Table, row, cell.Column, cell.RowSpan, cell.ColumnSpan));
                for (var y = row; y < row + cell.RowSpan; y++)
                    for (var x = cell.Column; x < cell.Column + cell.ColumnSpan; x++)
                    {
                        if (_cells[y, x].IsValid) throw new ArgumentException("Table cells cannot overlap.");
                        _cells[y, x] = id;
                    }
            }
        }
        foreach (var id in _cells) if (!id.IsValid) throw new ArgumentException("Table cells must cover the complete grid.");
        _extents = extents.ToImmutable();
    }
    /// <summary>Gets the table identity.</summary>
    public RichNodeId Table { get; }
    /// <summary>Gets the logical row count.</summary>
    public int Rows { get; }
    /// <summary>Gets the logical column count.</summary>
    public int Columns { get; }
    /// <summary>Gets the cell that covers a zero-based grid position.</summary>
    public RichNodeId this[int row, int column] => row >= 0 && row < Rows && column >= 0 && column < Columns ? _cells[row, column] :
        throw new ArgumentOutOfRangeException(nameof(row));
    /// <summary>Gets the complete extent of one merged or unmerged cell.</summary>
    public RichCellSelection GetExtent(RichNodeId cell) => _extents.TryGetValue(cell, out var value) ? value :
        throw new ArgumentException("The cell does not belong to this table.", nameof(cell));

    /// <summary>Expands a rectangle until every intersected merged cell is wholly contained.</summary>
    public RichCellSelection Expand(RichCellSelection selection)
    {
        if (selection.Table != Table || selection.Row < 0 || selection.Column < 0 || selection.RowCount <= 0 || selection.ColumnCount <= 0 ||
            selection.RowCount > Rows - selection.Row || selection.ColumnCount > Columns - selection.Column)
            throw new ArgumentException("The selection must be a bounded rectangle in this table.", nameof(selection));
        var top = selection.Row;
        var left = selection.Column;
        var bottom = top + selection.RowCount;
        var right = left + selection.ColumnCount;
        bool changed;
        do
        {
            changed = false;
            for (var row = top; row < bottom; row++)
                for (var column = left; column < right; column++)
                {
                    var extent = _extents[_cells[row, column]];
                    var nextTop = Math.Min(top, extent.Row);
                    var nextLeft = Math.Min(left, extent.Column);
                    var nextBottom = Math.Max(bottom, extent.Row + extent.RowCount);
                    var nextRight = Math.Max(right, extent.Column + extent.ColumnCount);
                    changed |= nextTop != top || nextLeft != left || nextBottom != bottom || nextRight != right;
                    (top, left, bottom, right) = (nextTop, nextLeft, nextBottom, nextRight);
                }
        } while (changed);
        return new(Table, top, left, bottom - top, right - left);
    }
}
