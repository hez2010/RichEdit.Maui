using System.Collections.Immutable;
using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichStructureTests
{
    [Fact]
    public void MergedCellRectanglesExpandToWholeIntersectedCells()
    {
        var snapshot = CreateMergedTable();
        var grid = snapshot.GetTableGrid(new(2));
        Assert.Equal(2, grid.Rows);
        Assert.Equal(3, grid.Columns);
        Assert.Equal(grid[0, 0], grid[1, 0]);
        Assert.Equal(grid[0, 1], grid[0, 2]);
        Assert.Equal(new RichCellSelection(new(2), 0, 0, 2, 3), grid.Expand(new(new(2), 1, 0, 1, 2)));
        Assert.Throws<ArgumentException>(() => grid.Expand(new(new(2), 1, 0, 2, 1)));
    }

    [Fact]
    public void ProjectionKeepsStructuralSeparatorsDistinctFromRichAddresses()
    {
        var snapshot = CreateMergedTable();
        var projection = snapshot.ProjectText();
        Assert.Same(snapshot, projection.Snapshot);
        Assert.Equal("A\tB\t\n\tC\tD", projection.Text.ToString());
        Assert.Equal(5, projection.Separators.Length);
        Assert.Contains(projection.Separators, separator => separator.Kind == RichSeparatorKind.Row);
        var c = projection.Paragraphs[2];
        var point = new RichTextPoint(snapshot.Body, c.Paragraph, 1);
        Assert.Equal(7, projection.GetOffset(point));
        Assert.Equal(point, projection.GetPoint(7));
        Assert.Null(projection.GetPoint(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => projection.GetOffset(point with { Offset = 2 }));
        Assert.Throws<ArgumentException>(() => snapshot.ValidatePoint(point with { Story = new(2) }));
    }

    [Fact]
    public void InvalidGridSpansAndRepeatedStructuralChildrenAreRejected()
    {
        var snapshot = CreateMergedTable();
        var first = snapshot.GetTableGrid(new(2))[0, 0];
        var cell = snapshot.Get<RichTableCell>(first);
        Assert.Throws<ArgumentException>(() => new RichDocumentSnapshot(snapshot.Body,
            snapshot.Nodes.SetItem(first, cell with { ColumnSpan = 2 }), snapshot.Styles));
        Assert.Throws<ArgumentException>(() => new RichDocumentSnapshot(snapshot.Body,
            snapshot.Nodes.SetItem(first, cell with { RowSpan = 1 }), snapshot.Styles));
        var story = snapshot.Get<RichStory>(snapshot.Body);
        Assert.Throws<ArgumentException>(() => new RichDocumentSnapshot(snapshot.Body,
            snapshot.Nodes.SetItem(story.Id, story with { Blocks = story.Blocks.Add(new(2)) }), snapshot.Styles));
    }

    [Fact]
    public void ParagraphPointsCountUtf16AndExplicitSoftBreaks()
    {
        RichNode[] nodes = [new RichStory(new(1), [new(2)]), new RichParagraph(new(2), new(1), [new(3), new(4), new(5)]),
            new RichText(new(3), new(2), TextSnapshot.FromText("😀")), new RichSoftBreak(new(4), new(2)),
            new RichText(new(5), new(2), TextSnapshot.FromText("e\u0301"))];
        var snapshot = new RichDocumentSnapshot(new(1), nodes.ToImmutableDictionary(node => node.Id), RichStyleSheet.Empty);
        Assert.Equal(5, snapshot.GetParagraphLength(new(2)));
        Assert.Equal("😀\u2028e\u0301", snapshot.ProjectText().Text.ToString());
        snapshot.ValidatePoint(new(new(1), new(2), 1)); // Raw UTF-16 APIs do not silently expand graphemes.
        Assert.Throws<ArgumentException>(() => new RichDocumentSnapshot(new(1),
            snapshot.Nodes.SetItem(new(3), new RichText(new(3), new(2), TextSnapshot.FromText("invalid\nparagraph"))), RichStyleSheet.Empty));
    }

    private static RichDocumentSnapshot CreateMergedTable()
    {
        var nodes = new Dictionary<RichNodeId, RichNode>();
        void Add(RichNode node) => nodes.Add(node.Id, node);
        Add(new RichStory(new(1), [new(2)]));
        Add(new RichTable(new(2), new(1), [new(1000), new(1000), new(1000)], [new(3), new(4)]));
        Add(new RichTableRow(new(3), new(2), [new(5), new(8)]));
        Add(new RichTableRow(new(4), new(2), [new(11), new(14)]));
        Cell(5, 3, 0, 1, 2, "A");
        Cell(8, 3, 1, 2, 1, "B");
        Cell(11, 4, 1, 1, 1, "C");
        Cell(14, 4, 2, 1, 1, "D");
        return new(new(1), nodes.ToImmutableDictionary(), RichStyleSheet.Empty);

        void Cell(int id, int row, int column, int columns, int rows, string value)
        {
            Add(new RichTableCell(new(id), new(row), column, columns, rows, [new(id + 1)]));
            Add(new RichParagraph(new(id + 1), new(id), [new(id + 2)]));
            Add(new RichText(new(id + 2), new(id + 1), TextSnapshot.FromText(value)));
        }
    }
}
