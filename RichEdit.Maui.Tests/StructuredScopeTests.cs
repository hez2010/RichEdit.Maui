using Microsoft.Maui;
using Microsoft.UI.Text;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class StructuredScopeTests
{
    [Fact]
    public Task NativeCellViewsEditOneSharedRootAndSurviveScopeRemovalAndUndo() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId table = default, leftCell = default, rightCell = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 1, (RichLength[])[new(1440), new(1440)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(table);
            leftCell = grid[0, 0]; rightCell = grid[0, 1];
            edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(leftCell).Blocks[0], 0), 0, "left");
            edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(rightCell).Blocks[0], 0), 0, "right");
        }));
        var left = new NativeEditorView();
        var peer = new NativeEditorView();
        var right = new NativeEditorView();
        left.BindStructuredDocument(document, leftCell);
        peer.BindStructuredDocument(document, leftCell);
        right.BindStructuredDocument(document, rightCell);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        left.Handler = handler;
        try
        {
            Assert.Same(document, left.StructuredSession!.Document);
            Assert.Same(document, right.StructuredSession!.Document);
            left.SelectedRange = new(4, 0);
            handler.PlatformView.Document.Selection.TypeText("X\rY");
            await Task.Delay(30);
            Assert.Equal("leftX\nY", left.Document.Text);
            Assert.Equal(left.Document.Text, peer.Document.Text);
            Assert.Equal("right", right.Document.Text);
            Assert.Equal("leftX\nY", document.CurrentSnapshot.ProjectText(leftCell).Text.ToString());
            peer.Undo();
            Assert.Equal("left", left.Document.Text);
            Assert.Equal(new(4, 0), peer.SelectedRange);
            document.Edit(edit => edit.DeleteBlock(table));
            var deleted = document.CurrentSnapshot;
            Assert.Equal("", left.Document.Text);
            Assert.Null(left.Document.StructuredProjection);
            handler.PlatformView.Document.Selection.TypeText("orphan");
            Assert.Same(deleted, document.CurrentSnapshot);
            handler.PlatformView.Document.GetText(TextGetOptions.None, out var native);
            Assert.Equal("\r", native);
            document.Undo();
            Assert.Equal("left", left.Document.Text);
            Assert.Equal("right", right.Document.Text);
            Assert.Same(document.CurrentSnapshot, left.Document.StructuredProjection!.Snapshot);
        }
        finally { left.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });
}
