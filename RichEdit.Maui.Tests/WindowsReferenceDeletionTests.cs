using Microsoft.Maui;
using Microsoft.UI.Text;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class WindowsReferenceDeletionTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Theory]
    [InlineData(-3, false)]
    [InlineData(-2, false)]
    [InlineData(-3, true)]
    [InlineData(-2, true)]
    public Task DeleteFromFirstLineToTableBoundaryCanUndo(int boundaryOffset, bool priorEdit) => Run(async (view, handler) =>
    {
        var document = view.StructuredSession!.Document;
        if (priorEdit)
        {
            handler.PlatformView.Document.Selection.SetRange(1, 1);
            handler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(50);
        }
        var original = await Save(document);
        var snapshot = document.CurrentSnapshot;
        var table = snapshot.Nodes.Values.OfType<RichTable>().First();
        var first = snapshot.Get<RichTableCell>(snapshot.GetTableGrid(table.Id)[0, 0]).Blocks[0];
        var position = snapshot.ProjectText().GetOffset(new(snapshot.Body, first, 0));
        var tableStart = handler.NativeProjection.ToDisplay(new TextSelectionState(position, position)).Anchor;
        handler.PlatformView.Document.Selection.SetRange(0, tableStart + boundaryOffset);
        await Task.Delay(50);
        var diagnostic = $"Native selection {handler.PlatformView.Document.Selection.StartPosition}:{handler.PlatformView.Document.Selection.EndPosition}; rich {view.StructuredSession.Selection}";
        Delete(view, handler);
        await Task.Delay(80);
        var afterDelete = await Save(document);
        Assert.False(original.SequenceEqual(afterDelete), diagnostic);
        Assert.True(document.CurrentSnapshot.Nodes.ContainsKey(table.Id));
        view.Undo();
        await Task.Delay(80);
        Assert.Equal(original, await Save(document));
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
