using Microsoft.Maui;
using Microsoft.UI.Text;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class StructuredTableSelectionTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    public Task NativeCellIntervalsExcludeTheFollowingCellAndExpandMergedCells(bool empty, bool merged, bool reversed, bool oppositeCorners) => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId table = default;
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 4, (RichLength[])[new(1440), new(1440), new(1440), new(1440)]);
            var grid = edit.Snapshot.GetTableGrid(table);
            if (!empty)
                for (var row = 0; row < 4; row++)
                    for (var column = 0; column < 4; column++)
                        edit.ReplaceText(new(edit.Body, edit.Snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0], 0), 0, $"r{row}c{column}");
            if (merged) edit.MergeCells(new(table, 1, 1, 2, 2));
            edit.InsertParagraph(edit.Body, 2, "after");
        });
        var map = await Map(snapshot);
        var grid = snapshot.GetTableGrid(table);
        foreach (var rectangle in new[] { new RichCellSelection(table, 1, 1, 2, 2), new(table, 0, 0, 2, 2), new(table, 1, 1, 1, 1), new(table, 2, 2, 2, 2) })
        {
            var first = snapshot.Get<RichTableCell>(grid[rectangle.Row, oppositeCorners ? rectangle.Column + rectangle.ColumnCount - 1 : rectangle.Column]);
            var last = snapshot.Get<RichTableCell>(grid[rectangle.Row + rectangle.RowCount - 1, oppositeCorners ? rectangle.Column : rectangle.Column + rectangle.ColumnCount - 1]);
            var start = map.ToNative(new(snapshot.Body, first.Blocks[0], 0));
            var end = map.ToNative(new(snapshot.Body, last.Blocks[^1], snapshot.GetParagraphLength(last.Blocks[^1]))) + 1;
            var selection = map.SelectionFromNative(reversed ? new(end, start) : new(start, end));
            var expected = grid.Expand(rectangle);
            Assert.Equal(expected, selection);
            var copied = RichDocumentFragment.FromSelection(snapshot, selection).Content;
            var copiedTable = Assert.Single(copied.Nodes.Values.OfType<RichTable>());
            Assert.Equal(expected.RowCount, copied.GetTableGrid(copiedTable.Id).Rows);
            Assert.Equal(expected.ColumnCount, copied.GetTableGrid(copiedTable.Id).Columns);
            var document = new RichDocument(snapshot);
            document.Edit(edit => edit.ApplyCharacterFormat(selection, new() { Bold = true }));
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 4; column++)
                {
                    var selected = row >= expected.Row && row < expected.Row + expected.RowCount && column >= expected.Column && column < expected.Column + expected.ColumnCount;
                    var formats = document.CurrentSnapshot.GetSelectedCharacterFormats(new RichCellSelection(table, row, column, 1, 1));
                    Assert.All(formats, format => Assert.Equal(selected, format.Bold));
                }
        }
    });

    [Fact]
    public Task SelectionFromNestedContentFindsTheSharedOuterTable() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId outer = default, first = default, last = default;
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            outer = edit.InsertTable(edit.Body, 1, 3, (RichLength[])[new(1440), new(1440), new(1440)]);
            var grid = edit.Snapshot.GetTableGrid(outer);
            var nested = edit.InsertTable(grid[1, 1], 1, 1, (RichLength[])[new(1000)]);
            first = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(nested)[0, 0]).Blocks[0];
            last = edit.Snapshot.Get<RichTableCell>(grid[2, 2]).Blocks[0];
            edit.ReplaceText(new(edit.Body, first, 0), 0, "nested");
            edit.ReplaceText(new(edit.Body, last, 0), 0, "last");
            edit.InsertParagraph(edit.Body, 2, "after");
        });
        var map = await Map(snapshot);
        var start = map.ToNative(new(snapshot.Body, first, 1));
        var end = map.ToNative(new(snapshot.Body, last, 4)) + 1;
        Assert.Equal(new RichCellSelection(outer, 1, 1, 2, 2), map.SelectionFromNative(new(start, end)));
        Assert.Equal(new RichCellSelection(outer, 1, 1, 2, 2), map.SelectionFromNative(new(end, start)));
    });

    private static async Task<StructuredRichNativeMap> Map(RichDocumentSnapshot snapshot)
    {
        var prepared = await StructuredRichNativeMap.PrepareAsync(snapshot);
        var native = new Microsoft.UI.Xaml.Controls.RichEditBox();
        native.Document.SetText(TextSetOptions.FormatRtf, prepared.Rtf);
        var story = native.Document.GetRange(0, 0);
        story.SetRange(0, story.StoryLength);
        return prepared.Resolve(story.Text).Map;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 3)]
    public Task NativeCellSelectionCanGrowAndShrinkAcrossRowsAndColumns(int anchorRow, int anchorColumn) => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "before");
            table = edit.InsertTable(edit.Body, 1, 4, (RichLength[])[new(1440), new(1440), new(1440), new(1440)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(table);
            for (var row = 0; row < grid.Rows; row++)
                for (var column = 0; column < grid.Columns; column++)
                    edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0], 0), 0, $"r{row}c{column}");
            edit.InsertParagraph(edit.Body, 2, "after");
        }));
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        window.Content = handler.ContainerView ?? handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            var native = handler.PlatformView.Document;
            var story = native.GetRange(0, 0);
            story.SetRange(0, story.StoryLength);
            var text = story.Text;
            var anchor = text.IndexOf($"r{anchorRow}c{anchorColumn}", StringComparison.Ordinal) + 1;
            native.Selection.SetRange(anchor, anchor);
            await Task.Delay(30);
            native.GetText(TextGetOptions.FormatRtf, out var beforeRtf);
            var baseline = new Microsoft.UI.Xaml.Controls.RichEditBox();
            baseline.Document.SetText(TextSetOptions.FormatRtf, beforeRtf);
            var before = document.CurrentSnapshot;
            var rowStep = anchorRow == 0 ? 1 : -1;
            var columnStep = anchorColumn == 0 ? 1 : -1;
            foreach (var (rowDistance, columnDistance) in new[] { (0, 1), (1, 1), (2, 2), (3, 3), (1, 2), (1, 1), (0, 1), (0, 0) })
            {
                var row = anchorRow + rowStep * rowDistance;
                var column = anchorColumn + columnStep * columnDistance;
                var active = text.IndexOf($"r{row}c{column}", StringComparison.Ordinal) + 2;
                var start = Math.Min(anchor, active);
                var end = Math.Max(anchor, active);
                baseline.Document.Selection.SetRange(start, end);
                baseline.Document.Selection.Options = active < anchor ? baseline.Document.Selection.Options | SelectionOptions.StartActive : baseline.Document.Selection.Options & ~SelectionOptions.StartActive;
                native.Selection.SetRange(start, end);
                native.Selection.Options = active < anchor ? native.Selection.Options | SelectionOptions.StartActive : native.Selection.Options & ~SelectionOptions.StartActive;
                await Task.Delay(30);

                Assert.Equal(baseline.Document.Selection.StartPosition, native.Selection.StartPosition);
                Assert.Equal(baseline.Document.Selection.EndPosition, native.Selection.EndPosition);
                Assert.Equal(baseline.Document.Selection.Options, native.Selection.Options);
                if (rowDistance == 0 && columnDistance == 0)
                    Assert.IsType<RichEdit.Core.RichTextSelection>(view.StructuredSession!.Selection);
                else
                {
                    // RichEdit's SetRange API expands a cross-row range to full
                    // rows. Keep that native behavior and its live endpoints.
                    var expected = rowDistance == 0
                        ? new RichCellSelection(table, row, Math.Min(anchorColumn, column), 1, columnDistance + 1)
                        : new RichCellSelection(table, Math.Min(anchorRow, row), 0, rowDistance + 1, 4);
                    Assert.Equal(expected, view.StructuredSession!.Selection);
                }
                native.GetText(TextGetOptions.FormatRtf, out var afterRtf);
                Assert.Equal(beforeRtf, afterRtf);
                Assert.Same(before, document.CurrentSnapshot);
                Assert.False(document.CanUndo);
            }
        }
        finally
        {
            window.Content = null;
            view.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
            window.AppWindow.Hide();
        }
    });
}
