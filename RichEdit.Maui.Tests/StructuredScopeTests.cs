using Microsoft.Maui;
using Microsoft.UI.Text;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class StructuredScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmptyNativeFlowBetweenTablesMaterializesParagraphsInTheParentStory(bool coordinated) => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId first = default, second = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            first = edit.InsertTable(edit.Body, 1, 1, (RichLength[])[new(1440)]);
            second = edit.InsertTable(edit.Body, 2, 1, (RichLength[])[new(1440)]);
            foreach (var pair in new[] { (first, "L"), (second, "R") })
            {
                var paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(pair.Item1)[0, 0]).Blocks[0];
                edit.ReplaceText(new(edit.Body, paragraph, 0), 0, pair.Item2);
            }
            edit.DeleteBlock(new(2));
        }));
        var scope = new RichTextViewScope(document.CurrentSnapshot.Body, first, second);
        var projected = scope.Project(document.CurrentSnapshot)!;
        Assert.Empty(projected.Blocks);
        Assert.Null(projected.GetPoint(0));
        var root = new NativeEditorView(); root.BindStructuredDocument(document);
        var view = new NativeEditorView(); view.BindStructuredScope(document, scope, coordinated ? root : null);
        var boundary = scope.StartSelection(projected);
        view.StructuredSession!.Selection = boundary;
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        try
        {
            handler.PlatformView.Document.Selection.TypeText("X\rY");
            await Task.Delay(30);
            Assert.Equal("X\nY", view.Document.Text);
            Assert.Equal("L\nX\nY\nR", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(4, document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks.Count);
            view.Undo();
            Assert.Equal(boundary, view.StructuredSession.Selection);
            Assert.Equal("L\nR", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal("", view.Document.Text);
            Assert.False(document.CanUndo);
            if (coordinated)
            {
                var composing = typeof(RichEditorHandler).GetField("_isComposing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var observe = typeof(RichEditorHandler).GetMethod("UpdateCompositionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                try
                {
                    var before = document.CurrentSnapshot;
                    composing.SetValue(handler, true); observe.Invoke(handler, null);
                    handler.PlatformView.Document.Selection.TypeText("仮\r名");
                    await Task.Delay(30);
                    Assert.Same(before, document.CurrentSnapshot);
                    Assert.Equal("L\n仮\n名\nR", root.StructuredSession!.Composition!.Snapshot.ProjectText().Text.ToString());
                    composing.SetValue(handler, false); observe.Invoke(handler, null);
                    Assert.Equal("L\n仮\n名\nR", document.CurrentSnapshot.ProjectText().Text.ToString());
                    root.Undo();
                    Assert.Equal(boundary, root.StructuredSession.Selection);
                    root.SelectAll();
                    before = document.CurrentSnapshot;
                    composing.SetValue(handler, true); observe.Invoke(handler, null);
                    handler.PlatformView.Document.Selection.TypeText("whole\rstory");
                    await Task.Delay(30);
                    Assert.Same(before, document.CurrentSnapshot);
                    Assert.Equal("whole\nstory", root.StructuredSession.Composition!.Snapshot.ProjectText().Text.ToString());
                    composing.SetValue(handler, false); observe.Invoke(handler, null);
                    Assert.Equal("whole\nstory", document.CurrentSnapshot.ProjectText().Text.ToString());
                    Assert.Empty(document.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
                    root.Undo();
                    Assert.Equal("L\nR", document.CurrentSnapshot.ProjectText().Text.ToString());
                    Assert.IsType<RichBlockSelection>(root.StructuredSession.Selection);
                }
                finally { composing.SetValue(handler, false); view.DiscardComposition(); }
            }
        }
        finally { view.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task CoordinatedCellInputEditsEveryCaretWithOneSessionAndOneHistoryUnit() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId leftCell = default, rightCell = default, leftParagraph = default, rightParagraph = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            var table = edit.InsertTable(edit.Body, 1, 1, (RichLength[])[new(1440), new(1440)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(table);
            leftCell = grid[0, 0]; rightCell = grid[0, 1];
            leftParagraph = snapshot.Get<RichTableCell>(leftCell).Blocks[0];
            rightParagraph = snapshot.Get<RichTableCell>(rightCell).Blocks[0];
            edit.ReplaceText(new(edit.Body, leftParagraph, 0), 0, "aa");
            edit.ReplaceText(new(edit.Body, rightParagraph, 0), 0, "bb");
        }));
        var root = new NativeEditorView(); root.BindStructuredDocument(document);
        var left = new NativeEditorView(); left.BindStructuredDocument(document, leftCell, root);
        var right = new NativeEditorView(); right.BindStructuredDocument(document, rightCell, root);
        var leftHandler = new RichEditorHandler(); var rightHandler = new RichEditorHandler();
        var context = new MauiContext(WindowsTestHost.MauiApp.Services);
        leftHandler.SetMauiContext(context); rightHandler.SetMauiContext(context);
        left.Handler = leftHandler; right.Handler = rightHandler;
        try
        {
            Assert.Same(root.StructuredSession, left.StructuredSession);
            Assert.Same(root.StructuredSession, right.StructuredSession);
            var set = new RichTextSelectionSet(document.CurrentSnapshot, (RichTextSelectionItem[])[
                new(new(new(document.CurrentSnapshot.Body, leftParagraph, 1), new(document.CurrentSnapshot.Body, leftParagraph, 1))),
                new(new(new(document.CurrentSnapshot.Body, rightParagraph, 1), new(document.CurrentSnapshot.Body, rightParagraph, 1)))], 1);
            root.StructuredSession!.Selection = set;
            var changes = 0;
            document.Changed += (_, _) => changes++;
            rightHandler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(30);
            Assert.Equal("aXa", left.Document.Text);
            Assert.Equal("bXb", right.Document.Text);
            leftHandler.PlatformView.Document.GetText(TextGetOptions.None, out var nativeLeft);
            rightHandler.PlatformView.Document.GetText(TextGetOptions.None, out var nativeRight);
            Assert.Equal("aXa\r", nativeLeft);
            Assert.Equal("bXb\r", nativeRight);
            Assert.Equal(1, changes);
            Assert.Equal(2, Assert.IsType<RichTextSelectionSet>(root.StructuredSession.Selection).Count);
            root.Undo();
            Assert.Equal(set, root.StructuredSession.Selection);
            Assert.Equal("aa", left.Document.Text);
            Assert.Equal("bb", right.Document.Text);
            Assert.False(document.CanUndo);
            root.MaxLength = document.CurrentSnapshot.ProjectText().Text.Length + 1;
            var limited = document.CurrentSnapshot;
            rightHandler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(30);
            Assert.Same(limited, document.CurrentSnapshot);
            rightHandler.PlatformView.Document.GetText(TextGetOptions.None, out var rejectedNative);
            Assert.Equal("bb\r", rejectedNative);
            root.MaxLength = -1;
            var composing = typeof(RichEditorHandler).GetField("_isComposing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var observe = typeof(RichEditorHandler).GetMethod("UpdateCompositionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                var before = document.CurrentSnapshot;
                composing.SetValue(rightHandler, true); observe.Invoke(rightHandler, null);
                rightHandler.PlatformView.Document.Selection.TypeText("仮");
                await Task.Delay(30);
                Assert.Same(before, document.CurrentSnapshot);
                Assert.True(root.Composition.IsActive);
                Assert.Same(root.StructuredSession.Composition, right.StructuredSession!.Composition);
                Assert.Equal("b仮b", root.StructuredSession.Composition!.Snapshot.ProjectText(rightCell).Text.ToString());
                Assert.Equal("aa", root.StructuredSession.Composition.Snapshot.ProjectText(leftCell).Text.ToString());
                left.Handler = null;
                Assert.NotNull(root.StructuredSession.Composition);
                composing.SetValue(rightHandler, false); observe.Invoke(rightHandler, null);
                Assert.Equal("a仮a", document.CurrentSnapshot.ProjectText(leftCell).Text.ToString());
                Assert.Equal("b仮b", document.CurrentSnapshot.ProjectText(rightCell).Text.ToString());
                root.Undo();
                Assert.Equal(set, root.StructuredSession.Selection);
                Assert.False(document.CanUndo);
            }
            finally { composing.SetValue(rightHandler, false); right.DiscardComposition(); }
            right.Document = new RichTextDocument();
            Assert.True(root.StructuredSession.ReplaceSelection("Y").Accepted);
            Assert.Equal("aYa", left.Document.Text);
        }
        finally
        {
            left.Handler = null; right.Handler = null;
            ((IElementHandler)leftHandler).DisconnectHandler(); ((IElementHandler)rightHandler).DisconnectHandler();
        }
    });

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
