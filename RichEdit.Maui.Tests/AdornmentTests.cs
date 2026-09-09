using CodeEdit.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class AdornmentTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Fact]
    public Task SharedNativeAdornmentContracts() => WithNativeEditor(async (editor, _) =>
    {
        foreach (var test in EditorContractTests.Cases.Where(test => test.Name.StartsWith("adornments ", StringComparison.Ordinal)))
        {
            EditorContractTests.Reset(editor);
            await test.Run(editor);
        }
    });

    [Fact]
    public Task SampleIndicatorsExpandParentsAndKeepNestedFolds() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("head\none\ntwo\ntail") };
        using var indicators = new CodeEditorFoldIndicators(editor);
        var outer = new RichTextRange(5, 8);
        var inner = new RichTextRange(9, 4);
        editor.Folding.SetCollapsedRanges((RichTextRange[])[outer, inner]);
        Assert.Single(editor.Adornments);
        ClickSampleIndicator(editor);
        Assert.Equal(inner, Assert.Single(editor.Folding.CollapsedRanges));
        Assert.Single(editor.Adornments);
        ClickSampleIndicator(editor);
        Assert.Empty(editor.Folding.CollapsedRanges);
        Assert.Empty(editor.Adornments);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task SampleIndicatorsKeepIndependentButtonsForFoldsOnOneLine() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("one ABC two DEF end") };
        using var indicators = new CodeEditorFoldIndicators(editor);
        editor.Folding.SetCollapsedRanges((RichTextRange[])[new(4, 3), new(12, 3)]);
        var row = Assert.IsType<HorizontalStackLayout>(Assert.Single(editor.Adornments).View);
        Assert.Equal(2, row.Count);
        ((Button)row[0]).Command.Execute(null);
        Assert.Equal(new RichTextRange(12, 3), Assert.Single(editor.Folding.CollapsedRanges));
        ClickSampleIndicator(editor);
        Assert.Empty(editor.Folding.CollapsedRanges);
    });

    [Fact]
    public Task AdornmentsValidateOwnershipAndThreadBeforeMutating() => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcd") };
        var label = new Label { Text = "annotation" };
        using var item = editor.Adornments.Add(2, label);
        Assert.Throws<ArgumentException>(() => editor.Adornments.Add(3, label));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Adornments.Add(5, new Label()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Adornments.MarginWidth = double.NaN);
        var exception = await Task.Run(() => Record.Exception(() => editor.Adornments.Clear()));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Single(editor.Adornments);
        editor.Document.Edit(edit => edit.InsertText(2, "!"));
        Assert.Equal(3, item.Position);
        editor.Undo();
        Assert.Equal(2, item.Position);
        editor.Document.Edit(edit => edit.DeleteText(new(1, 2)));
        Assert.Empty(editor.Adornments);
        Assert.Null(label.Parent);
        editor.Undo();
        Assert.Empty(editor.Adornments);
    });

    [Fact]
    public Task SampleEllipsisStaysWithHeaderWhenTextIsInsertedAfterFold() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("aaa\nbbb\nccc\nddd"), SelectedRange = new(4, 7) };
        using var indicators = new CodeEditorFoldIndicators(editor);
        new CodeEditorActions(editor).CollapseSelection();
        var range = Assert.Single(editor.Folding.CollapsedRanges);
        editor.Document.Edit(edit => edit.InsertText(range.End, "\n"));
        Assert.Equal(range, Assert.Single(editor.Folding.CollapsedRanges));
        Assert.Equal(range.End, Assert.Single(editor.Adornments, item => item.Placement == RichTextAdornmentPlacement.Text).Position);
        editor.Undo();
        Assert.Equal(range.End, Assert.Single(editor.Adornments, item => item.Placement == RichTextAdornmentPlacement.Text).Position);
    });

    [Fact]
    public Task AdornmentsSurviveHandlerReattachment() => WithNativeEditor(async (editor, handler) =>
    {
        editor.Document = RichTextDocument.FromPlainText("head\nbody\ntail");
        var view = new Button { Text = "+", WidthRequest = 24, HeightRequest = 20 };
        editor.Adornments.MarginWidth = 32;
        using var item = editor.Adornments.Add(5, view, RichTextAdornmentPlacement.LeftMargin);
        await Task.Delay(100);
        _window!.Content = null;
        editor.Handler = null;
        ((IElementHandler)handler).DisconnectHandler();
        var replacement = new RichEditorHandler();
        replacement.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = replacement;
        _window.Content = replacement.ContainerView ?? replacement.PlatformView;
        try
        {
            await Task.Delay(150);
            Assert.Single(editor.Adornments);
            Assert.NotNull(view.Handler);
            Assert.True(view.Bounds.X >= 0);
            Assert.Equal("head\nbody\ntail", editor.Document.Text);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            _window.Content = null;
            editor.Handler = null;
            ((IElementHandler)replacement).DisconnectHandler();
        }
    });

    private static void ClickSampleIndicator(CodeEditor editor)
    {
        var row = (HorizontalStackLayout)Assert.Single(editor.Adornments).View;
        ((Button)row[0]).Command.Execute(null);
    }

    private static Task WithNativeEditor(Func<RichEditor, RichEditorHandler, Task> test) => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new RichEditor();
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = handler;
        _window ??= new Microsoft.UI.Xaml.Window();
        var panel = new Microsoft.UI.Xaml.Controls.Grid();
        panel.Children.Add(handler.ContainerView ?? handler.PlatformView);
        _window.Content = panel;
        _window.Activate();
        try { await test(editor, handler); }
        finally
        {
            _window.Content = null;
            editor.Handler = null;
            ((IElementHandler)handler).DisconnectHandler();
        }
    });
}
