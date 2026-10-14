using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private readonly List<TableFrame> _tables = [];
    private readonly Stack<ListFrame> _listStack = [];
    private readonly Dictionary<int, ListDefinition> _listDefinitions = [];
    private readonly Dictionary<int, ListOverrideDefinition> _listOverrides = [];
    private readonly Dictionary<(int Number, int Level), long> _nextListNumbers = [];

    private sealed class Table(RichNodeId id) : Block(id) { internal readonly List<Row> Rows = []; }
    private sealed class Row
    {
        internal readonly List<CellSpec> Definitions = [];
        internal readonly List<List<Block>> Cells = [];
        internal bool Complete;
        internal int Left;
        internal int CellGap;
        internal RichCellPadding Padding;
        internal RichBorderSides PaddingUnits;
    }
    private sealed record CellSpec(int Boundary, bool HorizontalContinue, bool VerticalContinue, RichCellFormat Format, RichBorderSides PaddingUnits);
    private sealed class TableFrame(int level, Table table, List<Block> parent)
    {
        internal int Level = level;
        internal Table Table = table;
        internal List<Block> Parent = parent;
        internal Row? Row;
        internal bool HorizontalContinue, VerticalContinue;
        internal RichCellFormat CellFormat = new();
        internal RichBorderSides CellBorderSide;
        internal RichBorderSides CellPaddingUnits;
        internal int ListDepth;
    }
    private sealed class ListBlock(RichNodeId id, int number, bool numbered, int start, RichListNumberingStyle numberStyle) : Block(id)
    {
        internal int Number = number;
        internal bool Numbered = numbered;
        internal int Start = start;
        internal RichListNumberingStyle NumberStyle = numberStyle;
        internal readonly List<(RichNodeId Id, List<Block> Blocks)> Items = [];
    }
    private sealed record ListFrame(ListBlock List, List<Block> Parent, bool External = false, int Level = 0);
    private sealed class ListDefinition
    {
        internal readonly List<ListLevelDefinition> Levels = [];
        internal readonly ListLevelDefinition Default = new();
        internal ListLevelDefinition Level(int level) => level >= 0 && level < Levels.Count ? Levels[level] : Default;
    }
    private sealed class ListLevelDefinition
    {
        internal bool Numbered, NoNumber, ModernNumberFormat;
        internal int Start = 1;
        internal RichListNumberingStyle NumberStyle;
    }
    private sealed class ListOverrideDefinition
    {
        internal int ListId;
        internal readonly List<ListLevelOverride> Levels = [];
    }
    private sealed class ListLevelOverride
    {
        internal bool StartAt, Format;
        internal readonly ListLevelDefinition Definition = new();
    }

    private ListLevelDefinition ListLevel(int number, int level)
    {
        _listOverrides.TryGetValue(number, out var overrides);
        var definition = (_listDefinitions.GetValueOrDefault(overrides?.ListId ?? number) ?? new()).Level(level);
        if (overrides is null || level >= overrides.Levels.Count) return definition;
        var entry = overrides.Levels[level];
        return new()
        {
            Numbered = entry.Format ? entry.Definition.Numbered : definition.Numbered,
            NoNumber = entry.Format ? entry.Definition.NoNumber : definition.NoNumber,
            NumberStyle = entry.Format ? entry.Definition.NumberStyle : definition.NumberStyle,
            Start = entry.StartAt ? entry.Definition.Start : definition.Start,
        };
    }

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
            frame.ListDepth = _listStack.Count;
            _tables.Add(frame);
        }
        if (frame.Row is { Complete: false } existing)
        {
            // RTF may repeat row descriptors after cell contents. WinUI needs
            // these final descriptors to retain widths, merges, and shading.
            existing.Definitions.Clear();
            existing.Left = 0; existing.CellGap = 0; existing.Padding = default; existing.PaddingUnits = RichBorderSides.None;
            frame.HorizontalContinue = frame.VerticalContinue = false;
            frame.CellFormat = new(); frame.CellBorderSide = RichBorderSides.None;
            frame.CellPaddingUnits = RichBorderSides.None;
            return;
        }
        _paragraph = null;
        ChargeResource();
        var row = new Row();
        frame.Row = row;
        frame.Table.Rows.Add(row);
        frame.HorizontalContinue = frame.VerticalContinue = false;
        frame.CellFormat = new(); frame.CellBorderSide = RichBorderSides.None;
        frame.CellPaddingUnits = RichBorderSides.None;
        _container = [];
        row.Cells.Add(_container);
    }

    private void CellBoundary(int boundary)
    {
        if (_tables.Count == 0 || _tables[^1].Row is not { } row) throw Error("A cell definition has no table row");
        var frame = _tables[^1];
        if (row.Definitions.Count > 0 && boundary <= row.Definitions[^1].Boundary)
            throw Error("Table cell boundaries must increase");
        ChargeResource();
        row.Definitions.Add(new(boundary, frame.HorizontalContinue, frame.VerticalContinue, frame.CellFormat, frame.CellPaddingUnits));
        frame.HorizontalContinue = frame.VerticalContinue = false;
        frame.CellFormat = new(); frame.CellBorderSide = RichBorderSides.None;
        frame.CellPaddingUnits = RichBorderSides.None;
    }

    private void EndCell(int level)
    {
        CloseTables(level);
        BeginInheritedRow(level);
        if (_tables.Count == 0 || _tables[^1].Level != level || _tables[^1].Row is not { Complete: false } row)
            throw Error("A cell terminator has no open row");
        _state.InTable = true;
        _state.TableLevel = level;
        if (_container.Count == 0) EnsureParagraph();
        if (_paragraph is not null) CaptureParagraphProperties();
        while (_listStack.Count > _tables[^1].ListDepth && _listStack.Peek().External) EndList();
        _paragraph = null;
        _container = [];
        row.Cells.Add(_container);
    }

    private void BeginInheritedRow(int level)
    {
        if (_tables.Count == 0 || _tables[^1].Level != level || _tables[^1].Row is not { Complete: true } previous) return;
        // A following row may reuse its predecessor's properties without
        // another \trowd. Start it before content enters the parent story.
        ChargeResource();
        var row = new Row { Left = previous.Left, CellGap = previous.CellGap, Padding = previous.Padding, PaddingUnits = previous.PaddingUnits };
        foreach (var definition in previous.Definitions) { ChargeResource(); row.Definitions.Add(definition); }
        var frame = _tables[^1];
        frame.Row = row;
        frame.Table.Rows.Add(row);
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
        if (row.Definitions[0].Boundary <= row.Left) throw Error("Table cell boundaries must follow the row's left edge");
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
        var definition = ListLevel(number, external ? level : 0);
        var start = external && definition.Numbered ? _nextListNumbers.GetValueOrDefault((number, level), definition.Start) : definition.Start;
        if (start > int.MaxValue) throw Error("List numbering exceeds the supported range");
        var list = new ListBlock(Allocate(), number, definition.Numbered, (int)start, definition.NumberStyle);
        _container.Add(list);
        _listStack.Push(new(list, _container, external, level));
    }

    private void ListItem()
    {
        if (_listStack.Count == 0) throw Error("A list item has no list");
        _paragraph = null;
        _container = [];
        var frame = _listStack.Peek();
        frame.List.Items.Add((Allocate(), _container));
        if (frame.External)
        {
            _nextListNumbers[(frame.List.Number, frame.Level)] = (long)frame.List.Start + frame.List.Items.Count;
            foreach (var key in _nextListNumbers.Keys.Where(key => key.Number == frame.List.Number && key.Level > frame.Level).ToArray())
                _nextListNumbers.Remove(key);
        }
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
        var number = _state.ListOverride;
        if (number != 0 && ListLevel(number, _state.ListLevel).NoNumber) number = 0;
        while (_listStack.TryPeek(out var frame) && frame.External &&
            (_tables.Count == 0 || _listStack.Count > _tables[^1].ListDepth) &&
            (number == 0 || frame.Level > _state.ListLevel || frame.Level == _state.ListLevel && frame.List.Number != number)) EndList();
        if (number == 0) return;
        if (!_listStack.TryPeek(out var active) || active.Level != _state.ListLevel || active.List.Number != number ||
            _tables.Count != 0 && _listStack.Count == _tables[^1].ListDepth)
            BeginList(number, external: true, _state.ListLevel);
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
                    nodes.Add(list.Id, new RichList(list.Id, parent, list.Items.Select(item => item.Id).ToImmutableList()) { Numbered = list.Numbered, StartNumber = list.Start, NumberStyle = list.NumberStyle });
                    break;
                case Table table: MaterializeTable(table, parent, nodes); break;
            }
        }
        return result.ToImmutable();
    }

    private void MaterializeTable(Table table, RichNodeId parent, ImmutableDictionary<RichNodeId, RichNode>.Builder nodes)
    {
        var boundaries = table.Rows.SelectMany(row => row.Definitions.Select(cell => Boundary(row, cell))).Distinct().Order().ToArray();
        if (table.Rows.Any(row => row.Left != 0))
            Report("table-row-offset", "Table row offsets were normalized to a common origin while retaining cell widths.");
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
                var format = ResolveCellFormat(row, definition);
                var endColumn = Array.BinarySearch(boundaries, Boundary(row, definition)) + 1;
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
                        mergedEnd = Array.BinarySearch(boundaries, Boundary(row, row.Definitions[next])) + 1;
                    if (cell.Column != column || cell.ColumnSpan != mergedEnd - column) throw Error("Vertical table merges must have matching columns");
                    nodes[id] = cell with { RowSpan = cell.RowSpan + 1 };
                    RetainMergedContent(id, row.Cells[cellIndex]);
                }
                else
                {
                    if (definition.HorizontalContinue || definition.VerticalContinue) throw Error("A continued table merge has no origin");
                    id = Allocate(); cells.Add(id);
                    nodes.Add(id, new RichTableCell(id, rowId, column, endColumn - column, 1, Materialize(row.Cells[cellIndex], id, nodes)) { Format = format });
                }
                for (; column < endColumn; column++) grid[rowIndex, column] = id;
            }
            if (column != boundaries.Length)
            {
                Report("table-row-width", "Shorter table rows were padded with an empty cell to fit the document's rectangular table grid.");
                var cellId = Allocate();
                var paragraphId = Allocate();
                nodes.Add(paragraphId, new RichParagraph(paragraphId, cellId, []));
                nodes.Add(cellId, new RichTableCell(cellId, rowId, column, boundaries.Length - column, 1, [paragraphId]));
                cells.Add(cellId);
                for (; column < boundaries.Length; column++) grid[rowIndex, column] = cellId;
            }
            nodes.Add(rowId, new RichTableRow(rowId, table.Id, cells.ToImmutable()));
        }
        nodes.Add(table.Id, new RichTable(table.Id, parent, widths, rows.ToImmutable()));

        int Boundary(Row row, CellSpec cell)
        {
            var offset = (long)cell.Boundary - row.Left;
            if (offset is <= 0 or > int.MaxValue) throw Error("Table width exceeds the supported range");
            return (int)offset;
        }

        void RetainMergedContent(RichNodeId id, List<Block> content)
        {
            if (content.All(block => block is Paragraph { Length: 0 })) return;
            Report("merged-cell-content", "Content in continued merged cells was appended to the owning cell.");
            var cell = (RichTableCell)nodes[id];
            nodes[id] = cell with { Blocks = cell.Blocks.AddRange(Materialize(content, id, nodes)) };
        }
    }
}
