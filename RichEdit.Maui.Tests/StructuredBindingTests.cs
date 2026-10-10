using RichEdit.Core;
using Microsoft.Maui;
using Microsoft.UI.Text;

namespace RichEdit.Maui.Tests;

public sealed class StructuredBindingTests
{
    private static Microsoft.UI.Xaml.Window? _window;
    [Fact]
    public Task NativeTablesRemainStructuredThroughCellTypingUndoAndBoundaryInsertion() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId table = default, paragraph = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(1440), new(1440)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(table);
            var labels = new[] { "alpha", "beta", "gamma", "delta" };
            for (var row = 0; row < 2; row++)
                for (var column = 0; column < 2; column++)
                {
                    var cellParagraph = snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0];
                    edit.ReplaceText(new(edit.Body, cellParagraph, 0), 0, labels[row * 2 + column]);
                    if (row == 0 && column == 1) paragraph = cellParagraph;
                }
            edit.DeleteBlock(new(2));
        }));
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        window.Content = handler.ContainerView ?? handler.PlatformView;
        window.Activate();
        try
        {
            await Task.Delay(100);
            view.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(new(document.CurrentSnapshot.Body, paragraph, 0), new(document.CurrentSnapshot.Body, paragraph, 4));
            handler.PlatformView.Document.Selection.TypeText("BETA");
            await Task.Delay(30);
            Assert.Equal("alpha\tBETA\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Rows);
            handler.PlatformView.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
            Assert.Equal(2, rtf.Split(@"\trowd", StringSplitOptions.None).Length - 1);
            view.Undo();
            Assert.Equal("alpha\tbeta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
            view.StructuredSession.Selection = new RichCellSelection(table, 0, 0, 2, 1);
            view.Selection.ToggleBold();
            Assert.True(view.Selection.CharacterFormat.Bold);
            Assert.IsType<RichCellSelection>(view.StructuredSession.Selection);
            foreach (var run in document.CurrentSnapshot.Nodes.Values.OfType<RichText>())
                Assert.Equal(run.Content.ToString() is "alpha" or "gamma", document.CurrentSnapshot.ResolveCharacter(run.Id).Bold);
            view.Undo();
            view.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(new(document.CurrentSnapshot.Body, paragraph, 1), new(document.CurrentSnapshot.Body, paragraph, 3));
            handler.PlatformView.Document.Selection.CharacterFormat.Italic = FormatEffect.On;
            await Task.Delay(30);
            var italic = document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Where(run => document.CurrentSnapshot.ResolveCharacter(run.Id).Italic).ToArray();
            Assert.Single(italic);
            Assert.Equal("et", italic[0].Content.ToString());
            view.Undo();
            Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>(), run => Assert.False(document.CurrentSnapshot.ResolveCharacter(run.Id).Italic));
            handler.PlatformView.Document.Selection.EndKey(TextRangeUnit.Story, false);
            await Task.Delay(30);
            Assert.True(view.StructuredSession.Selection is RichBoundarySelection,
                $"Native {handler.PlatformView.Document.Selection.StartPosition}:{handler.PlatformView.Document.Selection.EndPosition}; core {view.StructuredSession.Selection}; source {view.SelectionState}; mapped {handler.NativeProjection.IsStructured}");
            handler.PlatformView.Document.Selection.TypeText("tail");
            await Task.Delay(30);
            Assert.Equal("alpha\tbeta\ngamma\tdelta\ntail", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks.Count);
            view.Undo();
            Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Rows);
            Assert.IsType<RichBoundarySelection>(view.StructuredSession.Selection);

            view.StructuredSession.Selection = new RichCellSelection(table, 0, 0, 1, 2);
            Assert.IsType<RichCellSelection>(view.StructuredSession.Selection);
            handler.PlatformView.Document.GetText(TextGetOptions.FormatRtf, out var selectedRtf);
            Assert.Contains(@"\clcbpat", selectedRtf);
            Assert.Contains(@"\cellx1440", selectedRtf);
            using var authored = new MemoryStream();
            await RichEdit.Core.Rtf.RtfCodec.WriteAsync(document.CurrentSnapshot, authored);
            Assert.DoesNotContain(@"\clcbpat", System.Text.Encoding.ASCII.GetString(authored.ToArray()));
            Assert.True(view.SendKeyDown(EditorKey.Delete, EditorKeyModifiers.None));
            await Task.Delay(30);
            Assert.Equal("\t\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Columns);
            view.Undo();
            Assert.IsType<RichCellSelection>(view.StructuredSession.Selection);
            Assert.Equal("alpha\tbeta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());

            var current = document.CurrentSnapshot;
            var grid = current.GetTableGrid(table);
            var alpha = current.Get<RichTableCell>(grid[0, 0]).Blocks[0];
            view.StructuredSession.Selection = new RichTextSelectionSet(current, (RichTextSelectionItem[])[
                new(new(new(current.Body, alpha, 5), new(current.Body, alpha, 5))),
                new(new(new(current.Body, paragraph, 4), new(current.Body, paragraph, 4)))], 1);
            handler.PlatformView.Document.Selection.TypeText("!");
            await Task.Delay(30);
            Assert.Equal("alpha!\tbeta!\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
            view.Undo();
            Assert.Equal("alpha\tbeta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, Assert.IsType<RichTextSelectionSet>(view.StructuredSession.Selection).Count);
            view.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(new(current.Body, paragraph, 2), new(current.Body, paragraph, 2));
            var composing = typeof(RichEditorHandler).GetField("_isComposing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var observe = typeof(RichEditorHandler).GetMethod("UpdateCompositionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                composing.SetValue(handler, true);
                observe.Invoke(handler, null);
                handler.PlatformView.Document.Selection.TypeText("仮");
                await Task.Delay(30);
                Assert.Equal("alpha\tbeta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
                Assert.Equal("alpha\tbe仮ta\ngamma\tdelta", view.StructuredSession.Composition!.Snapshot.ProjectText().Text.ToString());
                typeof(RichEditorHandler).GetMethod("ReconcileCompositionSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(handler, null);
                composing.SetValue(handler, false);
                observe.Invoke(handler, null);
                Assert.Equal("alpha\tbe仮ta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
                view.Undo();
                Assert.Equal("alpha\tbeta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
                var cells = new RichCellSelection(table, 0, 0, 1, 2);
                view.StructuredSession.Selection = cells;
                var before = document.CurrentSnapshot;
                composing.SetValue(handler, true);
                observe.Invoke(handler, null);
                handler.PlatformView.Document.Selection.TypeText("仮");
                await Task.Delay(30);
                Assert.Same(before, document.CurrentSnapshot);
                Assert.Equal("仮\t\ngamma\tdelta", view.StructuredSession.Composition!.Snapshot.ProjectText().Text.ToString());
                composing.SetValue(handler, false);
                observe.Invoke(handler, null);
                Assert.Equal("仮\t\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
                view.Undo();
                Assert.Equal(cells, view.StructuredSession.Selection);
                Assert.Equal("alpha\tbeta\ngamma\tdelta", document.CurrentSnapshot.ProjectText().Text.ToString());
            }
            finally { composing.SetValue(handler, false); view.DiscardComposition(); }
        }
        finally { window.Content = null; view.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task NativeTypingPreservesRichStylesAcrossMultipleCaretsAndSharedUndo() => WindowsTestHost.RunAsync(async () =>
    {
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            edit.SetStyles(new((RichStyleDefinition[])[new(new(1), "Strong", RichStyleKind.Paragraph) { Character = new() { Bold = true } }]));
            edit.SetParagraphStyle(new(2), new(1));
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "abcd");
        }));
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        try
        {
            view.Selections = new((TextSelection[])[new(1, 1), new(3, 3)], 1);
            handler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(30);
            Assert.Equal("aXbcXd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>(), run => Assert.Equal(new RichCharacterStyle(), run.Format));
            Assert.True(document.CurrentSnapshot.ResolveCharacter(document.CurrentSnapshot.Get<RichParagraph>(new(2)).Inlines[0]).Bold);
            view.Undo();
            Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(2, view.Selections.Count);
            Assert.Equal(1, view.Selections[0].Active);
            Assert.Equal(3, view.Selections[1].Active);
            handler.PlatformView.Document.GetText(TextGetOptions.None, out var native);
            Assert.Equal("abcd\r", native);
            view.SelectedRange = new(4, 0);
            view.StructuredSession!.TypingCharacterFormat = new() { Italic = true, Bold = false };
            handler.PlatformView.Document.Selection.TypeText("Y");
            await Task.Delay(30);
            var typed = document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Single(run => run.Content.ToString() == "Y");
            Assert.Equal(new RichCharacterStyle { Italic = true, Bold = false }, typed.Format);
        }
        finally { view.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task NativeCompositionStagesRichRootsThenPublishesOneCommit() => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("ab");
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        var composing = typeof(RichEditorHandler).GetField("_isComposing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var observe = typeof(RichEditorHandler).GetMethod("UpdateCompositionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            view.SelectedRange = new(1, 0);
            var published = 0;
            document.Changed += (_, _) => published++;
            composing.SetValue(handler, true);
            observe.Invoke(handler, null);
            handler.PlatformView.Document.Selection.TypeText("日");
            await Task.Delay(30);
            Assert.Equal("ab", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal("a日b", view.StructuredSession!.Composition!.Snapshot.ProjectText().Text.ToString());
            Assert.Equal(0, published);
            typeof(RichEditorHandler).GetMethod("ReconcileCompositionSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(handler, null);
            composing.SetValue(handler, false);
            observe.Invoke(handler, null);
            Assert.Equal("a日b", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(1, published);
            view.Undo();
            Assert.Equal("ab", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.False(document.CanUndo);
        }
        finally { composing.SetValue(handler, false); view.DiscardComposition(); view.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task NativeSurfaceUsesOneRichAuthorityAndMapsPeerCaretsBeforePublication() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abc");
        var first = new NativeEditorView();
        var second = new NativeEditorView();
        first.BindStructuredDocument(document);
        second.BindStructuredDocument(document);
        first.SelectedRange = new(1, 0);
        second.SelectedRange = new(3, 0);
        var publications = 0;
        document.Changed += (_, _) =>
        {
            publications++;
            Assert.Equal("aXbc", first.Document.Text);
            Assert.Equal("aXbc", second.Document.Text);
            Assert.Equal(new TextSpan(4, 0), second.SelectedRange);
        };
        first.ReplaceSelections("X");
        Assert.Equal(1, publications);
        Assert.Equal("aXbc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.True(document.CanUndo);
    });

    [Fact]
    public Task RichSessionSelectionAndGlobalHistoryDriveNativeSurfaceSelection() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abc");
        var first = new NativeEditorView();
        var second = new NativeEditorView();
        first.BindStructuredDocument(document);
        second.BindStructuredDocument(document);
        first.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(new(new(1), new(2), 1), new(new(1), new(2), 2));
        Assert.Equal(new TextSpan(1, 1), first.SelectedRange);
        first.ReplaceSelections("Q\nR");
        Assert.Equal("aQ\nRc", document.CurrentSnapshot.ProjectText().Text.ToString());
        second.Undo();
        Assert.Equal("abc", first.Document.Text);
        Assert.Equal(new TextSpan(1, 1), second.SelectedRange);
        Assert.False(document.CanUndo);
        second.Redo();
        Assert.Equal("aQ\nRc", second.Document.Text);
        Assert.Equal(new TextSpan(4, 0), second.SelectedRange);
    });

    [Fact]
    public Task RichCompositionFreezesCanonicalStateAndSharesTheLease() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichDocument.FromPlainText("abc");
        var view = new NativeEditorView();
        view.BindStructuredDocument(document);
        view.SelectedRange = new(1, 0);
        view.SetCompositionState(new(true, new(1, 1)));
        Assert.NotNull(view.StructuredSession!.Composition);
        Assert.Throws<InvalidOperationException>(() => document.CaptureSave());
        Assert.False(document.TryEdit(document.Revision, edit => edit.ReplaceText(new(edit.Body, new(2), 0), 0, "blocked")).Accepted);
        view.SetCompositionState(default);
        Assert.Null(view.StructuredSession.Composition);
        Assert.Equal("abc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.False(document.CanUndo);
    });
}
