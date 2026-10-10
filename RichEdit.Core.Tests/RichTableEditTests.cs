using Xunit;

namespace RichEdit.Core.Tests;

public class RichTableEditTests
{
    [Fact]
    public void MergeAndSplitRetainContentAndParagraphIdentities()
    {
        var (snapshot, table) = Table(2, 2);
        var grid = snapshot.GetTableGrid(table);
        var paragraphs = new List<RichNodeId>();
        var edit = Edit(snapshot);
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
            {
                var paragraph = snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0];
                paragraphs.Add(paragraph);
                edit.ReplaceText(new(snapshot.Body, paragraph, 0), 0, $"{row}{column}");
            }
        var merged = edit.MergeCells(new(table, 0, 0, 2, 2));
        var after = edit.Finish().Snapshot;
        Assert.Equal(grid[0, 0], merged);
        Assert.Equal(paragraphs, after.Get<RichTableCell>(merged).Blocks);
        Assert.All(paragraphs, id => Assert.Equal(merged, after.Get<RichParagraph>(id).Parent));
        Assert.Equal(merged, after.GetTableGrid(table)[1, 1]);
        edit = Edit(after);
        edit.SplitCell(merged);
        var split = edit.Finish().Snapshot;
        Assert.Equal(paragraphs, split.Get<RichTableCell>(merged).Blocks);
        Assert.Equal(4, split.Nodes.Values.OfType<RichTableCell>().Count());
        Assert.Equal(2, snapshot.GetTableGrid(table).Rows);
    }

    [Fact]
    public void InsertAndDeleteThroughMergedCellsAdjustSpansAndRetainSurvivingContent()
    {
        var (snapshot, table) = Table(3, 3);
        var edit = Edit(snapshot);
        var merged = edit.MergeCells(new(table, 0, 0, 2, 2));
        edit.InsertRows(table, 1, 2);
        edit.InsertColumns(table, 1, [new(500), new(700)]);
        var expanded = edit.Finish().Snapshot;
        var cell = expanded.Get<RichTableCell>(merged);
        Assert.Equal((4, 4), (cell.RowSpan, cell.ColumnSpan));
        Assert.Equal((5, 5), (expanded.GetTableGrid(table).Rows, expanded.GetTableGrid(table).Columns));
        edit = Edit(expanded);
        edit.DeleteRows(table, 0, 2);
        edit.DeleteColumns(table, 0, 2);
        var shrunk = edit.Finish().Snapshot;
        cell = shrunk.Get<RichTableCell>(merged);
        Assert.Equal((2, 2, 0), (cell.RowSpan, cell.ColumnSpan, cell.Column));
        Assert.Equal(merged, shrunk.GetTableGrid(table)[0, 0]);
        Assert.Equal(merged, shrunk.GetTableGrid(table)[1, 1]);
    }

    [Fact]
    public void ClearingCellsPreservesTheGridAndDeletingAllRowsRemovesTheTable()
    {
        var (snapshot, table) = Table(2, 2);
        var cell = snapshot.GetTableGrid(table)[1, 1];
        var paragraph = snapshot.Get<RichTableCell>(cell).Blocks[0];
        var edit = Edit(snapshot);
        edit.ReplaceText(new(snapshot.Body, paragraph, 0), 0, "content");
        edit.ClearCells(new(table, 1, 1, 1, 1));
        var cleared = edit.Finish();
        Assert.Equal(cell, cleared.Snapshot.GetTableGrid(table)[1, 1]);
        Assert.Null(cleared.Map.Map(new(snapshot.Body, paragraph, 0)));
        Assert.NotEqual(paragraph, cleared.Snapshot.Get<RichTableCell>(cell).Blocks[0]);
        edit = Edit(cleared.Snapshot);
        edit.DeleteRows(table, 0, 2);
        var removed = edit.Finish().Snapshot;
        Assert.DoesNotContain(table, removed.Nodes.Keys);
        Assert.Single(removed.Get<RichStory>(removed.Body).Blocks);
        Assert.Equal("", removed.ProjectText().Text.ToString());
    }

    [Theory]
    [InlineData(31)]
    [InlineData(401)]
    public void RandomStructuralCommandsMaintainCompleteUnambiguousGrids(int seed)
    {
        var (snapshot, table) = Table(3, 4);
        var random = new Random(seed);
        var nextId = snapshot.Nodes.Keys.Max(id => id.Value);
        for (var step = 0; step < 250; step++)
        {
            var grid = snapshot.GetTableGrid(table);
            var edit = new RichDocumentEdit(snapshot, nextId);
            switch (random.Next(6))
            {
                case 0 when grid.Rows < 10: edit.InsertRows(table, random.Next(grid.Rows + 1)); break;
                case 1 when grid.Columns < 10: edit.InsertColumns(table, random.Next(grid.Columns + 1), [new(600)]); break;
                case 2 when grid.Rows > 1: edit.DeleteRows(table, random.Next(grid.Rows)); break;
                case 3 when grid.Columns > 1: edit.DeleteColumns(table, random.Next(grid.Columns)); break;
                case 4:
                    var row = random.Next(grid.Rows);
                    var column = random.Next(grid.Columns);
                    edit.MergeCells(new(table, row, column, random.Next(1, grid.Rows - row + 1), random.Next(1, grid.Columns - column + 1)));
                    break;
                default: edit.SplitCell(grid[random.Next(grid.Rows), random.Next(grid.Columns)]); break;
            }
            var result = edit.Finish(); // Full schema validation checks reachability, parents, spans and grid coverage.
            snapshot = result.Snapshot;
            nextId = result.NextId;
            var next = snapshot.GetTableGrid(table);
            Assert.InRange(next.Rows, 1, 10);
            Assert.InRange(next.Columns, 1, 10);
        }
    }

    private static (RichDocumentSnapshot Snapshot, RichNodeId Table) Table(int rows, int columns)
    {
        RichNodeId table = default;
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, rows, Enumerable.Repeat(new RichLength(1000), columns));
            edit.DeleteBlock(new(2));
        });
        return (snapshot, table);
    }
    private static RichDocumentEdit Edit(RichDocumentSnapshot snapshot) => new(snapshot, snapshot.Nodes.Keys.Max(id => id.Value));
}
