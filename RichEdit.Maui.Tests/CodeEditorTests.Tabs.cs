namespace RichEdit.Maui.Tests;

public sealed partial class CodeEditorTests
{
    private static Microsoft.UI.Xaml.Window? _tabWindow;
    [Fact]
    public Task SpaceIndentationMeasuresExistingTabsWithTheirDisplayWidth() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEdit.Maui.CodeEditor
        { Document = CodeEdit.Maui.CodeDocument.FromPlainText("\t"), TabSize = 3, IndentSize = 4 };
        editor.SelectedRange = new(1, 0);
        editor.Indent();
        Assert.Equal("\t ", editor.Document.Text);
        editor.Undo();
        Assert.Equal("\t", editor.Document.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TabDisplayWidthUsesNativeSpaceAdvancesWithoutChangingSourceOrIndentation() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("\tX\n    X");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.Activate();
        try
        {
            await Task.Delay(100);
            var snapshot = editor.Document.CurrentSnapshot;
            double X(int offset) => fixture.Handler.GetTextCaretBounds(offset, RichTextCaretAffinity.Downstream)!.Value.Left;
            var fourSpaces = X(7) - X(3);
            Assert.InRange(Math.Abs((X(1) - X(0)) - fourSpaces), 0, 1);
            editor.SelectedRange = new(1, 0);
            editor.TabSize = 8;
            await Task.Delay(30);
            Assert.InRange(Math.Abs((X(1) - X(0)) - fourSpaces * 2), 0, 1);
            editor.IndentSize = 2;
            Assert.Equal(8, editor.TabSize);
            Assert.InRange(Math.Abs((X(1) - X(0)) - fourSpaces * 2), 0, 1);
            Assert.Same(snapshot, editor.Document.CurrentSnapshot);
            Assert.False(editor.CanUndo);
            Assert.False(editor.Document.IsModified);
            Assert.Equal(new(1, 0), editor.SelectedRange);
            editor.FontSize = 20;
            await Task.Delay(30);
            Assert.InRange(Math.Abs((X(1) - X(0)) - (X(7) - X(3)) * 2), 0, 1);
            using (var widget = editor.Adornments.Add(0, new Label { Text = "hint", WidthRequest = 110, HeightRequest = 20 },
                new() { Placement = RichTextAdornmentPlacement.Inline }))
            {
                await Task.Delay(150);
                var interval = (X(7) - X(3)) * 2;
                var leading = X(0) - X(3);
                var expected = (Math.Floor((leading + 0.01) / interval) + 1) * interval;
                Assert.InRange(Math.Abs((X(1) - X(3)) - expected), 0, 2);
            }
            await Task.Delay(60);
            editor.SelectedRange = new(0, 0);
            fixture.Handler.PlatformView.Document.Selection.TypeText("WWWWWWWWW");
            await Task.Delay(30);
            Assert.Equal("WWWWWWWWW\tX\n    X", editor.Document.Text);
            Assert.InRange(Math.Abs((X(10) - X(0)) - (X(16) - X(12)) * 4), 0, 2);
        }
        finally { window.Content = null; }
    });
}
