using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Resizes the existing table columns without changing cells, spans, content, or selections.</summary>
    /// <param name="tableId">The table to resize.</param>
    /// <param name="columns">One positive width per existing column; the total must fit an RTF cell boundary.</param>
    public void SetColumnWidths(RichNodeId tableId, IEnumerable<RichLength> columns)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(columns);
        var table = Get<RichTable>(tableId);
        var widths = columns.ToImmutableArray();
        if (widths.Length != table.Columns.Length || widths.Any(width => width.Twips <= 0) ||
            widths.Sum(width => (long)width.Twips) > int.MaxValue)
            throw new ArgumentException("Supply one positive width per column, with a total no greater than Int32.MaxValue twips.", nameof(columns));
        if (!widths.SequenceEqual(table.Columns)) _nodes[tableId] = table with { Columns = widths };
    }

    /// <summary>Sets authored appearance on a normalized cell rectangle. Merged cells are updated once.</summary>
    public void SetCellFormat(RichCellSelection selection, RichCellFormat format)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(format);
        format.Validate();
        var grid = Grid(selection.Table);
        foreach (var id in Cells(grid, grid.Expand(selection))) _nodes[id] = Get<RichTableCell>(id) with { Format = format };
    }
    /// <summary>Inserts a table whose cells contain empty paragraphs, in the parent document transaction.</summary>
    public RichNodeId InsertTable(RichNodeId parent, int index, int rows, IEnumerable<RichLength> columns)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(columns);
        var widths = columns.ToImmutableArray();
        ValidateDimensions(rows, widths.Length);
        if (widths.Any(width => width.Twips <= 0)) throw new ArgumentException("Column widths must be positive.", nameof(columns));
        var blocks = Blocks(Get<RichNode>(parent));
        if (index < 0 || index > blocks.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var table = Allocate();
        var rowIds = ImmutableList.CreateBuilder<RichNodeId>();
        for (var row = 0; row < rows; row++)
        {
            var id = Allocate();
            var cells = ImmutableList.CreateBuilder<RichNodeId>();
            for (var column = 0; column < widths.Length; column++) cells.Add(CreateCell(id, column));
            _nodes.Add(id, new RichTableRow(id, table, cells.ToImmutable()));
            rowIds.Add(id);
        }
        _nodes.Add(table, new RichTable(table, parent, widths, rowIds.ToImmutable()));
        SetBlocks(parent, blocks.Insert(index, table));
        return table;
    }

    /// <summary>Clears selected cell contents after expanding to whole merged cells, preserving the grid and each cell's initial paragraph defaults.</summary>
    public void ClearCells(RichCellSelection selection)
    {
        VerifyOpen();
        var grid = Grid(selection.Table);
        selection = grid.Expand(selection);
        var before = Candidate(validate: false);
        foreach (var id in Cells(grid, selection))
        {
            var cell = Get<RichTableCell>(id);
            var initial = before.Get<RichParagraph>(before.EnumerateParagraphs(id).First());
            foreach (var block in cell.Blocks) RemoveSubtree(block);
            _nodes[id] = cell with { Blocks = [] };
            EnsureParagraph(id);
            var paragraph = Get<RichParagraph>(Get<RichTableCell>(id).Blocks[0]);
            _nodes[paragraph.Id] = paragraph with { Style = initial.Style, Format = initial.Format, Character = initial.Character };
        }
    }

    /// <summary>Merges a normalized cell rectangle, retaining all cell blocks in row-major order in the top-left cell.</summary>
    public RichNodeId MergeCells(RichCellSelection selection)
    {
        VerifyOpen();
        var grid = Grid(selection.Table);
        selection = grid.Expand(selection);
        var cells = Cells(grid, selection).OrderBy(id => grid.GetExtent(id).Row).ThenBy(id => grid.GetExtent(id).Column).ToArray();
        var first = Get<RichTableCell>(cells[0]);
        var blocks = first.Blocks;
        foreach (var id in cells.Skip(1))
        {
            var cell = Get<RichTableCell>(id);
            foreach (var block in cell.Blocks) _nodes[block] = Reparent(Get<RichBlock>(block), first.Id);
            blocks = blocks.AddRange(cell.Blocks);
            var row = Get<RichTableRow>(cell.Parent!.Value);
            _nodes[row.Id] = row with { Cells = row.Cells.Remove(id) };
            _nodes.Remove(id);
        }
        _nodes[first.Id] = first with { ColumnSpan = selection.ColumnCount, RowSpan = selection.RowCount, Blocks = blocks };
        return first.Id;
    }

    /// <summary>Splits a merged cell into grid cells. The top-left cell retains its identity and content; new cells are empty.</summary>
    public void SplitCell(RichNodeId cell)
    {
        VerifyOpen();
        var original = Get<RichTableCell>(cell);
        var table = Get<RichTable>(Get<RichTableRow>(original.Parent!.Value).Parent!.Value);
        var extent = Grid(table.Id).GetExtent(cell);
        if (extent.RowCount == 1 && extent.ColumnCount == 1) return;
        _nodes[cell] = original with { RowSpan = 1, ColumnSpan = 1 };
        for (var y = extent.Row; y < extent.Row + extent.RowCount; y++)
        {
            var row = Get<RichTableRow>(table.Rows[y]);
            var cells = row.Cells;
            for (var x = extent.Column; x < extent.Column + extent.ColumnCount; x++)
                if (y != extent.Row || x != extent.Column)
                {
                    var id = CreateCell(row.Id, x);
                    _nodes[id] = Get<RichTableCell>(id) with { Format = original.Format };
                    cells = cells.Add(id);
                }
            _nodes[row.Id] = row with { Cells = SortCells(cells) };
        }
    }

    /// <summary>Inserts rows, extending merged cells that cross the insertion boundary.</summary>
    public void InsertRows(RichNodeId tableId, int index, int count = 1)
    {
        VerifyOpen();
        var table = Get<RichTable>(tableId);
        if (index < 0 || index > table.Rows.Count || count <= 0) throw new ArgumentOutOfRangeException(nameof(index));
        ValidateDimensions(checked(table.Rows.Count + count), table.Columns.Length);
        var grid = Grid(tableId);
        var crossing = index > 0 && index < grid.Rows
            ? Enumerable.Range(0, grid.Columns).Select(column => grid[index - 1, column]).Distinct()
                .Where(id => grid.GetExtent(id) is var extent && extent.Row + extent.RowCount > index).ToHashSet()
            : [];
        foreach (var id in crossing)
        {
            var cell = Get<RichTableCell>(id);
            _nodes[id] = cell with { RowSpan = cell.RowSpan + count };
        }
        var added = ImmutableList.CreateBuilder<RichNodeId>();
        for (var offset = 0; offset < count; offset++)
        {
            var id = Allocate();
            var cells = ImmutableList.CreateBuilder<RichNodeId>();
            for (var column = 0; column < grid.Columns; column++)
                if (index == 0 || !crossing.Contains(grid[index - 1, column])) cells.Add(CreateCell(id, column));
            _nodes.Add(id, new RichTableRow(id, tableId, cells.ToImmutable()));
            added.Add(id);
        }
        _nodes[tableId] = table with { Rows = table.Rows.InsertRange(index, added) };
        Record(new RichAddressMap.TableAxis(tableId, true, index, 0, count));
    }

    /// <summary>Deletes rows, shrinking intersected spans and retaining the content of cells that still span surviving rows.</summary>
    public void DeleteRows(RichNodeId tableId, int index, int count = 1)
    {
        VerifyOpen();
        var table = Get<RichTable>(tableId);
        ValidateRemoval(index, count, table.Rows.Count);
        if (count == table.Rows.Count) { DeleteBlock(tableId); return; }
        var grid = Grid(tableId);
        foreach (var id in AllCells(table))
        {
            var cell = Get<RichTableCell>(id);
            var extent = grid.GetExtent(id);
            var removed = Math.Max(0, Math.Min(extent.Row + extent.RowCount, index + count) - Math.Max(extent.Row, index));
            if (removed == 0) continue;
            if (removed == cell.RowSpan) { RemoveSubtree(id); continue; }
            var rowSpan = cell.RowSpan - removed;
            if (extent.Row >= index)
            {
                var row = Get<RichTableRow>(table.Rows[index + count]);
                _nodes[id] = cell with { Parent = row.Id, RowSpan = rowSpan };
                _nodes[row.Id] = row with { Cells = SortCells(row.Cells.Add(id)) };
            }
            else _nodes[id] = cell with { RowSpan = rowSpan };
        }
        for (var row = index; row < index + count; row++) _nodes.Remove(table.Rows[row]);
        _nodes[tableId] = table with { Rows = table.Rows.RemoveRange(index, count) };
        Record(new RichAddressMap.TableAxis(tableId, true, index, count, 0));
    }

    /// <summary>Inserts columns, extending merged cells that cross the insertion boundary.</summary>
    public void InsertColumns(RichNodeId tableId, int index, IEnumerable<RichLength> columns)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(columns);
        var table = Get<RichTable>(tableId);
        var widths = columns.ToImmutableArray();
        if (index < 0 || index > table.Columns.Length || widths.IsEmpty || widths.Any(width => width.Twips <= 0))
            throw new ArgumentException("Supply positive column widths at a bounded insertion index.");
        ValidateDimensions(table.Rows.Count, checked(table.Columns.Length + widths.Length));
        var grid = Grid(tableId);
        var crossing = new HashSet<RichNodeId>();
        foreach (var id in AllCells(table))
        {
            var cell = Get<RichTableCell>(id);
            if (cell.Column >= index) _nodes[id] = cell with { Column = cell.Column + widths.Length };
            else if (cell.Column + cell.ColumnSpan > index)
            {
                crossing.Add(id);
                _nodes[id] = cell with { ColumnSpan = cell.ColumnSpan + widths.Length };
            }
        }
        for (var y = 0; y < grid.Rows; y++)
        {
            if (index != 0 && crossing.Contains(grid[y, index - 1])) continue;
            var row = Get<RichTableRow>(table.Rows[y]);
            var cells = row.Cells;
            for (var x = 0; x < widths.Length; x++) cells = cells.Add(CreateCell(row.Id, index + x));
            _nodes[row.Id] = row with { Cells = SortCells(cells) };
        }
        _nodes[tableId] = table with { Columns = table.Columns.InsertRange(index, widths) };
        Record(new RichAddressMap.TableAxis(tableId, false, index, 0, widths.Length));
    }

    /// <summary>Deletes columns, shrinking intersected spans and retaining any surviving part of a merged cell.</summary>
    public void DeleteColumns(RichNodeId tableId, int index, int count = 1)
    {
        VerifyOpen();
        var table = Get<RichTable>(tableId);
        ValidateRemoval(index, count, table.Columns.Length);
        if (count == table.Columns.Length) { DeleteBlock(tableId); return; }
        foreach (var rowId in table.Rows)
        {
            var row = Get<RichTableRow>(rowId);
            var cells = row.Cells;
            foreach (var id in row.Cells)
            {
                var cell = Get<RichTableCell>(id);
                var removed = Math.Max(0, Math.Min(cell.Column + cell.ColumnSpan, index + count) - Math.Max(cell.Column, index));
                if (removed == cell.ColumnSpan)
                {
                    RemoveSubtree(id);
                    cells = cells.Remove(id);
                }
                else _nodes[id] = cell with
                {
                    Column = cell.Column >= index + count ? cell.Column - count : Math.Min(cell.Column, index),
                    ColumnSpan = cell.ColumnSpan - removed,
                };
            }
            _nodes[rowId] = row with { Cells = cells };
        }
        _nodes[tableId] = table with { Columns = table.Columns.RemoveRange(index, count) };
        Record(new RichAddressMap.TableAxis(tableId, false, index, count, 0));
    }

    private RichNodeId CreateCell(RichNodeId row, int column)
    {
        var cell = Allocate();
        var paragraph = Allocate();
        _nodes.Add(paragraph, new RichParagraph(paragraph, cell, []));
        _nodes.Add(cell, new RichTableCell(cell, row, column, 1, 1, [paragraph]));
        return cell;
    }
    private RichTableGrid Grid(RichNodeId table) => Candidate(validate: false).GetTableGrid(table);
    private IEnumerable<RichNodeId> AllCells(RichTable table) => table.Rows.SelectMany(row => Get<RichTableRow>(row).Cells).ToArray();
    private ImmutableList<RichNodeId> SortCells(IEnumerable<RichNodeId> cells) => cells.OrderBy(id => Get<RichTableCell>(id).Column).ToImmutableList();
    private static IEnumerable<RichNodeId> Cells(RichTableGrid grid, RichCellSelection selection)
    {
        var seen = new HashSet<RichNodeId>();
        for (var row = selection.Row; row < selection.Row + selection.RowCount; row++)
            for (var column = selection.Column; column < selection.Column + selection.ColumnCount; column++)
                if (seen.Add(grid[row, column])) yield return grid[row, column];
    }
    private void RemoveSubtree(RichNodeId root)
    {
        var removed = ImmutableHashSet.CreateBuilder<RichNodeId>();
        var pending = new Stack<RichNodeId>();
        pending.Push(root);
        while (pending.TryPop(out var id))
        {
            foreach (var child in RichDocumentSnapshot.Children(Get<RichNode>(id))) pending.Push(child);
            _nodes.Remove(id);
            removed.Add(id);
        }
        Record(new RichAddressMap.Remove(removed.ToImmutable()));
    }
    private static void ValidateDimensions(int rows, int columns)
    {
        if (rows <= 0 || columns <= 0 || (long)rows * columns > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(rows), "Tables require one through one million grid positions.");
    }
    private static void ValidateRemoval(int index, int count, int length)
    {
        if (index < 0 || count <= 0 || count > length - index) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
