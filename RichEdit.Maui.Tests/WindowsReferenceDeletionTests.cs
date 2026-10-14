using Microsoft.Maui;
using Microsoft.UI.Text;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class WindowsReferenceDeletionTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public Task RichTablePasteImmediatelyBeforeTableKeepsNativeContent(int rows) => Run(async (view, handler) =>
    {
        var document = view.StructuredSession!.Document;
        var before = document.CurrentSnapshot;
        var nativeBefore = NativeText(handler);
        var blocks = before.Get<RichStory>(before.Body).Blocks;
        var table = blocks.First(id => before.Get<RichBlock>(id) is RichTable);
        var paragraph = blocks[blocks.IndexOf(table) - 1];
        var point = new RichTextPoint(before.Body, paragraph, before.GetParagraphLength(paragraph));
        view.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(point, point);
        var content = RichDocumentSnapshot.Build(edit =>
        {
            var insertedTable = edit.InsertTable(edit.Body, 1, rows, (RichLength[])[new(1494), new(1494), new(1494), new(1494), new(1494)]);
            var cells = edit.Snapshot.GetTableGrid(insertedTable);
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < 5; column++)
                {
                    var cellParagraph = edit.Snapshot.Get<RichTableCell>(cells[row, column]).Blocks[0];
                    edit.ReplaceText(new(edit.Body, cellParagraph, 0), 0, $"Pasted {row}:{column}");
                }
            edit.InsertParagraph(edit.Body, 2, "");
            edit.ApplyCharacterFormat(new RichCellSelection(insertedTable, 0, 0, rows, 5), new() { Bold = true, Italic = true });
        });
        view.StructuredSession.ReplaceSelection(new RichDocumentFragment(content));
        await Task.Delay(30);
        var nativeAfter = NativeText(handler);
        Assert.Equal(handler.NativeProjection.StructuredDisplay!.Text, nativeAfter.Replace('\r', '\n').Replace('\v', RichTextDocument.SoftLineBreakCharacter));
        Assert.Contains("Pasted 0:0", nativeAfter);
        Assert.Contains("1st column", nativeAfter);
        var inserted = nativeAfter.IndexOf("Pasted 0:0", StringComparison.Ordinal);
        var format = handler.PlatformView.Document.GetRange(inserted, inserted + 1).CharacterFormat;
        Assert.Equal(FormatEffect.On, format.Bold);
        Assert.Equal(FormatEffect.On, format.Italic);
        view.Undo();
        Assert.Equal(nativeBefore, NativeText(handler));
        view.Redo();
        Assert.Equal(nativeAfter, NativeText(handler));
        var pasted = document.CurrentSnapshot;
        handler.PlatformView.Document.Selection.TypeText("X");
        Assert.Equal(pasted.ProjectText().Text.Length + 1, document.CurrentSnapshot.ProjectText().Text.Length);
        view.Undo();
        Assert.Equal(nativeAfter, NativeText(handler));
    });

    [Fact]
    public Task UndoRestoresParagraphsBeforeANativeTableAtTheStartOfTheDocument() => Run(async (view, handler) =>
    {
        var document = view.StructuredSession!.Document;
        var before = await Save(document);
        var nativeBefore = NativeText(handler);
        var body = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body);
        var prefix = body.Blocks.TakeWhile(id => document.CurrentSnapshot.Get<RichBlock>(id) is not RichTable).ToArray();
        Assert.NotEmpty(prefix);
        document.Edit(edit => { foreach (var block in prefix) edit.DeleteBlock(block); });
        await Task.Delay(80);
        Assert.IsType<RichTable>(document.CurrentSnapshot.Get<RichBlock>(document.CurrentSnapshot.Get<RichStory>(body.Id).Blocks[0]));
        Assert.StartsWith("\uFFF9\r", NativeText(handler));
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(before, await Save(document));
        Assert.Equal(nativeBefore, NativeText(handler));
        view.Redo();
        await Task.Delay(80);
        Assert.StartsWith("\uFFF9\r", NativeText(handler));
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(nativeBefore, NativeText(handler));
    });

    [Theory]
    [InlineData(-7, false, false)]
    [InlineData(-6, false, false)]
    [InlineData(-5, false, false)]
    [InlineData(-4, false, false)]
    [InlineData(-3, false, false)]
    [InlineData(-2, false, false)]
    [InlineData(-4, true, false)]
    [InlineData(-3, true, false)]
    [InlineData(-2, true, false)]
    [InlineData(-4, false, true)]
    [InlineData(-4, true, true)]
    public Task DeleteFromFirstLineToTableBoundaryCanUndo(int boundaryOffset, bool priorEdit, bool reversed) => Run(async (view, handler) =>
    {
        var document = view.StructuredSession!.Document;
        if (priorEdit)
        {
            handler.PlatformView.Document.Selection.SetRange(1, 1);
            handler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(50);
        }
        var original = await Save(document);
        var nativeOriginal = NativeText(handler);
        var snapshot = document.CurrentSnapshot;
        var table = snapshot.Nodes.Values.OfType<RichTable>().First();
        var tableStart = nativeOriginal.IndexOf("1st column", StringComparison.Ordinal);
        Assert.True(tableStart >= 2);
        Assert.Equal("\uFFF9\r", nativeOriginal[(tableStart - 2)..tableStart]);
        var formats = new[] { "Font size 10", "Font size 12", "Underline,bold", "Underline,italic", "Font size 22", "Bold text.", "Italic text.", "1st column" }
            .Select(text => nativeOriginal.IndexOf(text, StringComparison.Ordinal))
            .Select(position => (Position: position, Format: handler.PlatformView.Document.GetRange(position, position + 1).CharacterFormat.GetClone())).ToArray();
        handler.PlatformView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        var nativeSelection = handler.PlatformView.Document.Selection;
        nativeSelection.SetRange(0, tableStart + boundaryOffset);
        nativeSelection.Options = reversed ? nativeSelection.Options | SelectionOptions.StartActive : nativeSelection.Options & ~SelectionOptions.StartActive;
        await Task.Delay(50);
        var selected = view.StructuredSession.Selection;
        var diagnostic = $"Native selection {handler.PlatformView.Document.Selection.StartPosition}:{handler.PlatformView.Document.Selection.EndPosition}; rich {view.StructuredSession.Selection}";
        Delete(view, handler);
        await Task.Delay(80);
        var afterDelete = await Save(document);
        Assert.False(original.SequenceEqual(afterDelete), diagnostic);
        Assert.True(document.CurrentSnapshot.Nodes.ContainsKey(table.Id));
        var nativeDeleted = NativeText(handler);
        Assert.Equal(0, nativeSelection.StartPosition);
        Assert.Equal(0, nativeSelection.EndPosition);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            view.Undo();
            await Task.Delay(80);
            Assert.Equal(original, await Save(document));
            Assert.Equal(nativeOriginal, NativeText(handler));
            Assert.Equal(selected, view.StructuredSession.Selection);
            Assert.Equal(0, nativeSelection.StartPosition);
            // A range ending at the row separator restores to the preceding
            // paragraph's end, with the same authored text selected.
            Assert.Equal(Math.Min(tableStart + boundaryOffset, tableStart - 3), nativeSelection.EndPosition);
            Assert.Equal(reversed, (nativeSelection.Options & SelectionOptions.StartActive) != 0);
            foreach (var (position, format) in formats)
                Assert.True(handler.PlatformView.Document.GetRange(position, position + 1).CharacterFormat.IsEqual(format), $"Native formatting changed at {position}.");
            if (cycle == 0)
            {
                view.Redo();
                await Task.Delay(80);
                Assert.Equal(afterDelete, await Save(document));
                Assert.Equal(nativeDeleted, NativeText(handler));
                Assert.Equal(0, nativeSelection.StartPosition);
                Assert.Equal(0, nativeSelection.EndPosition);
            }
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeletingDifferentlyStyledLinesCanUndoAndRedo(bool reversed) => Run(async (view, handler) =>
    {
        var original = await Save(view.StructuredSession!.Document);
        var text = view.Document.Text;
        var start = text.IndexOf("Font size 10", StringComparison.Ordinal);
        var end = text.IndexOf("Bold text.", start, StringComparison.Ordinal) + "Bold text.".Length;
        Assert.True(start >= 0 && end > start, text);
        await Select(view, handler, start, end, reversed);
        Delete(view, handler);
        await Task.Delay(80);
        Assert.DoesNotContain("Font size 10", view.Document.Text);
        var deleted = await Save(view.StructuredSession.Document);
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(original, await Save(view.StructuredSession.Document));
        view.Redo();
        await Task.Delay(80);
        Assert.Equal(deleted, await Save(view.StructuredSession.Document));
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(original, await Save(view.StructuredSession.Document));
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public Task DeletingReferenceListItemsRemovesTheirText(bool numbered, bool partial, bool reversed) => Run(async (view, handler) =>
    {
        var document = view.StructuredSession!.Document;
        var original = await Save(document);
        var snapshot = document.CurrentSnapshot;
        var lists = snapshot.Nodes.Values.OfType<RichList>().Where(list => list.Numbered == numbered).ToArray();
        var paragraphs = lists.SelectMany(list => list.Items).SelectMany(id => snapshot.Get<RichListItem>(id).Blocks)
            .Where(id => snapshot.Get<RichBlock>(id) is RichParagraph && snapshot.GetParagraphLength(id) > 0)
            .OrderBy(id => snapshot.ProjectText().GetOffset(new(snapshot.Body, id, 0))).ToArray();
        Assert.True(paragraphs.Length >= 2);
        var projection = snapshot.ProjectText();
        var start = projection.GetOffset(new(snapshot.Body, paragraphs[0], 0));
        var end = projection.GetOffset(new(snapshot.Body, paragraphs[1], snapshot.GetParagraphLength(paragraphs[1])));
        if (partial) { start += 2; end -= 2; }
        var removed = view.Document.Text[start..end];
        await Select(view, handler, start, end, reversed);
        var diagnostic = $"Selected items {paragraphs[0]} ({snapshot.Get<RichParagraph>(paragraphs[0]).Parent}) to {paragraphs[1]} ({snapshot.Get<RichParagraph>(paragraphs[1]).Parent}); selection {view.StructuredSession.Selection}; text [{removed}]";
        Delete(view, handler);
        await Task.Delay(80);
        var afterDelete = await Save(document);
        Assert.False(original.SequenceEqual(afterDelete), diagnostic);
        Assert.DoesNotContain(removed, view.Document.Text);
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(original, await Save(document));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeletingTheWholeReferenceTableRemovesItsStructure(bool programmatic) => Run(async (view, handler) =>
    {
        var document = view.StructuredSession!.Document;
        var original = await Save(document);
        var snapshot = document.CurrentSnapshot;
        var table = snapshot.Nodes.Values.OfType<RichTable>().First();
        var grid = snapshot.GetTableGrid(table.Id);
        if (programmatic) view.StructuredSession.Selection = new RichCellSelection(table.Id, 0, 0, grid.Rows, grid.Columns);
        else
        {
            var projection = snapshot.ProjectText();
            var first = snapshot.Get<RichTableCell>(grid[0, 0]).Blocks[0];
            var last = snapshot.Get<RichTableCell>(grid[grid.Rows - 1, grid.Columns - 1]).Blocks[^1];
            await Select(view, handler, projection.GetOffset(new(snapshot.Body, first, 0)),
                projection.GetOffset(new(snapshot.Body, last, snapshot.GetParagraphLength(last))));
        }
        Delete(view, handler);
        await Task.Delay(80);
        Assert.False(document.CurrentSnapshot.Nodes.ContainsKey(table.Id), "Deleting the complete table must remove the table, not only its cell text.");
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(original, await Save(document));
    });

    private static async Task Select(NativeEditorView view, RichEditorHandler handler, int start, int end, bool reversed = false)
    {
        var range = handler.NativeProjection.ToDisplay(new TextSelectionState(start, end));
        var native = handler.PlatformView.Document.Selection;
        native.SetRange(range.Anchor, range.Active);
        native.Options = reversed ? native.Options | SelectionOptions.StartActive : native.Options & ~SelectionOptions.StartActive;
        await Task.Delay(50);
    }

    private static void Delete(NativeEditorView view, RichEditorHandler handler)
    {
        if (!view.SendKeyDown(EditorKey.Delete, EditorKeyModifiers.None))
            handler.PlatformView.Document.Selection.Delete(TextRangeUnit.Character, 1);
    }

    private static async Task<byte[]> Save(RichDocument document)
    {
        using var stream = new MemoryStream();
        await RichEdit.Core.Rtf.RtfCodec.WriteAsync(document.CurrentSnapshot, stream);
        return stream.ToArray();
    }

    private static string NativeText(RichEditorHandler handler)
    {
        var range = handler.PlatformView.Document.GetRange(0, 0);
        range.SetRange(0, range.StoryLength);
        return range.Text;
    }

    private static Task Run(Func<NativeEditorView, RichEditorHandler, Task> action) => WindowsTestHost.RunAsync(async () =>
    {
        using var input = typeof(WindowsReferenceDeletionTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var view = new NativeEditorView { FontSize = 17 };
        view.BindStructuredDocument(new RichDocument(imported.Snapshot));
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        view.Handler = handler;
        var errors = new List<Exception>();
        void Unhandled(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) { errors.Add(args.Exception); args.Handled = true; }
        Microsoft.UI.Xaml.Application.Current.UnhandledException += Unhandled;
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        window.Content = handler.ContainerView ?? handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            await action(view, handler);
            if (errors.Count != 0) throw new AggregateException(errors);
        }
        catch (Exception error) when (errors.Count != 0)
        {
            throw new AggregateException(errors.Append(error));
        }
        finally
        {
            Microsoft.UI.Xaml.Application.Current.UnhandledException -= Unhandled;
            window.Content = null;
            view.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
            window.AppWindow.Hide();
        }
    });
}
