using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.UI.Text;
using CodeEdit.Maui;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public class CodeEditorTests
{
    private static Microsoft.UI.Xaml.Window? _gutterWindow;
    private static MauiApp? _controlTestApp;
    [Fact]
    public void CSharpColoringKeepsCommentsStringsAndEscapedIdentifiersSeparate()
    {
        const string source = "#nullable enable\npublic class @class { string s = @\"if \"\"else\"\"\"; /* return */ int x = 0xFE+1; // true\n}";
        var tokens = new CSharpSyntaxHighlighter().Highlight(source, TestContext.Current.CancellationToken);
        var classified = tokens.Select(token => (token.Kind, Text: source.Substring(token.Range.Start, token.Range.Length))).ToArray();
        Assert.Contains((CodeTokenKind.Preprocessor, "#nullable enable"), classified);
        Assert.Contains((CodeTokenKind.String, "@\"if \"\"else\"\"\""), classified);
        Assert.Contains((CodeTokenKind.Comment, "/* return */"), classified);
        Assert.Contains((CodeTokenKind.Comment, "// true"), classified);
        Assert.Contains((CodeTokenKind.Number, "0xFE"), classified);
        Assert.Contains((CodeTokenKind.Number, "1"), classified);
        Assert.DoesNotContain(classified, item => item.Text == "@class");
        Assert.DoesNotContain(classified, item => item.Kind == CodeTokenKind.Keyword && item.Text is "if" or "else" or "return" or "true");
    }

    [Theory]
    [InlineData("\"\"\"\nraw // text\n\"\"\"", CodeTokenKind.String)]
    [InlineData("$$\"\"\"raw {{value}}\"\"\"", CodeTokenKind.String)]
    [InlineData("$@\"first\nsecond\"", CodeTokenKind.String)]
    [InlineData("'\\n'", CodeTokenKind.String)]
    [InlineData("/* unfinished", CodeTokenKind.Comment)]
    [InlineData("1.25e-10", CodeTokenKind.Number)]
    public void LexicalTokensUseCompleteUtf16Ranges(string text, CodeTokenKind kind)
    {
        var token = Assert.Single(new CSharpSyntaxHighlighter().Highlight(text, TestContext.Current.CancellationToken));
        Assert.Equal(kind, token.Kind);
        Assert.Equal(new RichTextRange(0, text.Length), token.Range);
    }

    [Fact]
    public void LongInvalidPrefixesRemainLinearAndCancellationIsObserved()
    {
        var highlighter = new CSharpSyntaxHighlighter();
        Assert.Empty(highlighter.Highlight(new string('$', 100_000), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => highlighter.Highlight("/*" + new string('x', 100_000), cancellation.Token));
    }

    [Fact]
    public Task PlainTextLoadingNormalizesLineEndingsWithoutHistory() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("a\r\nb\rc\n") };
        Assert.Equal("a\nb\nc\n", editor.Document.Text);
        Assert.Equal(4, editor.LineCount);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, editor.Document.Version);
        Assert.Equal(new RichTextRange(6, 0), editor.GetLineRange(4));
    });

    [Fact]
    public Task BlockIndentExcludesAnUnselectedFollowingLineAndIsAtomic() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("one\ntwo\nthree"), SelectedRange = new RichTextRange(0, 8) };
        var changes = 0;
        editor.TextChanged += (_, _) => changes++;
        editor.Indent();
        Assert.Equal("    one\n    two\nthree", editor.Document.Text);
        Assert.Equal(1, changes);
        editor.Undo();
        Assert.Equal("one\ntwo\nthree", editor.Document.Text);
        Assert.False(editor.CanUndo);
        editor.Redo();
        editor.SelectedRange = new RichTextRange(0, 16);
        editor.Outdent();
        Assert.Equal("one\ntwo\nthree", editor.Document.Text);
    });

    [Fact]
    public Task IndentUsesTheNextStopAndOutdentHandlesTabs() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("ab"), SelectedRange = new RichTextRange(2, 0) };
        editor.Indent();
        Assert.Equal("ab  ", editor.Document.Text);
        Assert.Equal(new RichTextRange(4, 0), editor.SelectedRange);
        editor.Document = RichTextDocument.FromPlainText("\tfirst\n  second");
        editor.SelectAll();
        editor.Outdent();
        Assert.Equal("first\nsecond", editor.Document.Text);
        editor.UseTabs = true;
        editor.SelectAll();
        editor.Indent();
        Assert.Equal("\tfirst\n\tsecond", editor.Document.Text);
    });

    [Fact]
    public Task NewLineCopiesOnlyIndentationBeforeTheCaret() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("    call();"), SelectedRange = new RichTextRange(11, 0) };
        editor.InsertNewLine();
        Assert.Equal("    call();\n    ", editor.Document.Text);
        Assert.Equal(new CodePosition(2, 5), editor.CaretPosition);
        editor.Undo();
        Assert.Equal("    call();", editor.Document.Text);
        editor.SelectedRange = new RichTextRange(2, 0);
        editor.InsertNewLine();
        Assert.Equal("  \n    call();", editor.Document.Text);
        Assert.Equal(new CodePosition(2, 3), editor.CaretPosition);
    });

    [Fact]
    public Task CommentsToggleAfterIndentationAndIgnoreSelectedBlankLines() => WindowsTestHost.RunAsync(() =>
    {
        const string source = "  first();\n\n\tsecond();\n";
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText(source) };
        editor.SelectAll();
        editor.ToggleLineComment();
        Assert.Equal("  // first();\n\n\t// second();\n", editor.Document.Text);
        editor.SelectAll();
        editor.ToggleLineComment();
        Assert.Equal(source, editor.Document.Text);
        editor.Undo();
        Assert.Contains("//", editor.Document.Text);
    });

    [Fact]
    public Task ReadOnlyAndLengthLimitsPreventPartialCodeCommands() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("a\na"), MaxLength = 4 };
        editor.SelectAll();
        editor.Indent();
        Assert.Equal("a\na", editor.Document.Text);
        Assert.Equal(0, editor.ReplaceAll("a", "long"));
        Assert.False(editor.CanUndo);
        editor.IsReadOnly = true;
        Assert.False(editor.Commands.Indent.CanExecute(null));
        editor.Outdent();
        editor.ToggleLineComment();
        editor.InsertNewLine();
        Assert.Equal("a\na", editor.Document.Text);
    });

    [Fact]
    public Task OverLimitDocumentsCanStillShrink() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("    value"), MaxLength = 2 };
        editor.Outdent();
        Assert.Equal("value", editor.Document.Text);
        editor.Undo();
        Assert.Equal("    value", editor.Document.Text);
    });

    [Fact]
    public Task SearchWrapsAndReplaceAllUsesOneUndoUnit() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("foo food FOO foo\u0301 foo" ) };
        var options = new CodeSearchOptions(WholeWord: true);
        Assert.Equal(3, editor.FindAll("foo", options).Count);
        Assert.Equal(2, editor.FindAll("foo", new CodeSearchOptions(MatchCase: true, WholeWord: true)).Count);
        Assert.Equal(new RichTextRange(0, 3), editor.FindNext("foo", options));
        Assert.Equal(new RichTextRange(9, 3), editor.FindNext("foo", options));
        Assert.Equal(new RichTextRange(18, 3), editor.FindNext("foo", options));
        Assert.Null(editor.FindNext("foo", options, wrap: false));
        Assert.Equal(new RichTextRange(0, 3), editor.FindNext("foo", options));
        Assert.Equal(new RichTextRange(18, 3), editor.FindPrevious("foo", options));
        var source = editor.Document.Text;
        Assert.Equal(3, editor.ReplaceAll("foo", "bar", options));
        Assert.Equal("bar food bar foo\u0301 bar", editor.Document.Text);
        editor.Undo();
        Assert.Equal(source, editor.Document.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task DocumentOwnershipValidationKeepsTheWrapperAndNativeEditorInSync() => WindowsTestHost.RunAsync(() =>
    {
        var first = new CodeEditor { Document = RichTextDocument.FromPlainText("owned") };
        var second = new CodeEditor();
        var original = second.Document;
        second.Document = first.Document;
        Assert.Same(original, second.Document);
        Assert.Same(original, second.TextView.Document);
        GC.KeepAlive(first);
    });

    [Fact]
    public Task PositionsUseUtf16AndTrackDocumentReplacement() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = RichTextDocument.FromPlainText("😀\n日本語\n") };
        editor.GoToLine(2, 3);
        Assert.Equal(new RichTextRange(5, 0), editor.SelectedRange);
        Assert.Equal(new CodePosition(2, 3), editor.CaretPosition);
        editor.GoToLine(100, 100);
        Assert.Equal(new CodePosition(3, 1), editor.CaretPosition);
        var previous = editor.Document;
        editor.Document = RichTextDocument.FromPlainText("x");
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
        Assert.Equal(editor.Theme.KeywordColor, NativeColor(fixture, 0));
        editor.Theme = CodeEditorTheme.Dark;
        await editor.RefreshHighlightingAsync();
        Assert.Same(document, editor.Document);
        Assert.Equal(source, editor.Document.Text);
        Assert.Equal(0, textChanges);
        Assert.Equal(selection, editor.SelectedRange);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
        Assert.Equal(editor.Theme.KeywordColor, NativeColor(fixture, 0));
        editor.Redo();
        Assert.Equal("class D { }", editor.Document.Text);
    });

    [Fact]
    public Task RecoloringUpdatesOnlyRangesWhoseClassificationChanged() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Concat(Enumerable.Repeat("class A { }\n", 40)));
        var editor = fixture.Editor;
        await editor.RefreshHighlightingAsync();
        var start = editor.Document.Text.LastIndexOf("class", StringComparison.Ordinal);
        var previousFormat = editor.Document.CurrentSnapshot.Runs.First(run => run.Range.Start <= start && run.Range.End > start).Format;
        editor.Document.Edit(edit => edit.ReplaceText(new RichTextRange(start, 5), "other", previousFormat));
        var changes = new List<RichTextChangeSet>();
        editor.ContentChanged += (_, args) => changes.Add(args.ChangeSet);
        await editor.RefreshHighlightingAsync();
        var change = Assert.Single(Assert.Single(changes).Changes);
        Assert.Equal(RichTextChangeKind.CharacterFormat, change.Kind);
        Assert.Equal(new RichTextRange(start, 5), change.NewRange);
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
        Assert.Equal(editor.Theme.KeywordColor, editor.Document.CurrentSnapshot.Runs[0].Format.ForegroundColor);
        Assert.Null(editor.Document.CurrentSnapshot.Runs.First(run => run.Start <= 6 && run.End > 6).Format.ForegroundColor);
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
        grid.Children.Add(fixture.Handler.PlatformView);
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
                Assert.Equal(fixture.Editor.Theme.KeywordColor, NativeColor(fixture, 0));
                button.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                await Task.Delay(30);
                Assert.Equal(fixture.Editor.Theme.KeywordColor, NativeColor(fixture, 0));
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
        fixture.Editor.Highlighter = null;
        await fixture.Editor.RefreshHighlightingAsync();
        Assert.Empty(fixture.Editor.Tokens);
        Assert.Equal(fixture.Editor.Theme.TextColor, NativeColor(fixture, 0));
        Assert.False(fixture.Editor.CanUndo);
    });

    [Fact]
    public Task StaleClassificationCannotOverwriteANewerDocument() => WindowsTestHost.RunAsync(async () =>
    {
        var highlighter = new BlockingHighlighter();
        using var fixture = new CodeEditorFixture("old");
        fixture.Editor.Highlighter = highlighter;
        var pending = fixture.Editor.RefreshHighlightingAsync();
        await highlighter.Started.Task;
        fixture.Editor.Document = RichTextDocument.FromPlainText("class New {}");
        fixture.Editor.Highlighter = new CSharpSyntaxHighlighter();
        await fixture.Editor.RefreshHighlightingAsync();
        highlighter.Release.TrySetResult();
        await pending;
        Assert.Equal(new RichTextRange(0, 5), Assert.Single(fixture.Editor.Tokens).Range);
        Assert.Equal("class New {}", fixture.Editor.Document.Text);
    });

    [Fact]
    public Task InvalidProviderRangesAreRejectedBeforeNativeFormatting() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("x");
        fixture.Editor.Highlighter = new InvalidHighlighter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Editor.RefreshHighlightingAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Editor.Document.Version);
        Assert.False(fixture.Editor.CanUndo);
    });

    [Fact]
    public Task DisconnectCancelsPendingClassification() => WindowsTestHost.RunAsync(async () =>
    {
        var highlighter = new BlockingHighlighter();
        var fixture = new CodeEditorFixture("old");
        fixture.Editor.Highlighter = highlighter;
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
        Assert.All(fixture.Editor.Document.CurrentSnapshot.Runs, run =>
        {
            Assert.False(run.Format.Bold);
            Assert.Null(run.Format.ForegroundColor);
        });
        await fixture.Editor.RefreshHighlightingAsync();
        fixture.Editor.Undo();
        Assert.Empty(fixture.Editor.Document.Text);
    });

    [Fact]
    public Task GutterUsesNativeLayoutAndTracksScrolling() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}")));
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 350));
        window.Content = fixture.Handler.PlatformView;
        try
        {
            window.Activate();
            await fixture.Editor.RefreshHighlightingAsync();
            await Task.Delay(150);
            var lines = fixture.Editor.NativeAdapter!.GetVisibleLines();
            Assert.NotEmpty(lines);
            Assert.Equal(1, lines[0].Number);
            Assert.True(lines.Zip(lines.Skip(1)).All(pair => pair.First.Top < pair.Second.Top));
            fixture.Editor.GoToLine(90);
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

    private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Descendants(Microsoft.UI.Xaml.DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    [Fact]
    public Task RegistrationCreatesTheCompleteControlAndWrappedGutter() => WindowsTestHost.RunAsync(async () =>
    {
        var app = _controlTestApp ??= MauiApp.CreateBuilder().UseMauiApp<CodeTestApplication>().UseCodeEditor().Build();
        var editor = new CodeEditor
        {
            Document = RichTextDocument.FromPlainText("// A long logical line wraps across several visual rows. " + new string('x', 180) + "\nclass Example\n{\n    string value = \"Hello\";\n}\n"),
            Theme = CodeEditorTheme.Dark,
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

    private sealed class CodeTestApplication : Microsoft.Maui.Controls.Application;

    private static Microsoft.Maui.Graphics.Color NativeColor(CodeEditorFixture fixture, int position)
    {
        var color = fixture.Handler.PlatformView.Document.GetRange(position, position + 1).CharacterFormat.ForegroundColor;
        return Microsoft.Maui.Graphics.Color.FromRgba(color.R, color.G, color.B, color.A);
    }

    private sealed class BlockingHighlighter : ICodeSyntaxHighlighter
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<CodeToken> Highlight(string text, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return [new(new RichTextRange(0, text.Length), CodeTokenKind.Comment)];
        }
    }

    private sealed class InvalidHighlighter : ICodeSyntaxHighlighter
    {
        public IReadOnlyList<CodeToken> Highlight(string text, CancellationToken cancellationToken = default) =>
            [new(new RichTextRange(0, text.Length + 1), CodeTokenKind.Keyword)];
    }

    private sealed class CodeEditorFixture : IDisposable
    {
        internal CodeEditor Editor { get; }
        internal RichEditorHandler Handler { get; } = new();
        internal CodeEditorFixture(string source)
        {
            Editor = new CodeEditor { Document = RichTextDocument.FromPlainText(source) };
            Handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
            Editor.TextView.Handler = Handler;
        }
        public void Dispose()
        {
            Editor.TextView.Handler = null;
            ((IElementHandler)Handler).DisconnectHandler();
        }
    }
}
