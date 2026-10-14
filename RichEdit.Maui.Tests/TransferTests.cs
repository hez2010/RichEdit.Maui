using RichEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class TransferTests
{
    [Theory]
    [InlineData(@"{\rtf1\expnd2147483647 text}")]
    [InlineData(@"{\rtf1{\pict\pngblip\picw2147483647\pich1 00}}")]
    public Task NativeTransferReportsParserFailureAndAllowsExplicitRecovery(string rtf) => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("original text");
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var before = document.CurrentSnapshot;
        var selection = view.StructuredSession!.Selection;
        var failures = new List<Exception>();
        view.Commands.Failed += (_, args) => failures.Add(args.Exception);
        var content = new NativeEditorView.TransferContent("recovered text", rtf);
        Task<bool> Transfer() => view.InsertTransferAsync(0, () => Task.FromResult(content));

        // Direct callers can catch the original parser failure.
        await Assert.ThrowsAsync<OverflowException>(Transfer);
        Assert.Empty(failures);
        // Native callbacks use the same boundary as clipboard commands.
        await view.Commands.ExecuteClipboardAsync(Transfer);
        Assert.IsType<OverflowException>(Assert.Single(failures));
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Equal(selection, view.StructuredSession.Selection);
        Assert.False(document.CanUndo);

        // Recovery is an explicit application decision, not silent style loss.
        content = content with { Rtf = null };
        Assert.True(await Transfer());
        Assert.Equal("recovered textoriginal text", document.CurrentSnapshot.ProjectText().Text.ToString());
        view.Undo();
        Assert.Equal("original text", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(selection, view.StructuredSession.Selection);
        Assert.False(document.CanUndo);
    });

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
