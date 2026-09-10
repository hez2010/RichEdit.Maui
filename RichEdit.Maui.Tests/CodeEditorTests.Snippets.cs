using CodeEdit.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Theory]
    [InlineData(4, 12, "same")]
    [InlineData(6, 7, "shared")]
    public Task OverlappingSnippetPlaceholdersEndTheSession(int start, int length, string replacement) => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor();
        var host = new Grid { Children = { editor } };
        using var features = new CodeEditorFeatures(editor, host);
        features.InsertSnippet();
        Assert.True(editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(new(start, length), replacement)]));
        var snapshot = editor.Document.CurrentSnapshot;

        // Paint synchronously so a regression is reported as a test failure instead of a dispatcher crash.
        features.NextPlaceholder(previous: true);
        Assert.False(features.NextPlaceholder());
        Assert.Same(snapshot, editor.Document.CurrentSnapshot);
        Assert.All(editor.TextView.PresentationSnapshot.Runs, run => Assert.Null(run.Format.BackgroundColor));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SnippetUndoEndsOverlappingPlaceholdersInTheQueuedRepaint(bool redo) => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new CodeEditor { Document = new CodeDocument("name"), SelectedRange = new(0, 4) };
        var host = new Grid { Children = { editor } };
        using var features = new CodeEditorFeatures(editor, host);
        features.InsertSnippet();
        editor.Undo();
        if (redo) editor.Redo();
        var snapshot = editor.Document.CurrentSnapshot;

        var dispatched = new TaskCompletionSource();
        Assert.True(editor.Dispatcher.Dispatch(() => dispatched.SetResult()));
        await dispatched.Task;

        Assert.Equal(redo ? "var name = value;" : "name", editor.Document.Text);
        Assert.False(features.NextPlaceholder());
        Assert.Same(snapshot, editor.Document.CurrentSnapshot);
        Assert.All(editor.TextView.PresentationSnapshot.Runs, run => Assert.Null(run.Format.BackgroundColor));
    });

    [Fact]
    public Task EmptySnippetPlaceholderStillTracksReplacementText() => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new CodeEditor();
        var host = new Grid { Children = { editor } };
        using var features = new CodeEditorFeatures(editor, host);
        features.InsertSnippet();
        Assert.True(editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(editor.SelectedRange, "")]));

        var dispatched = new TaskCompletionSource();
        Assert.True(editor.Dispatcher.Dispatch(() => dispatched.SetResult()));
        await dispatched.Task;

        Assert.True(features.NextPlaceholder(previous: true));
        Assert.Equal(new RichTextRange(4, 0), editor.SelectedRange);
        Assert.True(editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(editor.SelectedRange, "answer")]));
        Assert.True(features.NextPlaceholder());
        Assert.Equal("value", editor.Selection.Text);
        Assert.True(features.NextPlaceholder(previous: true));
        Assert.Equal("answer", editor.Selection.Text);
    });
}
