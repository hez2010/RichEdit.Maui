namespace RichEdit.Maui.Tests;

public sealed partial class CodeEditorTests
{
    private static Microsoft.UI.Xaml.Window? _tabWindow;

    [Fact]
    public Task TabsAfterDifferentLengthFieldNamesAlignSeparators() => WindowsTestHost.RunAsync(async () =>
    {
        string[] fieldNames = ["1", "22", "333", "4444", "55555", "666666", "7777777", "88888888", "999999999"];
        // One leading tab starts each field at column 4 (zero-based).
        // Add enough tabs to place every separator at column 20.
        string[] lines = fieldNames.Select(fieldName => "\t" + fieldName + new string('\t', (19 - (4 + fieldName.Length)) / 4 + 1) + "= value").ToArray();
        string source = string.Join("\n", lines);
        using var fixture = new CodeEditorFixture(source);
        fixture.Editor.TabSize = 4;
        fixture.Editor.FontFamily = "Consolas";
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await WaitForTabTestLayoutAsync(fixture);
            double? expectedPosition = null;
            int lineStart = 0;
            foreach (string line in lines)
            {
                int equalsOffset = lineStart + line.IndexOf('=');
                double actualPosition = fixture.Handler.GetTextCaretBounds(equalsOffset, RichTextCaretAffinity.Downstream)!.Value.Left;
                expectedPosition ??= actualPosition;
                Assert.True(Math.Abs(actualPosition - expectedPosition.Value) <= 1, $"{line}: expected X={expectedPosition}, actual X={actualPosition}");
                lineStart += line.Length + 1;
            }
            Assert.Equal(source, fixture.Editor.Document.Text);
        }
        finally
        {
            window.Content = null;
        }
    });

    // Compare native caret positions with spaces, rather than assuming a pixel width
    // for Consolas. Exact tab boundaries must advance to the following stop.
    [Theory]
    [InlineData(4, 3, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(4, 5, 1)]
    [InlineData(4, 7, 1)]
    [InlineData(4, 8, 1)]
    [InlineData(4, 9, 1)]
    [InlineData(4, 4, 3)]
    [InlineData(4, 8, 3)]
    [InlineData(8, 7, 1)]
    [InlineData(8, 8, 1)]
    [InlineData(8, 9, 1)]
    [InlineData(8, 8, 3)]
    [InlineData(3, 3, 1)]
    [InlineData(3, 6, 3)]
    [InlineData(4, 0, 3)]
    [InlineData(4, 4, 3, "Consolas", 10)]
    [InlineData(4, 4, 3, "Consolas", 11)]
    [InlineData(4, 4, 3, "Consolas", 12)]
    [InlineData(4, 4, 3, "Consolas", 13.5)]
    [InlineData(4, 4, 3, "Consolas", 13.51)]
    [InlineData(8, 8, 3, "Consolas", 14.02)]
    [InlineData(8, 8, 3, "Consolas", 14.03)]
    [InlineData(4, 4, 3, "Consolas", 18)]
    [InlineData(8, 8, 3, "Consolas", 20)]
    [InlineData(4, 4, 3, "Courier New", 10)]
    [InlineData(4, 4, 3, "Courier New", 11)]
    [InlineData(4, 4, 3, "Courier New", 12)]
    [InlineData(4, 4, 3, "Courier New", 13.5)]
    [InlineData(4, 4, 3, "Courier New", 13.52)]
    [InlineData(8, 8, 3, "Courier New", 14.02)]
    [InlineData(8, 8, 3, "Courier New", 16)]
    public Task TabsAdvanceToTheNextColumnStop(int tabSize, int prefixLength, int tabCount, string fontFamily = "Consolas", double fontSize = 14) => WindowsTestHost.RunAsync(async () =>
    {
        int expectedColumn = (prefixLength / tabSize + tabCount) * tabSize;
        string tabbedLine = new string('x', prefixLength) + new string('\t', tabCount) + "=";
        string referenceLine = new string(' ', expectedColumn) + "=";
        string source = tabbedLine + "\n" + referenceLine;
        using var fixture = new CodeEditorFixture(source);
        var snapshot = fixture.Editor.Document.CurrentSnapshot;
        fixture.Editor.FontFamily = fontFamily;
        fixture.Editor.FontSize = fontSize;
        fixture.Editor.TabSize = tabSize;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await WaitForTabTestLayoutAsync(fixture);
            int actualOffset = tabbedLine.Length - 1;
            int referenceOffset = tabbedLine.Length + 1 + expectedColumn;
            var actualBounds = fixture.Handler.GetTextCaretBounds(actualOffset, RichTextCaretAffinity.Downstream);
            var referenceBounds = fixture.Handler.GetTextCaretBounds(referenceOffset, RichTextCaretAffinity.Downstream);
            Assert.NotNull(actualBounds);
            Assert.NotNull(referenceBounds);
            double actualPosition = actualBounds.Value.Left;
            double expectedPosition = referenceBounds.Value.Left;
            Assert.True(Math.Abs(actualPosition - expectedPosition) <= 1, $"Font={fontFamily}, FontSize={fontSize}, TabSize={tabSize}, prefixLength={prefixLength}, tabCount={tabCount}: expected column {expectedColumn} at X={expectedPosition}, actual X={actualPosition}");
            Assert.Same(snapshot, fixture.Editor.Document.CurrentSnapshot);
            Assert.Equal(source, fixture.Editor.Document.Text);
            Assert.False(fixture.Editor.Document.IsModified);
            Assert.False(fixture.Editor.CanUndo);
        }
        finally
        {
            window.Content = null;
        }
    });

    private static async Task WaitForTabTestLayoutAsync(CodeEditorFixture fixture)
    {
        var view = fixture.Handler.PlatformView;
        if (!view.IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) => loaded.TrySetResult();
            view.Loaded += OnLoaded;
            try { await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { view.Loaded -= OnLoaded; }
        }
        view.UpdateLayout();
        await Task.Delay(30);
    }

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
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await WaitForTabTestLayoutAsync(fixture);
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
