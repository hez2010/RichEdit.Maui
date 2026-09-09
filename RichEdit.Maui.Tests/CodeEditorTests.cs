using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.UI.Text;
using CodeEdit.Maui;
using CodeEdit.Lsp;
using RichEdit.Maui.TestApp;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;
using Microsoft.UI.Xaml;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public partial class CodeEditorTests
{
    private static Microsoft.UI.Xaml.Window? _gutterWindow;
    private static MauiApp? _controlTestApp;
    [Fact]
    public Task PlainTextLoadingNormalizesLineEndingsWithoutHistory() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("a\r\nb\rc\n") };
        Assert.Equal("a\nb\nc\n", editor.Document.Text);
        Assert.Equal(4, editor.LineCount);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, editor.Document.Version);
        Assert.Equal(new RichTextRange(6, 0), editor.GetLineRange(4));
    });

    [Fact]
    public Task BlockIndentExcludesAnUnselectedFollowingLineAndIsAtomic() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("one\ntwo\nthree"), SelectedRange = new RichTextRange(0, 8) };
        var actions = new CodeEditorActions(editor);
        var changes = 0;
        editor.TextChanged += (_, _) => changes++;
        actions.Indent();
        Assert.Equal("    one\n    two\nthree", editor.Document.Text);
        Assert.Equal(1, changes);
        editor.Undo();
        Assert.Equal("one\ntwo\nthree", editor.Document.Text);
        Assert.False(editor.CanUndo);
        editor.Redo();
        editor.SelectedRange = new RichTextRange(0, 16);
        actions.Outdent();
        Assert.Equal("one\ntwo\nthree", editor.Document.Text);
    });

    [Fact]
    public Task IndentUsesTheNextStopAndOutdentHandlesTabs() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("ab"), SelectedRange = new RichTextRange(2, 0) };
        var actions = new CodeEditorActions(editor);
        actions.Indent();
        Assert.Equal("ab  ", editor.Document.Text);
        Assert.Equal(new RichTextRange(4, 0), editor.SelectedRange);
        editor.Document = CodeDocument.FromPlainText("\tfirst\n  second");
        editor.SelectAll();
        actions.Outdent();
        Assert.Equal("first\nsecond", editor.Document.Text);
        editor.UseTabs = true;
        editor.SelectAll();
        actions.Indent();
        Assert.Equal("\tfirst\n\tsecond", editor.Document.Text);
    });

    [Fact]
    public Task NewLineCopiesOnlyIndentationBeforeTheCaret() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("    call();"), SelectedRange = new RichTextRange(11, 0) };
        var actions = new CodeEditorActions(editor);
        actions.InsertNewLine();
        Assert.Equal("    call();\n    ", editor.Document.Text);
        Assert.Equal(new CodePosition(2, 5), editor.CaretPosition);
        editor.Undo();
        Assert.Equal("    call();", editor.Document.Text);
        editor.SelectedRange = new RichTextRange(2, 0);
        actions.InsertNewLine();
        Assert.Equal("  \n    call();", editor.Document.Text);
        Assert.Equal(new CodePosition(2, 3), editor.CaretPosition);
    });

    [Fact]
    public Task CommentsToggleAfterIndentationAndIgnoreSelectedBlankLines() => WindowsTestHost.RunAsync(() =>
    {
        const string source = "  first();\n\n\tsecond();\n";
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText(source) };
        var actions = new CodeEditorActions(editor);
        editor.SelectAll();
        actions.ToggleLineComment();
        Assert.Equal("  // first();\n\n\t// second();\n", editor.Document.Text);
        editor.SelectAll();
        actions.ToggleLineComment();
        Assert.Equal(source, editor.Document.Text);
        editor.Undo();
        Assert.Contains("//", editor.Document.Text);
    });

    [Fact]
    public Task ReadOnlyAndLengthLimitsPreventPartialCodeCommands() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("a\na"), MaxLength = 4 };
        var actions = new CodeEditorActions(editor);
        editor.SelectAll();
        actions.Indent();
        Assert.Equal("a\na", editor.Document.Text);
        Assert.Equal(0, actions.ReplaceAll("a", "long"));
        Assert.False(editor.CanUndo);
        editor.IsReadOnly = true;
        actions.Outdent();
        actions.ToggleLineComment();
        actions.InsertNewLine();
        Assert.Equal("a\na", editor.Document.Text);
    });

    [Fact]
    public Task OverLimitDocumentsCanStillShrink() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("    value"), MaxLength = 2 };
        var actions = new CodeEditorActions(editor);
        actions.Outdent();
        Assert.Equal("value", editor.Document.Text);
        editor.Undo();
        Assert.Equal("    value", editor.Document.Text);
    });

    [Fact]
    public Task SearchWrapsAndReplaceAllUsesOneUndoUnit() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("foo food FOO foo\u0301 foo" ) };
        var actions = new CodeEditorActions(editor);
        var options = new CodeSearchOptions(WholeWord: true);
        Assert.Equal(3, actions.FindAll("foo", options).Count);
        Assert.Equal(2, actions.FindAll("foo", new CodeSearchOptions(MatchCase: true, WholeWord: true)).Count);
        Assert.Equal(new RichTextRange(0, 3), actions.FindNext("foo", options));
        Assert.Equal(new RichTextRange(9, 3), actions.FindNext("foo", options));
        Assert.Equal(new RichTextRange(18, 3), actions.FindNext("foo", options));
        Assert.Null(actions.FindNext("foo", options, wrap: false));
        Assert.Equal(new RichTextRange(0, 3), actions.FindNext("foo", options));
        Assert.Equal(new RichTextRange(18, 3), actions.FindPrevious("foo", options));
        var source = editor.Document.Text;
        Assert.Equal(3, actions.ReplaceAll("foo", "bar", options));
        Assert.Equal("bar food bar foo\u0301 bar", editor.Document.Text);
        editor.Undo();
        Assert.Equal(source, editor.Document.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task DocumentOwnershipValidationKeepsTheWrapperAndNativeEditorInSync() => WindowsTestHost.RunAsync(() =>
    {
        var first = new CodeEditor { Document = CodeDocument.FromPlainText("owned") };
        var second = new CodeEditor();
        var original = second.Document;
        second.Document = first.Document;
        Assert.Same(original, second.Document);
        Assert.Same(original.Source, second.TextView.Document);
        GC.KeepAlive(first);
    });

    [Fact]
    public Task PositionsUseUtf16AndTrackDocumentReplacement() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = CodeDocument.FromPlainText("😀\n日本語\n") };
        var actions = new CodeEditorActions(editor);
        actions.GoToLine(2, 3);
        Assert.Equal(new RichTextRange(5, 0), editor.SelectedRange);
        Assert.Equal(new CodePosition(2, 3), editor.CaretPosition);
        actions.GoToLine(100, 100);
        Assert.Equal(new CodePosition(3, 1), editor.CaretPosition);
        var previous = editor.Document;
        editor.Document = CodeDocument.FromPlainText("x");
        previous.Edit(edit => edit.InsertText(0, "detached\n"));
        Assert.Equal(1, editor.LineCount);
        Assert.Equal("x", editor.Document.Text);
    });

    [Fact]
    public Task HighlightingAndThemeChangesPreserveSourceSelectionAndHistory() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("class C { }");
        var editor = fixture.Editor;
        editor.SelectedRange = new RichTextRange(6, 1);
        editor.Selection.ReplaceText("D");
        editor.Undo();
        var document = editor.Document;
        var source = document.Text;
        var textChanges = 0;
        editor.TextChanged += (_, _) => textChanges++;
        var selection = editor.SelectedRange;
        await editor.RefreshHighlightingAsync();
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 0));
        editor.Theme = static _ => Microsoft.Maui.Graphics.Colors.Purple;
        await editor.RefreshHighlightingAsync();
        Assert.Same(document, editor.Document);
        Assert.Equal(source, editor.Document.Text);
        Assert.Equal(0, textChanges);
        Assert.Equal(selection, editor.SelectedRange);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 0));
        editor.Redo();
        Assert.Equal("class D { }", editor.Document.Text);
    });

    [Fact]
    public Task HighlightingPreservesOtherFormatsAcrossTokenBoundaries() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("public class C { return 42; }");
        var editor = fixture.Editor;
        editor.TextView.Document.Edit(edit => edit.UpdateCharacterFormat(new RichTextRange(2, 8),
            format => format with { FontWeight = 700, BackgroundColor = Microsoft.Maui.Graphics.Colors.Yellow }));
        var before = editor.TextView.Document.CurrentSnapshot;
        await editor.RefreshHighlightingAsync();
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 0));
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 3));
        Assert.Equal(editor.TextColor!, NativeColor(fixture, 6));
        Assert.Equal(700, fixture.Handler.PlatformView.Document.GetRange(3, 4).CharacterFormat.Weight);
        Assert.Equal(Windows.UI.Color.FromArgb(255, 255, 255, 0), fixture.Handler.PlatformView.Document.GetRange(3, 4).CharacterFormat.BackgroundColor);
        editor.Theme = static _ => Microsoft.Maui.Graphics.Colors.Purple;
        await editor.RefreshHighlightingAsync();
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 3));
        editor.LanguageServer = null;
        await editor.RefreshHighlightingAsync();
        Assert.True(before.ContentEquals(editor.TextView.Document.CurrentSnapshot));
        Assert.Equal(editor.TextColor!, NativeColor(fixture, 3));
    });

    [Fact]
    public Task HighlightingTwoThousandLinesUpdatesNativeColorsAndPreservesHistory()
    {
        var output = TestContext.Current.TestOutputHelper!;
        return WindowsTestHost.RunAsync(async () =>
        {
            const string block = "    public static string FormatValue(int value)\n    {\n        var result = value + 42;\n        return $\"Value: {result}\"; // formatted value\n    }";
            var source = string.Join('\n', Enumerable.Repeat(block, 400));
            using var fixture = new CodeEditorFixture(source);
            var editor = fixture.Editor;
            editor.SelectedRange = new RichTextRange(source.LastIndexOf("return", StringComparison.Ordinal), 6);
            editor.Selection.ReplaceText("throw");
            editor.Undo();
            var selection = editor.SelectedRange;
            var changes = new List<RichTextChangeSet>();
            editor.ContentChanged += (_, args) => changes.Add(args.ChangeSet);

            var timer = System.Diagnostics.Stopwatch.StartNew();
            await editor.RefreshHighlightingAsync();
            output.WriteLine($"Initial highlighting: {timer.Elapsed.TotalMilliseconds:F1} ms (2,000 lines, {editor.Tokens.Count} tokens)");
            Assert.Equal(2000, editor.LineCount);
            Assert.Equal(4400, editor.Tokens.Count);
            Assert.Empty(changes);
            Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, source.IndexOf("public", StringComparison.Ordinal)));
            Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, source.LastIndexOf("return", StringComparison.Ordinal)));
            Assert.Equal(TokenColor(editor, "number"), NativeColor(fixture, source.LastIndexOf("42", StringComparison.Ordinal)));
            Assert.Equal(TokenColor(editor, "comment"), NativeColor(fixture, source.LastIndexOf("//", StringComparison.Ordinal)));

            timer.Restart();
            editor.Theme = static _ => Microsoft.Maui.Graphics.Colors.Purple;
            await editor.RefreshHighlightingAsync();
            output.WriteLine($"Theme change and highlighting: {timer.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, source.LastIndexOf("return", StringComparison.Ordinal)));
            Assert.Equal(TokenColor(editor, "comment"), NativeColor(fixture, source.LastIndexOf("//", StringComparison.Ordinal)));
            var version = editor.Document.Version;
            await editor.RefreshHighlightingAsync();
            Assert.Equal(version, editor.Document.Version);
            Assert.Equal(source, editor.Document.Text);
            Assert.Equal(selection, editor.SelectedRange);
            Assert.False(editor.CanUndo);
            Assert.True(editor.CanRedo);
            editor.Redo();
            Assert.Contains("throw", editor.Document.Text);
        });
    }

    [Fact]
    public Task RecoloringUpdatesOnlyRangesWhoseClassificationChanged() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Concat(Enumerable.Repeat("class A { }\n", 40)));
        var editor = fixture.Editor;
        await editor.RefreshHighlightingAsync();
        var start = editor.Document.Text.LastIndexOf("class", StringComparison.Ordinal);
        var previousFormat = editor.TextView.Document.CurrentSnapshot.Runs.First(run => run.Range.Start <= start && run.Range.End > start).Format;
        editor.Document.Edit(edit => edit.ReplaceText(new RichTextRange(start, 5), "other"));
        var changes = new List<RichTextChangeSet>();
        editor.ContentChanged += (_, args) => changes.Add(args.ChangeSet);
        await editor.RefreshHighlightingAsync();
        Assert.Empty(changes);
        Assert.Equal(editor.TextColor!, NativeColor(fixture, start));
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 0));
        editor.Undo();
        Assert.EndsWith("class A { }\n", editor.Document.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task NativeTypingIsReclassifiedAndUndoRestoresSource() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("class C { }");
        var editor = fixture.Editor;
        await editor.RefreshHighlightingAsync();
        editor.SelectedRange = new RichTextRange(0, 0);
        fixture.Handler.PlatformView.Document.Selection.SetText(TextSetOptions.None, "public ");
        Assert.Equal("public class C { }", editor.Document.Text);
        await editor.RefreshHighlightingAsync();
        Assert.Equal(TokenColor(editor, "keyword"), NativeColor(fixture, 0));
        Assert.All(editor.TextView.Document.CurrentSnapshot.Runs, run => Assert.Null(run.Format.ForegroundColor));
        Assert.Null(editor.TextView.Document.CurrentSnapshot.Runs.First(run => run.Start <= 6 && run.End > 6).Format.ForegroundColor);
        editor.Undo();
        Assert.Equal("class C { }", editor.Document.Text);
        Assert.False(editor.CanUndo);
        await editor.RefreshHighlightingAsync();
        Assert.True(editor.CanRedo);
    });

    [Fact]
    public Task HighlightingSurvivesFocusChangesWithoutChangingHistory() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("class Example { string text = \"hello\"; }");
        var button = new Microsoft.UI.Xaml.Controls.Button { Content = "Move focus" };
        var grid = new Microsoft.UI.Xaml.Controls.Grid();
        grid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition());
        grid.RowDefinitions.Add(new Microsoft.UI.Xaml.Controls.RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
        Microsoft.UI.Xaml.Controls.Grid.SetRow(button, 1);
        grid.Children.Add(fixture.Handler.ContainerView ?? fixture.Handler.PlatformView);
        grid.Children.Add(button);
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = grid;
        try
        {
            window.Activate();
            await fixture.Editor.RefreshHighlightingAsync();
            await Task.Delay(100);
            var version = fixture.Editor.Document.Version;
            for (var i = 0; i < 3; i++)
            {
                fixture.Handler.PlatformView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                await Task.Delay(30);
                Assert.Equal(TokenColor(fixture.Editor, "keyword"), NativeColor(fixture, 0));
                button.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                await Task.Delay(30);
                Assert.Equal(TokenColor(fixture.Editor, "keyword"), NativeColor(fixture, 0));
            }
            Assert.Equal(version, fixture.Editor.Document.Version);
            Assert.False(fixture.Editor.CanUndo);
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task RecoloringDoesNotSplitNativeTypingUndoUnits() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Empty);
        fixture.Handler.PlatformView.Document.Selection.TypeText("int");
        await fixture.Editor.RefreshHighlightingAsync();
        fixture.Handler.PlatformView.Document.Selection.TypeText(" x");
        await fixture.Editor.RefreshHighlightingAsync();
        Assert.Equal("int x", fixture.Editor.Document.Text);
        fixture.Editor.Undo();
        Assert.Empty(fixture.Editor.Document.Text);
        Assert.False(fixture.Editor.CanUndo);
        Assert.True(fixture.Editor.CanRedo);
    });

    [Fact]
    public Task NativeNewLineAndAutomaticIndentShareOneUndoUnit() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("    value");
        var editor = fixture.Editor;
        editor.SelectedRange = new RichTextRange(editor.Document.Length, 0);
        fixture.Handler.PlatformView.Document.Selection.TypeText("\r");
        await Task.Delay(100);
        Assert.Equal("    value\n    ", editor.Document.Text);
        editor.Undo();
        Assert.Equal("    value", editor.Document.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task DisablingColoringClearsNativeColorsWithoutATextEdit() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("return 42;");
        await fixture.Editor.RefreshHighlightingAsync();
        fixture.Editor.LanguageServer = null;
        await fixture.Editor.RefreshHighlightingAsync();
        Assert.Empty(fixture.Editor.Tokens);
        Assert.Equal(fixture.Editor.TextColor!, NativeColor(fixture, 0));
        Assert.False(fixture.Editor.CanUndo);
    });

    [Fact]
    public Task StaleClassificationCannotOverwriteANewerDocument() => WindowsTestHost.RunAsync(async () =>
    {
        var highlighter = new BlockingLanguageServerResponse();
        using var fixture = new CodeEditorFixture("old");
        fixture.LanguageServer.SemanticTokensHandler = highlighter.GetTokensAsync;
        var pending = fixture.Editor.RefreshHighlightingAsync();
        await highlighter.Started.Task;
        fixture.Editor.Document = CodeDocument.FromPlainText("class New {}");
        fixture.LanguageServer.SemanticTokensHandler = null;
        await fixture.Editor.RefreshHighlightingAsync();
        highlighter.Release.TrySetResult();
        await pending;
        var token = Assert.Single(fixture.Editor.Tokens);
        Assert.Equal((0, 5), (token.Start, token.Length));
        Assert.Equal("class New {}", fixture.Editor.Document.Text);
    });

    [Fact]
    public Task InvalidProviderRangesAreRejectedBeforeNativeFormatting() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("x");
        fixture.LanguageServer.SemanticTokensHandler = (_, _) => Task.FromResult<SemanticTokens?>(new([0, 0, 1, 100, 0]));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Editor.RefreshHighlightingAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Editor.Document.Version);
        Assert.False(fixture.Editor.CanUndo);
    });

    [Fact]
    public Task DisconnectCancelsPendingClassification() => WindowsTestHost.RunAsync(async () =>
    {
        var highlighter = new BlockingLanguageServerResponse();
        var fixture = new CodeEditorFixture("old");
        fixture.LanguageServer.SemanticTokensHandler = highlighter.GetTokensAsync;
        var pending = fixture.Editor.RefreshHighlightingAsync();
        await highlighter.Started.Task;
        fixture.Dispose();
        highlighter.Release.TrySetResult();
        await pending;
        Assert.Null(fixture.Editor.NativeAdapter);
        Assert.Empty(fixture.Editor.Tokens);
    });

    [Fact]
    public Task RichClipboardContentPastesAsPlainSourceAndRemainsUndoable() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Empty);
        var package = new DataPackage();
        package.SetRtf(@"{\rtf1\ansi{\colortbl;\red255\green0\blue0;}\b\cf1 class C}");
        package.SetText("class C");
        Clipboard.SetContent(package);
        await fixture.Editor.PasteAsync();
        Assert.Equal("class C", fixture.Editor.Document.Text);
        Assert.All(fixture.Editor.TextView.Document.CurrentSnapshot.Runs, run =>
        {
            Assert.False(run.Format.Bold);
            Assert.Null(run.Format.ForegroundColor);
        });
        await fixture.Editor.RefreshHighlightingAsync();
        fixture.Editor.Undo();
        Assert.Empty(fixture.Editor.Document.Text);
    });

    [Fact]
    [WinRT.DynamicWindowsRuntimeCast(typeof(UIElement))]
    public Task GutterUsesNativeLayoutAndTracksScrolling() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}")));
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 350));
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        try
        {
            window.Activate();
            await fixture.Editor.RefreshHighlightingAsync();
            await Task.Delay(150);
            var lines = fixture.Editor.NativeAdapter!.GetVisibleLines();
            Assert.NotEmpty(lines);
            Assert.Equal(1, lines[0].Number);
            Assert.True(lines.Zip(lines.Skip(1)).All(pair => pair.First.Top < pair.Second.Top));
            new CodeEditorActions(fixture.Editor).GoToLine(90);
            await Task.Delay(100);
            lines = fixture.Editor.NativeAdapter!.GetVisibleLines();
            Assert.NotEmpty(lines);
            var scrollViewer = Descendants(fixture.Handler.PlatformView).OfType<Microsoft.UI.Xaml.Controls.ScrollViewer>().First();
            Assert.True(lines[0].Number > 1, $"First={lines[0]}, selected={fixture.Editor.SelectedRange}, native={fixture.Handler.PlatformView.Document.Selection.StartPosition}, scroll={scrollViewer.VerticalOffset}, height={fixture.Handler.PlatformView.ActualHeight}, content={scrollViewer.Content?.GetType()}, origin={(scrollViewer.Content as Microsoft.UI.Xaml.UIElement)?.TransformToVisual(fixture.Handler.PlatformView).TransformPoint(new Windows.Foundation.Point(0, 0))}");
            Assert.Contains(lines, line => line.Number == 90);
        }
        finally
        {
            window.Content = null;
        }
    });

    [Fact]
    public Task FoldingGutterKeepsPartialLinesAndSkipsJoinedLines() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("first row\nsecond row\nthird row");
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 350));
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        try
        {
            window.Activate();
            fixture.Editor.Folding.Collapse(new(0, 5));
            await Task.Delay(80);
            Assert.Equal(new[] { 1, 2, 3 }, fixture.Editor.NativeAdapter!.GetVisibleLines().Select(line => line.Number));
            fixture.Editor.Folding.SetCollapsedRanges((RichTextRange[])[new(5, 12)]);
            await Task.Delay(80);
            Assert.Equal(new[] { 1, 3 }, fixture.Editor.NativeAdapter.GetVisibleLines().Select(line => line.Number));
            fixture.Editor.Folding.ExpandAll();
            await Task.Delay(80);
            Assert.Equal(new[] { 1, 2, 3 }, fixture.Editor.NativeAdapter.GetVisibleLines().Select(line => line.Number));
        }
        finally { window.Content = null; }
    });

    private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Descendants(Microsoft.UI.Xaml.DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    [Theory]
    [InlineData(4, 7, true)]
    [InlineData(4, 8, false)] // Include ccc's newline without folding ddd.
    [InlineData(5, 4, true)] // Partial endpoint lines still fold as whole lines.
    [WinRT.DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    [WinRT.DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Controls.Button))]
    public Task SampleFoldingKeepsHeaderEllipsisAndFollowingLine(int start, int length, bool clickEllipsis) => WindowsTestHost.RunAsync(async () =>
    {
        const string source = "aaa\nbbb\nccc\nddd";
        using var fixture = new CodeEditorFixture(source);
        var editor = fixture.Editor;
        using var indicators = new CodeEditorFoldIndicators(editor);
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 350));
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        try
        {
            window.Activate();
            editor.Document.Edit(edit => edit.InsertText(editor.Document.Length, "!"));
            editor.Undo();
            await editor.RefreshHighlightingAsync();
            await Task.Delay(100);
            var snapshot = editor.Document.CurrentSnapshot;
            var before = editor.NativeAdapter!.GetVisibleLines();
            editor.SelectedRange = new(start, length);
            new CodeEditorActions(editor).CollapseSelection();
            await Task.Delay(100);

            Assert.Equal(new RichTextRange(7, 4), Assert.Single(editor.Folding.CollapsedRanges));
            Assert.Equal(source, editor.Document.Text);
            Assert.Same(snapshot, editor.Document.CurrentSnapshot);
            Assert.True(editor.CanRedo);
            var visible = editor.NativeAdapter.GetVisibleLines();
            Assert.Equal(new[] { 1, 2, 4 }, visible.Select(line => line.Number));
            var header = visible.Single(line => line.Number == 2);
            Assert.Equal(before.Single(line => line.Number == 2).Top, header.Top);
            Assert.Equal(before.Single(line => line.Number == 3).Top, visible.Single(line => line.Number == 4).Top);

            var suffix = Assert.Single(editor.Adornments, item => item.Placement == RichTextAdornmentPlacement.Text);
            var ellipsis = Assert.IsType<Microsoft.Maui.Controls.Button>(suffix.View);
            Assert.Equal("…", ellipsis.Text);
            var gutter = Assert.IsType<Microsoft.Maui.Controls.HorizontalStackLayout>(
                Assert.Single(editor.Adornments, item => item.Placement == RichTextAdornmentPlacement.LeftMargin).View);
            Assert.InRange(Math.Abs(ellipsis.Bounds.Center.Y - header.Top - header.Height / 2), 0, 1);
            Assert.InRange(Math.Abs(gutter.Bounds.Center.Y - header.Top - header.Height / 2), 0, 1);

            var nativeEllipsis = (Microsoft.UI.Xaml.FrameworkElement)ellipsis.Handler!.PlatformView!;
            var origin = nativeEllipsis.TransformToVisual(fixture.Handler.PlatformView).TransformPoint(new(0, 0));
            Assert.InRange(Math.Abs(origin.Y - ellipsis.Bounds.Y), 0, 1);
            Assert.True(origin.X > gutter.Bounds.Right, "The ellipsis follows the header in the text area.");

            var button = clickEllipsis ? ellipsis : (Microsoft.Maui.Controls.Button)gutter[0];
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer((Microsoft.UI.Xaml.Controls.Button)button.Handler!.PlatformView!);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            Assert.Empty(editor.Folding.CollapsedRanges);
            Assert.Empty(editor.Adornments);
            Assert.Equal(new[] { 1, 2, 3, 4 }, editor.NativeAdapter.GetVisibleLines().Select(line => line.Number));
            Assert.Same(snapshot, editor.Document.CurrentSnapshot);
            editor.Redo();
            Assert.Equal(source + "!", editor.Document.Text);
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task RegistrationCreatesTheCompleteControlAndWrappedGutter() => WindowsTestHost.RunAsync(async () =>
    {
        var app = _controlTestApp ??= MauiApp.CreateBuilder().UseMauiApp<CodeTestApplication>().UseCodeEditor().Build();
        var editor = new CodeEditor
        {
            Document = CodeDocument.FromPlainText("// A long logical line wraps across several visual rows. " + new string('x', 180) + "\nclass Example\n{\n    string value = \"Hello\";\n}\n"),
            Theme = static _ => Microsoft.Maui.Graphics.Colors.Purple,
            WordWrap = true,
        };
        var native = editor.ToPlatform(new MauiContext(app.Services));
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 400));
        window.Content = native;
        try
        {
            window.Activate();
            ((IView)editor).Measure(660, 350);
            ((IView)editor).Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, 660, 350));
            await editor.RefreshHighlightingAsync();
            await Task.Delay(150);
            Assert.NotNull(editor.NativeAdapter);
            Assert.True(editor.Width > 0);
            var lines = editor.NativeAdapter.GetVisibleLines();
            Assert.Contains(lines, line => line.Number == 6);
            Assert.True(lines[1].Top - lines[0].Top > lines[0].Height * 2);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(native);
            Assert.True(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);
        }
        finally
        {
            window.Content = null;
            editor.Handler?.DisconnectHandler();
            editor.TextView.Handler?.DisconnectHandler();
        }
    });

    [Fact]
    public Task LanguageServerTextEditsUseTheRequestedSnapshotAndOneUndoUnit() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("😀 foo\nbar");
        var editor = fixture.Editor;
        editor.SelectAll();
        var snapshot = editor.Document.CurrentSnapshot;
        Assert.Equal(new LspPosition(0, 3), editor.GetLspPosition(3));
        Assert.Equal(7, editor.GetOffset(new LspPosition(1, 0)));
        Assert.True(editor.ApplyLanguageServerEdits((LspTextEdit[])[
            new(new(new(0, 3), new(0, 6)), "name"),
            new(new(new(1, 0), new(1, 3)), "baz")], snapshot));
        Assert.Equal("😀 name\nbaz", editor.Document.Text);
        Assert.Equal(editor.Document.Length, editor.SelectedRange.Length);
        await editor.RefreshHighlightingAsync();
        editor.Undo();
        Assert.Equal(snapshot.Text, editor.Document.Text);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.False(editor.ApplyLanguageServerEdits((LspTextEdit[])[new(new(new(0, 0), new(0, 2)), "x")], snapshot));
        var otherSnapshot = editor.Document.CurrentSnapshot;
        editor.Document = new(otherSnapshot.Text);
        Assert.False(editor.ApplyLanguageServerEdits((LspTextEdit[])[new(new(new(0, 0), new(0, 2)), "x")], otherSnapshot));
    });

    [Fact]
    public Task InvalidLanguageServerEditsLeaveSourceAndHistoryUntouched() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new CodeEditorFixture("abcd");
        var editor = fixture.Editor;
        var snapshot = editor.Document.CurrentSnapshot;
        Assert.Throws<ArgumentException>(() => editor.ApplyLanguageServerEdits((LspTextEdit[])[
            new(new(new(0, 0), new(0, 3)), "x"), new(new(new(0, 2), new(0, 4)), "y")], snapshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ApplyLanguageServerEdits((LspTextEdit[])[new(new(new(0, 8), new(0, 9)), "x")], snapshot));
        editor.MaxLength = 4;
        Assert.False(editor.ApplyLanguageServerEdits((LspTextEdit[])[new(new(new(0, 0), new(0, 0)), "long")], snapshot));
        editor.IsReadOnly = true;
        Assert.False(editor.ApplyLanguageServerEdits((LspTextEdit[])[new(new(new(0, 0), new(0, 1)), "x")], snapshot));
        Assert.Equal("abcd", editor.Document.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task LanguageServerDiagnosticsFollowDocumentIdentityAndVersion() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("class C {}");
        var editor = fixture.Editor;
        var session = (await editor.GetLanguageDocumentAsync())!;
        Assert.Equal(editor.Document.Uri, session.Uri);
        Assert.Equal("csharp", session.LanguageId);
        var initialRevision = session.CurrentSnapshot.Version;
        var sourceVersion = editor.Document.Version;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.DiagnosticsChanged += (_, _) => changed.TrySetResult();
        LspDiagnostic[] diagnostics = [new() { Range = new(new(0, 0), new(0, 5)), Message = "test", Severity = 2 }];
        await fixture.LanguageServer.PublishDiagnosticsAsync(new(editor.Document.Uri.AbsoluteUri, diagnostics, initialRevision));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("test", Assert.Single(editor.Diagnostics).Message);
        Assert.Equal(sourceVersion, editor.Document.Version);
        Assert.False(editor.CanUndo);
        editor.Document.Edit(edit => edit.InsertText(editor.Document.Length, " "));
        Assert.Empty(editor.Diagnostics);
        await fixture.LanguageServer.PublishDiagnosticsAsync(new(editor.Document.Uri.AbsoluteUri, diagnostics, initialRevision));
        await Task.Delay(30);
        Assert.Empty(editor.Diagnostics);
        await fixture.LanguageServer.PublishDiagnosticsAsync(new("untitled:another", diagnostics, session.CurrentSnapshot.Version));
        await Task.Delay(30);
        Assert.Empty(editor.Diagnostics);
        editor.LanguageServer = null;
        await editor.GetLanguageDocumentAsync();
        Assert.Empty(editor.Diagnostics);
    });

    [Fact]
    public Task ThemeReceivesCompleteTokensAndRecolorsWithoutARequest() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("abc", new(["customType"], ["readonly", "static"]));
        fixture.LanguageServer.SemanticTokensHandler = (_, _) => Task.FromResult<SemanticTokens?>(new([0, 1, 1, 0, 3, 0, 1, 1, 0, 0]));
        var editor = fixture.Editor;
        var uiThread = Environment.CurrentManagedThreadId;
        var seen = new List<SemanticToken>();
        editor.Theme = token =>
        {
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            seen.Add(token);
            return token.Type == "customType" && token.Modifiers.Contains("readonly") ? Microsoft.Maui.Graphics.Colors.Magenta : null;
        };
        await editor.RefreshHighlightingAsync();
        Assert.Equal(2, editor.Tokens.Count);
        Assert.Same(editor.Tokens[0], seen[0]);
        Assert.Equal((1, 1), (seen[0].Start, seen[0].Length));
        Assert.Equal(new[] { "readonly", "static" }, seen[0].Modifiers);
        Assert.Equal(Microsoft.Maui.Graphics.Colors.Magenta, NativeColor(fixture, 1));
        Assert.Equal(editor.TextColor, NativeColor(fixture, 2));

        var requests = fixture.LanguageServer.SemanticTokenRequests;
        var tokens = editor.Tokens;
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Decorations.Changed += (_, _) => cleared.TrySetResult();
        editor.Theme = null;
        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(editor.TextColor, NativeColor(fixture, 1));
        Assert.Same(tokens, editor.Tokens);
        Assert.Equal(requests, fixture.LanguageServer.SemanticTokenRequests);
        Assert.Equal(0, editor.Document.Version);
        Assert.False(editor.CanUndo);
    });

    private sealed partial class CodeTestApplication : Microsoft.Maui.Controls.Application;

    private static Microsoft.Maui.Graphics.Color TokenColor(CodeEditor editor, string type) => editor.Theme!(new(0, 1, type, []))!;

    private static Microsoft.Maui.Graphics.Color NativeColor(CodeEditorFixture fixture, int position)
    {
        var color = fixture.Handler.PlatformView.Document.GetRange(position, position + 1).CharacterFormat.ForegroundColor;
        return Microsoft.Maui.Graphics.Color.FromRgba(color.R, color.G, color.B, color.A);
    }

    private sealed class BlockingLanguageServerResponse
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task<SemanticTokens?> GetTokensAsync(string text, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task;
            return new([0, 0, text.Length, 2, 0]);
        }
    }

    private sealed partial class CodeEditorFixture : IDisposable
    {
        internal TestLanguageServer LanguageServer { get; }
        internal CodeEditor Editor { get; }
        internal RichEditorHandler Handler { get; } = new();
        internal CodeEditorFixture(string source, SemanticTokensLegend? legend = null)
        {
            LanguageServer = new(legend);
            Editor = new CodeEditor
            {
                Document = CodeDocument.FromPlainText(source, languageId: "csharp"), LanguageServer = LanguageServer.Client,
                TextColor = Microsoft.Maui.Graphics.Colors.Black, BackgroundColor = Microsoft.Maui.Graphics.Colors.White,
                Theme = static token => token.Type switch
                {
                    "keyword" => Microsoft.Maui.Graphics.Colors.Blue,
                    "string" => Microsoft.Maui.Graphics.Colors.Maroon,
                    "comment" => Microsoft.Maui.Graphics.Colors.Green,
                    "number" => Microsoft.Maui.Graphics.Colors.DarkCyan,
                    "macro" => Microsoft.Maui.Graphics.Colors.Purple,
                    _ => null,
                },
            };
            Handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
            Editor.TextView.Handler = Handler;
        }
        public void Dispose()
        {
            Editor.TextView.Handler = null;
            ((IElementHandler)Handler).DisconnectHandler();
            LanguageServer.Dispose();
        }
    }
}
