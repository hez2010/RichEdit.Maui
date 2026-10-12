using RichEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class TransferTests
{
    [Fact]
    public Task SourceTransfersRejectStaleDestinationsAndRestoreSelectionOnUndo() => WindowsTestHost.RunAsync(async () =>
    {
        var document = new TextDocument("ab");
        var view = new NativeEditorView();
        view.BindSourceDocument(document);
        view.SelectedRange = new(0, 1);
        var incoming = new TaskCompletionSource<NativeEditorView.TransferContent>();
        var pending = view.InsertTransferAsync(2, () => incoming.Task);
        document.Edit(edit => edit.Insert(0, "X"));
        incoming.SetResult(new("stale", null));
        Assert.False(await pending);
        Assert.Equal("Xab", document.Text);
        var before = view.Selections;
        Assert.True(await view.InsertTransferAsync(3, () => Task.FromResult(new NativeEditorView.TransferContent("\r\n日本", null))));
        Assert.Equal("Xab\r\n日本", document.Text);
        view.Undo();
        Assert.Equal("Xab", document.Text);
        Assert.Equal(before, view.Selections);
    });

    [Fact]
    public Task TransferUsesPasteCancellationAndRejectsTheWholeLengthExcess() => WindowsTestHost.RunAsync(async () =>
    {
        var document = new TextDocument("ab");
        var view = new NativeEditorView();
        view.BindSourceDocument(document);
        view.MaxLength = 3;
        Assert.False(await view.InsertTransferAsync(1, () => Task.FromResult(new NativeEditorView.TransferContent("long", null))));
        view.SourcePasting += (_, args) => args.Cancel = true;
        Assert.False(await view.InsertTransferAsync(1, () => Task.FromResult(new NativeEditorView.TransferContent("x", null))));
        Assert.Equal("ab", document.Text);
        Assert.False(document.CanUndo);
    });

    [Fact]
    public Task RichTransferCarriesTableStructureAndFormattingThroughRtf() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(1440), new(1440)]);
            var paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "drag 日本語");
            edit.ApplyCharacterFormat(new RichCellSelection(table, 0, 0, 2, 2), new() { Bold = true });
            edit.DeleteBlock(new(2));
        }));
        var source = new NativeEditorView();
        source.BindStructuredDocument(document);
        source.StructuredSession!.Selection = new RichCellSelection(table, 0, 0, 2, 2);
        var transfer = (await source.CaptureTransferAsync())!;
        Assert.Contains(@"\trowd", transfer.Rtf);
        Assert.Contains("drag 日本語", transfer.Text);
        var destination = new RichDocument();
        var target = new NativeEditorView();
        target.BindStructuredDocument(destination);
        Assert.True(await target.InsertTransferAsync(0, () => Task.FromResult(transfer)));
        var copied = Assert.Single(destination.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
        Assert.Equal(2, destination.CurrentSnapshot.GetTableGrid(copied.Id).Columns);
        var text = Assert.Single(destination.CurrentSnapshot.Nodes.Values.OfType<RichText>());
        Assert.Equal("drag 日本語", text.Content.ToString());
        Assert.True(destination.CurrentSnapshot.ResolveCharacter(text.Id).Bold);
        target.Undo();
        Assert.Empty(destination.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
        Assert.False(destination.CanUndo);
    });
}
