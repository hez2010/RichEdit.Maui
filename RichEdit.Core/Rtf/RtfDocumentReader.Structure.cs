using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private readonly List<TableFrame> _tables = [];
    private readonly Stack<ListFrame> _listStack = [];
    private readonly Dictionary<int, ListDefinition> _listDefinitions = [];
    private readonly Dictionary<int, int> _listOverrides = [];

    private sealed class Table(RichNodeId id) : Block(id) { internal readonly List<Row> Rows = []; }
    private sealed class Row
    {
        internal readonly List<CellSpec> Definitions = [];
        internal readonly List<List<Block>> Cells = [];
        internal bool Complete;
    }
    private sealed record CellSpec(int Boundary, bool HorizontalContinue, bool VerticalContinue);
    private sealed class TableFrame(int level, Table table, List<Block> parent)
    {
        internal int Level = level;
        internal Table Table = table;
        internal List<Block> Parent = parent;
        internal Row? Row;
        internal bool HorizontalContinue, VerticalContinue;
    }
    private sealed class ListBlock(RichNodeId id, int number, bool numbered, int start) : Block(id)
    {
        internal int Number = number;
        internal bool Numbered = numbered;
        internal int Start = start;
        internal readonly List<(RichNodeId Id, List<Block> Blocks)> Items = [];
    }
    private sealed record ListFrame(ListBlock List, List<Block> Parent, bool External = false, int Level = 0);
    private sealed class ListDefinition { internal bool Numbered; internal int Start = 1; }

    private void BeginRow()
    {
        var level = Math.Max(1, _state.TableLevel);
        CloseTables(level);
        TableFrame frame;
        if (_tables.Count != 0 && _tables[^1].Level == level) frame = _tables[^1];
        else
        {
            if (_tables.Count + 1 != level) throw Error("A table nesting level has no parent cell");
            var table = new Table(Allocate());
            _container.Add(table);
            frame = new(level, table, _container);
            _tables.Add(frame);
        }
        if (frame.Row is { Complete: false } existing)
        {
            // RTF may repeat row descriptors after cell contents. WinUI needs
            // these final descriptors to retain widths, merges, and shading.
            existing.Definitions.Clear();
            frame.HorizontalContinue = frame.VerticalContinue = false;
            return;
        }
        _paragraph = null;
        ChargeResource();
        var row = new Row();
        frame.Row = row;
        frame.Table.Rows.Add(row);
        frame.HorizontalContinue = frame.VerticalContinue = false;
        _container = [];
        row.Cells.Add(_container);
    }

    private void CellBoundary(int boundary)
    {
        if (_tables.Count == 0 || _tables[^1].Row is not { } row) throw Error("A cell definition has no table row");
        var frame = _tables[^1];
        if (boundary <= 0 || row.Definitions.Count > 0 && boundary <= row.Definitions[^1].Boundary)
            throw Error("Table cell boundaries must increase");
        ChargeResource();
        row.Definitions.Add(new(boundary, frame.HorizontalContinue, frame.VerticalContinue));
        frame.HorizontalContinue = frame.VerticalContinue = false;
    }

    private void EndCell(int level)
    {
        CloseTables(level);
        if (_tables.Count == 0 || _tables[^1].Level != level || _tables[^1].Row is not { Complete: false } row)
            throw Error("A cell terminator has no open row");
        _state.InTable = true;
        _state.TableLevel = level;
        if (_container.Count == 0) EnsureParagraph();
        if (_paragraph is not null) CaptureParagraphProperties();
        _paragraph = null;
        _container = [];
        row.Cells.Add(_container);
    }

    private void EndRow(int level)
    {
        CloseTables(level);
        if (_tables.Count == 0 || _tables[^1].Row is not { Complete: false } row) throw Error("A row terminator has no open row");
        var frame = _tables[^1];
        if (row.Cells.Count == row.Definitions.Count + 1 && row.Cells[^1].Count == 0) row.Cells.RemoveAt(row.Cells.Count - 1);
        if (row.Cells.Count != row.Definitions.Count || row.Cells.Count == 0) throw Error("Table cell contents do not match the row definitions");
        row.Complete = true;
        _paragraph = null;
        _container = frame.Parent;
    }

    private void CloseTables(int keepLevel)
    {
        while (_tables.Count != 0 && _tables[^1].Level > keepLevel)
        {
            var frame = _tables[^1];
            if (frame.Row is not { Complete: true }) throw Error("Unterminated table row");
            _tables.RemoveAt(_tables.Count - 1);
            _container = frame.Parent;
            _paragraph = null;
        }
    }

    private void BeginList(int number, bool external = false, int level = 0)
    {
        _paragraph = null;
        var id = _listOverrides.GetValueOrDefault(number, number);
        var definition = _listDefinitions.GetValueOrDefault(id) ?? new();
        var list = new ListBlock(Allocate(), number, definition.Numbered, definition.Start);
        _container.Add(list);
        _listStack.Push(new(list, _container, external, level));
    }

    private void ListItem()
    {
        if (_listStack.Count == 0) throw Error("A list item has no list");
        _paragraph = null;
        _container = [];
        _listStack.Peek().List.Items.Add((Allocate(), _container));
    }

    private void EndList()
    {
        if (!_listStack.TryPop(out var frame)) throw Error("A list terminator has no list");
        if (frame.List.Items.Count == 0 || frame.List.Items.Any(item => item.Blocks.Count == 0)) throw Error("Lists must contain nonempty items");
        _container = frame.Parent;
        _paragraph = null;
    }

    private void ApplyExternalList()
    {
        while (_listStack.TryPeek(out var frame) && frame.External &&
            (_state.ListOverride == 0 || frame.Level > _state.ListLevel || frame.Level == _state.ListLevel && frame.List.Number != _state.ListOverride)) EndList();
        if (_state.ListOverride == 0) return;
        if (!_listStack.TryPeek(out var active) || active.Level != _state.ListLevel || active.List.Number != _state.ListOverride)
            BeginList(_state.ListOverride, external: true, _state.ListLevel);
        ListItem();
    }

    private ImmutableList<RichNodeId> Materialize(List<Block> blocks, RichNodeId parent, ImmutableDictionary<RichNodeId, RichNode>.Builder nodes)
    {
        var result = ImmutableList.CreateBuilder<RichNodeId>();
        foreach (var block in blocks)
        {
            _cancellation.ThrowIfCancellationRequested();
            result.Add(block.Id);
            switch (block)
            {
                case Paragraph paragraph:
                    nodes.Add(paragraph.Id, new RichParagraph(paragraph.Id, parent, paragraph.Inlines.Select(inline => inline.Id).ToImmutableList())
                    { Style = paragraph.Style, Format = paragraph.Format, Character = paragraph.Character });
                    foreach (var inline in paragraph.Inlines) nodes.Add(inline.Id, inline);
                    break;
                case ListBlock list:
                    foreach (var item in list.Items)
                        nodes.Add(item.Id, new RichListItem(item.Id, list.Id, Materialize(item.Blocks, item.Id, nodes)));
                    nodes.Add(list.Id, new RichList(list.Id, parent, list.Items.Select(item => item.Id).ToImmutableList()) { Numbered = list.Numbered, StartNumber = list.Start });
                    break;
                case Table table: MaterializeTable(table, parent, nodes); break;
            }
        }
        return result.ToImmutable();
    }

    private void MaterializeTable(Table table, RichNodeId parent, ImmutableDictionary<RichNodeId, RichNode>.Builder nodes)
    {
        var boundaries = table.Rows.SelectMany(row => row.Definitions).Select(cell => cell.Boundary).Distinct().Order().ToArray();
        if ((long)boundaries.Length * table.Rows.Count > Math.Min(options.MaximumNodes, 1_000_000)) throw Error("Table grid limit exceeded");
        var widths = boundaries.Select((value, index) => new RichLength(value - (index == 0 ? 0 : boundaries[index - 1]))).ToImmutableArray();
        var grid = new RichNodeId[table.Rows.Count, boundaries.Length];
        var rows = ImmutableList.CreateBuilder<RichNodeId>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var rowId = Allocate(); rows.Add(rowId);
            var cells = ImmutableList.CreateBuilder<RichNodeId>();
            var column = 0;
            for (var cellIndex = 0; cellIndex < row.Definitions.Count; cellIndex++)
            {
                var definition = row.Definitions[cellIndex];
                var endColumn = Array.BinarySearch(boundaries, definition.Boundary) + 1;
                RichNodeId id;
                if (definition.HorizontalContinue && column > 0)
                {
                    id = grid[rowIndex, column - 1];
                    var cell = (RichTableCell)nodes[id];
                    if (cell.Parent == rowId) nodes[id] = cell with { ColumnSpan = endColumn - cell.Column };
                    RetainMergedContent(id, row.Cells[cellIndex]);
                }
                else if (definition.VerticalContinue && rowIndex > 0)
                {
                    id = grid[rowIndex - 1, column];
                    var cell = (RichTableCell)nodes[id];
                    var mergedEnd = endColumn;
                    for (var next = cellIndex + 1; next < row.Definitions.Count && row.Definitions[next].HorizontalContinue; next++)
                        mergedEnd = Array.BinarySearch(boundaries, row.Definitions[next].Boundary) + 1;
                    if (cell.Column != column || cell.ColumnSpan != mergedEnd - column) throw Error("Vertical table merges must have matching columns");
                    nodes[id] = cell with { RowSpan = cell.RowSpan + 1 };
                    RetainMergedContent(id, row.Cells[cellIndex]);
                }
                else
                {
                    if (definition.HorizontalContinue || definition.VerticalContinue) throw Error("A continued table merge has no origin");
                    id = Allocate(); cells.Add(id);
                    nodes.Add(id, new RichTableCell(id, rowId, column, endColumn - column, 1, Materialize(row.Cells[cellIndex], id, nodes)));
                }
                for (; column < endColumn; column++) grid[rowIndex, column] = id;
            }
            if (column != boundaries.Length) throw Error("Table rows must cover the complete grid");
            nodes.Add(rowId, new RichTableRow(rowId, table.Id, cells.ToImmutable()));
        }
        nodes.Add(table.Id, new RichTable(table.Id, parent, widths, rows.ToImmutable()));

        void RetainMergedContent(RichNodeId id, List<Block> content)
        {
            if (content.All(block => block is Paragraph { Length: 0 })) return;
            Report("merged-cell-content", "Content in continued merged cells was appended to the owning cell.");
            var cell = (RichTableCell)nodes[id];
            nodes[id] = cell with { Blocks = cell.Blocks.AddRange(Materialize(content, id, nodes)) };
        }
    }
}
