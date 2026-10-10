namespace RichEdit.Maui.Tests;

public class SharedDocumentTests
{
    [Fact]
    public Task CompositionInOneViewPreventsMutationThroughAnother() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichTextDocument.FromPlainText("abc");
        var first = new NativeEditorView { Document = document };
        var second = new NativeEditorView { Document = document };
        var revision = document.Revision;
        first.SetCompositionState(new(true, new(0, 1)));
        var invoked = false;
        Assert.Throws<InvalidOperationException>(() => document.Edit(edit =>
        {
            invoked = true;
            edit.InsertText(0, "x");
        }));
        Assert.False(invoked);
        Assert.False(second.TryApplyEdits(revision, (RichTextEdit[])[new(new(0, 0), "x")]));
        Assert.Equal(revision, document.Revision);
        first.SetCompositionState(default);
        Assert.True(second.TryApplyEdits(revision, (RichTextEdit[])[new(new(0, 0), "x")]));
        Assert.Equal("xabc", document.Text);
    });

    [Fact]
    public Task FailingObserverDoesNotPreventPeerPublication() => WindowsTestHost.RunAsync(() =>
    {
        var document = RichTextDocument.FromPlainText("abc");
        var first = new NativeEditorView { Document = document };
        var second = new NativeEditorView { Document = document };
        var failure = new InvalidOperationException("observer failure");
        first.ContentChanged += (_, _) => throw failure;
        var notified = false;
        second.ContentChanged += (_, _) => notified = true;
        first.SelectedRange = new(1, 0);
        second.SelectedRange = new(3, 0);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => first.Selection.ReplaceText("X")));
        Assert.Equal("aXbc", document.Text);
        Assert.Equal(new TextSpan(4, 0), second.SelectedRange);
        Assert.True(notified);
        Assert.True(document.CanUndo);
        second.Document = new RichTextDocument();
    });

    [Fact]
    public Task SharedDocumentRejectsAnotherViewThreadBeforeAttachment() => WindowsTestHost.RunAsync(async () =>
    {
        var first = new NativeEditorView { Document = RichTextDocument.FromPlainText("source") };
        await Task.Run(() =>
        {
            var other = new NativeEditorView();
            var previous = other.Document;
            Assert.Throws<InvalidOperationException>(() => other.Document = first.Document);
            Assert.Same(previous, other.Document);
        });
        Assert.Equal("source", first.Document.Text);
    });
}
