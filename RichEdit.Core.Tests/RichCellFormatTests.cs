using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichCellFormatTests
{
    [Fact]
    public async Task CellPaddingOverridesRowDefaultsWithOrderIndependentUnits()
    {
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(@"{\rtf1\trowd\trgaph90\trpaddt60\trpaddft3\clpadl120\clpadfl3\clpadr1000\clpadfr0\cellx2000\cellx4000\intbl a\cell b\cell\row}"));
        var imported = await RtfCodec.ReadAsync(stream, new() { RequireLossless = true }, TestContext.Current.CancellationToken);
        var table = Assert.Single(imported.Snapshot.Nodes.Values.OfType<RichTable>());
        var grid = imported.Snapshot.GetTableGrid(table.Id);
        Assert.Equal(new RichCellPadding(new(120), new(60), new(90), new(0)), imported.Snapshot.Get<RichTableCell>(grid[0, 0]).Format.Padding);
        Assert.Equal(new RichCellPadding(new(90), new(60), new(90), new(0)), imported.Snapshot.Get<RichTableCell>(grid[0, 1]).Format.Padding);
    }
    [Fact]
    public async Task IndependentCellEdgesPaddingShadingAndAlignmentRoundTripThroughStandardRtf()
    {
        RichNodeId table = default;
        var format = new RichCellFormat
        {
            Padding = new(new(21), new(42), new(63), new(84)),
            VerticalAlignment = RichCellVerticalAlignment.Bottom,
            Shading = new(3400, new(30, 60, 90), new(240, 220, 200)),
            Borders = new(new(RichBorderStyle.Single, new(0)), new(RichBorderStyle.Double, new(25), new(200, 20, 30)),
                new(RichBorderStyle.Dotted, new(40), new(20, 150, 30)), new(RichBorderStyle.Dashed, new(15))),
        };
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, [new(1000), new(1300)]);
            edit.SetCellFormat(new(table, 0, 0, 1, 1), format);
            edit.MergeCells(new(table, 1, 0, 1, 2));
            edit.SetCellFormat(new(table, 1, 0, 1, 1), format with { VerticalAlignment = RichCellVerticalAlignment.Center });
        });
        using var stream = new MemoryStream();
        await RtfCodec.WriteAsync(snapshot, stream, TestContext.Current.CancellationToken);
        var rtf = System.Text.Encoding.ASCII.GetString(stream.ToArray());
        Assert.Contains("\\clpadl21", rtf);
        Assert.Contains("\\clbrdrt", rtf);
        Assert.Contains("\\brdrdb", rtf);
        stream.Position = 0;
        var imported = await RtfCodec.ReadAsync(stream, new() { RequireLossless = true }, TestContext.Current.CancellationToken);
        var importedTable = Assert.Single(imported.Snapshot.Nodes.Values.OfType<RichTable>());
        var grid = imported.Snapshot.GetTableGrid(importedTable.Id);
        Assert.Equal(format, imported.Snapshot.Get<RichTableCell>(grid[0, 0]).Format);
        Assert.Equal(new RichCellFormat(), imported.Snapshot.Get<RichTableCell>(grid[0, 1]).Format);
        Assert.Equal(format with { VerticalAlignment = RichCellVerticalAlignment.Center }, imported.Snapshot.Get<RichTableCell>(grid[1, 0]).Format);
        Assert.Equal(grid[1, 0], grid[1, 1]);
    }

    [Fact]
    public void CellFormattingSurvivesClearSplitCopyAndUndo()
    {
        RichNodeId table = default;
        var format = new RichCellFormat { Padding = RichCellPadding.Uniform(new(120)), Borders = RichCellBorders.Uniform(new(RichBorderStyle.Single, new(20))) };
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 1, [new(1000), new(1000)]);
            edit.SetCellFormat(new(table, 0, 0, 1, 2), format);
            edit.MergeCells(new(table, 0, 0, 1, 2));
        }));
        var cell = document.CurrentSnapshot.GetTableGrid(table)[0, 0];
        document.Edit(edit => { edit.ClearCells(new(table, 0, 0, 1, 1)); edit.SplitCell(cell); });
        Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichTableCell>(), value => Assert.Equal(format, value.Format));
        var copied = RichDocumentFragment.FromSelection(document.CurrentSnapshot, new RichCellSelection(table, 0, 0, 1, 2));
        Assert.All(copied.Content.Nodes.Values.OfType<RichTableCell>(), value => Assert.Equal(format, value.Format));
        document.Undo();
        Assert.Equal(2, document.CurrentSnapshot.Get<RichTableCell>(cell).ColumnSpan);
        Assert.Equal(format, document.CurrentSnapshot.Get<RichTableCell>(cell).Format);
        var before = document.CurrentSnapshot;
        Assert.Throws<ArgumentException>(() => document.Edit(edit => edit.SetCellFormat(new(table, 0, 0, 1, 1), new() { Padding = RichCellPadding.Uniform(new(-1)) })));
        Assert.Same(before, document.CurrentSnapshot);
    }
}
