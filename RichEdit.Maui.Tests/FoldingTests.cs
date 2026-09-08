using CodeEdit.Maui;
using Microsoft.Maui;
using Microsoft.UI.Text;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class FoldingTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Fact]
    public Task SharedNativeFoldingContracts() => WithNativeEditor(async (editor, _) =>
    {
        foreach (var test in EditorContractTests.Cases.Where(test => test.Name.StartsWith("folding ", StringComparison.Ordinal)))
        {
            EditorContractTests.Reset(editor);
            await test.Run(editor);
        }
    });

    [Fact]
    public Task SampleActionsCollapseSelectionAndExpandAtCaret() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("head\none\ntwo\ntail"), SelectedRange = new(5, 8) };
        var actions = new CodeEditorActions(editor);
        actions.CollapseSelection();
        Assert.Equal(new RichTextRange(5, 8), Assert.Single(editor.Folding.CollapsedRanges));
        Assert.Equal(new RichTextRange(5, 0), editor.SelectedRange);
        Assert.Equal(new RichTextRange(5, 8), actions.GetFoldAtCaret());
        actions.ExpandAtCaret();
        Assert.Empty(editor.Folding.CollapsedRanges);
        Assert.Equal("head\none\ntwo\ntail", editor.Document.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task NestedRangesExpandIndependentlyAndNavigationIsExplicit() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("head\none\ntwo\ntail") };
        var outer = new RichTextRange(5, 8);
        var inner = new RichTextRange(9, 4);
        editor.Folding.Collapse(inner);
        editor.Folding.Collapse(outer);
        Assert.Equal(new[] { outer, inner }, editor.Folding.CollapsedRanges);
        Assert.Equal(outer, editor.Folding.GetCollapsedRange(10));
        editor.SelectedRange = new(10, 0);
        editor.ScrollIntoView(new(10, 0));
        Assert.Equal(2, editor.Folding.CollapsedRanges.Count);
        editor.Folding.Expand(outer);
        Assert.Equal(new[] { inner }, editor.Folding.CollapsedRanges);
        Assert.Null(editor.Folding.GetCollapsedRange(5));
        Assert.Equal(inner, editor.Folding.GetCollapsedRange(9));
        Assert.Null(editor.Folding.GetCollapsedRange(inner.End));
        editor.Folding.ExpandAll();
        Assert.Empty(editor.Folding.CollapsedRanges);
    });

    [Fact]
    public Task FoldingPreservesDocumentSelectionHistoryAndSaveState() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcdef") };
        editor.Document.Edit(edit => edit.InsertText(6, "g"));
        editor.Document.MarkSaved();
        editor.Document.Edit(edit => edit.InsertText(7, "h"));
        editor.Undo();
        editor.SelectionState = new(5, 1);
        var snapshot = editor.Document.CurrentSnapshot;
        var rtf = editor.Document.RtfText;
        var changed = 0;
        var selected = 0;
        var folded = 0;
        editor.ContentChanged += (_, _) => changed++;
        editor.SelectionChanged += (_, _) => selected++;
        editor.Folding.Changed += (_, _) => folded++;
        editor.Folding.Collapse(new(1, 5));
        editor.Folding.Collapse(new(1, 5));
        Assert.Same(snapshot, editor.Document.CurrentSnapshot);
        Assert.Equal(rtf, editor.Document.RtfText);
        Assert.Equal(new RichTextSelectionState(5, 1), editor.SelectionState);
        Assert.False(editor.Document.IsModified);
        Assert.True(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, changed);
        Assert.Equal(0, selected);
        Assert.Equal(1, folded);
        editor.Folding.ExpandAll();
        Assert.Equal(2, folded);
        editor.Redo();
        Assert.Equal("abcdefgh", editor.Document.Text);
    });

    [Fact]
    public Task FoldBoundariesTrackEditsAndKeepBoundaryInsertionsVisible() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abCDEFgh") };
        editor.Folding.Collapse(new(2, 4));
        editor.Document.Edit(edit => edit.InsertText(0, "!"));
        Assert.Equal(new RichTextRange(3, 4), Assert.Single(editor.Folding.CollapsedRanges));
        editor.Document.Edit(edit => edit.InsertText(3, "<"));
        Assert.Equal(new RichTextRange(4, 4), Assert.Single(editor.Folding.CollapsedRanges));
        editor.Document.Edit(edit => edit.InsertText(8, ">"));
        Assert.Equal(new RichTextRange(4, 4), Assert.Single(editor.Folding.CollapsedRanges));
        editor.Document.Edit(edit => edit.InsertText(6, "inside"));
        Assert.Equal(new RichTextRange(4, 10), Assert.Single(editor.Folding.CollapsedRanges));
        editor.Document.Edit(edit => edit.ReplaceText(new(3, 3), "start"));
        Assert.Equal(new RichTextRange(8, 8), Assert.Single(editor.Folding.CollapsedRanges));
        editor.Document.Edit(edit => edit.DeleteText(new(14, 3)));
        Assert.Equal(new RichTextRange(8, 6), Assert.Single(editor.Folding.CollapsedRanges));
        editor.Document.Edit(edit => edit.ReplaceText(new(7, 8), "replacement"));
        Assert.Empty(editor.Folding.CollapsedRanges);
    });

    [Fact]
    public Task InvalidUpdatesAreAtomicAndDocumentReplacementClearsFolds() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcdefgh") };
        var range = new RichTextRange(2, 4);
        editor.Folding.Collapse(range);
        Assert.Throws<ArgumentException>(() => editor.Folding.SetCollapsedRanges([new(1, 2), new(5, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Folding.SetCollapsedRanges([new(1, 2), new(6, 3)]));
        Assert.Equal(range, Assert.Single(editor.Folding.CollapsedRanges));
        var notifications = 0;
        editor.Folding.Changed += (_, _) => notifications++;
        editor.Document = RichTextDocument.FromPlainText("new document");
        Assert.Empty(editor.Folding.CollapsedRanges);
        Assert.Equal(1, notifications);
    });

    [Fact]
    public Task FoldingMutationsRejectBackgroundThreads() => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcdef") };
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => editor.Folding.Collapse(new(1, 2))));
        Assert.Empty(editor.Folding.CollapsedRanges);
    });

    [Fact]
    public Task NativeFoldingRemovesLineSpaceAndExpandsWithoutChangingSource() => WithNativeEditor(async (editor, handler) =>
    {
        const string source = "head\none\ntwo\ntail";
        editor.Document = RichTextDocument.FromPlainText(source);
        await Task.Delay(60);
        var before = GetRect(handler, 13);
        var header = GetRect(handler, 0);
        var version = editor.Document.Version;
        editor.Folding.Collapse(new(5, 8));
        await Task.Delay(60);
        var collapsed = GetRect(handler, 13);
        Assert.True(collapsed.Y < before.Y - header.Height, $"tail before={before}, collapsed={collapsed}, header={header}");
        Assert.Equal(source, ReadNativeText(handler));
        Assert.Equal(source, editor.Document.Text);
        Assert.Equal(version, editor.Document.Version);
        Assert.False(editor.CanUndo);
        Assert.All(editor.Document.CurrentSnapshot.Runs, run => Assert.False(run.Format.Hidden));
        editor.Folding.ExpandAll();
        await Task.Delay(60);
        Assert.Equal(before.Y, GetRect(handler, 13).Y, 1);
        Assert.Equal(version, editor.Document.Version);
    });

    [Fact]
    public Task NativeEditsAndFormattingPreserveFoldedSourceAndAuthoredFormats() => WithNativeEditor(async (editor, handler) =>
    {
        const string source = "head\none\ntwo\ntail";
        editor.Document = RichTextDocument.FromPlainText(source);
        editor.Folding.Collapse(new(5, 8));
        editor.SelectedRange = new(source.Length, 0);
        handler.PlatformView.Document.GetRange(source.Length, source.Length).SetText(TextSetOptions.None, "!");
        await Task.Delay(100);
        Assert.Equal(source + "!", editor.Document.Text);
        Assert.Equal(new RichTextRange(5, 8), Assert.Single(editor.Folding.CollapsedRanges));
        Assert.All(editor.Document.CurrentSnapshot.Runs, run => Assert.False(run.Format.Hidden));
        editor.Undo();
        await Task.Delay(80);
        Assert.Equal(source, editor.Document.Text);
        Assert.Equal(source, ReadNativeText(handler));
        editor.SelectedRange = new(6, 1);
        editor.Selection.CharacterFormat.Bold = true;
        await Task.Delay(60);
        Assert.Equal(FormatEffect.On, handler.PlatformView.Document.GetRange(6, 7).CharacterFormat.Hidden);
        editor.Folding.ExpandAll();
        Assert.Equal(FormatEffect.Off, handler.PlatformView.Document.GetRange(6, 7).CharacterFormat.Hidden);
        Assert.Equal(FormatEffect.On, handler.PlatformView.Document.GetRange(6, 7).CharacterFormat.Bold);
    });

    [Fact]
    public Task FoldedHyperlinksRetainTheirSourceAndNativeOffsetMapping() => WithNativeEditor(async (editor, handler) =>
    {
        const string source = "head\nlink\ntail";
        editor.Document = RichTextDocument.FromPlainText(source);
        editor.Document.Edit(edit => edit.SetLink(new(5, 4), "https://example.com"));
        await Task.Delay(50);
        editor.Folding.Collapse(new(5, 5));
        await Task.Delay(50);
        editor.SelectedRange = new(editor.Document.Length, 0);
        handler.PlatformView.Document.Selection.SetText(TextSetOptions.None, "!");
        await Task.Delay(100);
        Assert.Equal(source + "!", editor.Document.Text);
        Assert.Equal(new RichTextRange(5, 4), Assert.Single(editor.Document.CurrentSnapshot.Links).Range);
        editor.Folding.ExpandAll();
        await Task.Delay(50);
        Assert.Equal(source + "!", editor.Document.Text);
    });

    [Fact]
    public Task FoldingSurvivesHandlerReattachment() => WithNativeEditor(async (editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("head\none\ntwo\ntail");
        editor.Folding.Collapse(new(5, 8));
        _window!.Content = null;
        editor.Handler = null;
        ((IElementHandler)handler).DisconnectHandler();
        var replacement = new RichEditorHandler();
        replacement.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = replacement;
        _window.Content = replacement.ContainerView ?? replacement.PlatformView;
        try
        {
            await Task.Delay(60);
            Assert.Equal(new RichTextRange(5, 8), Assert.Single(editor.Folding.CollapsedRanges));
            var collapsed = GetRect(replacement, 13).Y;
            editor.Folding.ExpandAll();
            await Task.Delay(60);
            Assert.True(GetRect(replacement, 13).Y > collapsed);
            Assert.Equal("head\none\ntwo\ntail", editor.Document.Text);
        }
        finally
        {
            _window.Content = null;
            editor.Handler = null;
            ((IElementHandler)replacement).DisconnectHandler();
        }
    });

    private static Task WithNativeEditor(Func<RichEditor, RichEditorHandler, Task> test) => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor();
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = handler;
        _window ??= new Microsoft.UI.Xaml.Window();
        _window.Content = handler.ContainerView ?? handler.PlatformView;
        _window.Activate();
        try { await test(editor, handler); }
        finally
        {
            _window.Content = null;
            editor.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
        }
    });

    private static Windows.Foundation.Rect GetRect(RichEditorHandler handler, int offset)
    {
        handler.PlatformView.Document.GetRange(offset, offset).GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        return rect;
    }

    private static string ReadNativeText(RichEditorHandler handler)
    {
        handler.PlatformView.Document.GetText(TextGetOptions.None, out var text);
        return text.TrimEnd('\r').Replace('\r', '\n');
    }
}
