using RichEdit.Core;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace RichEdit.Maui.Tests;

public sealed class StructuredClipboardTests
{
    [Fact]
    public Task RtfClipboardCarriesActualTablesAndCutRetainsTheSourceGrid() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        RichNodeId table = default;
        var source = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(1440), new(1440)]);
            var paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "copy 日本語");
            edit.ApplyCharacterFormat(new RichCellSelection(table, 0, 0, 2, 2), new() { Bold = true });
            edit.DeleteBlock(new(2));
        }));
        var view = new NativeEditorView();
        view.BindStructuredDocument(source);
        var cells = new RichCellSelection(table, 0, 0, 2, 2);
        view.StructuredSession!.Selection = cells;
        await view.CopyAsync();
        var rtf = await Clipboard.GetContent().GetRtfAsync();
        Assert.Contains(@"\trowd", rtf);
        // Replace the clipboard to force semantic RTF parsing, bypassing the
        // process-local fragment cache and its stable selection metadata.
        var external = new DataPackage();
        external.SetRtf(rtf);
        await WindowsClipboardAccess.SetAsync(external);
        var destination = new RichDocument();
        var target = new NativeEditorView();
        target.BindStructuredDocument(destination);
        await target.PasteAsync();
        Assert.Single(destination.CurrentSnapshot.Get<RichStory>(destination.CurrentSnapshot.Body).Blocks);
        var copiedTable = Assert.Single(destination.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
        Assert.Equal(2, destination.CurrentSnapshot.GetTableGrid(copiedTable.Id).Rows);
        var run = Assert.Single(destination.CurrentSnapshot.Nodes.Values.OfType<RichText>());
        Assert.Equal("copy 日本語", run.Content.ToString());
        Assert.True(destination.CurrentSnapshot.ResolveCharacter(run.Id).Bold);
        target.Undo();
        Assert.False(destination.CanUndo);
        await view.CutAsync();
        Assert.Equal("\t\n\t", source.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(2, source.CurrentSnapshot.GetTableGrid(table).Columns);
        view.Undo();
        Assert.Equal(cells, view.StructuredSession.Selection);
        Assert.Contains("copy 日本語", source.CurrentSnapshot.ProjectText().Text.ToString());
        view.SelectAll();
        Assert.IsType<RichBlockSelection>(view.StructuredSession.Selection);
        await view.CutAsync();
        Assert.Empty(source.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
        Assert.Equal("", source.CurrentSnapshot.ProjectText().Text.ToString());
        view.Undo();
        Assert.IsType<RichBlockSelection>(view.StructuredSession.Selection);
        Assert.Single(source.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
    });

    [Fact]
    public Task ClipboardDistributesRichSelectionsAndRejectsStalePasteOrExcessLength() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        var source = RichDocument.FromPlainText("ab cd");
        var view = new NativeEditorView();
        view.BindStructuredDocument(source);
        source.Edit(edit => edit.SetCharacterFormat(new(edit.Body, new(2), 0), 2, new() { Bold = true }));
        RichTextSelectionItem Range(int start, int end) => new(new(new(new(1), new(2), start), new(new(1), new(2), end)));
        view.StructuredSession!.Selection = new RichTextSelectionSet(source.CurrentSnapshot, (RichTextSelectionItem[])[Range(0, 2), Range(3, 5)]);
        await view.CopyAsync();
        var destination = RichDocument.FromPlainText("XY");
        view.BindStructuredDocument(destination);
        view.StructuredSession!.Selection = new RichTextSelectionSet(destination.CurrentSnapshot, (RichTextSelectionItem[])[Range(0, 0), Range(2, 2)]);
        await view.PasteAsync();
        Assert.Equal("abXYcd", destination.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(2, Assert.IsType<RichTextSelectionSet>(view.StructuredSession.Selection).Count);
        view.Undo();
        view.MaxLength = 3;
        await view.PasteAsync();
        Assert.Equal("XY", destination.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.False(destination.CanUndo);
        view.MaxLength = -1;
        view.StructuredPasting += (_, _) => view.StructuredSession.Selection = Range(1, 1).Selection;
        await view.PasteAsync();
        Assert.Equal("XY", destination.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.False(destination.CanUndo);
    });
}
