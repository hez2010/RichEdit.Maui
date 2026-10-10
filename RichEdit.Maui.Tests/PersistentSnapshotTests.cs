namespace RichEdit.Maui.Tests;

public sealed class PersistentSnapshotTests
{
    [Fact]
    public void RichEditsAndHistoryShareUnchangedTextChunks()
    {
        var text = string.Concat(Enumerable.Repeat("a paragraph\n", 4000));
        var document = RichTextDocument.FromPlainText(text);
        var before = document.CurrentSnapshot;
        var untouched = before.TextBuffer.GetChunks(new(0, before.Length)).ElementAt(10);

        document.Edit(edit => edit.InsertText(0, "hello\n"));
        var after = document.CurrentSnapshot;
        Assert.Equal("hello\n" + text, after.Text);
        Assert.Equal("hello", after.GetText(new(0, 5)));
        Assert.Contains(untouched, after.TextBuffer.GetChunks(new(0, after.Length)));
        Assert.Equal(text, before.Text);

        document.Undo();
        Assert.Same(before.TextBuffer, document.CurrentSnapshot.TextBuffer);
        document.Redo();
        Assert.Same(after.TextBuffer, document.CurrentSnapshot.TextBuffer);
    }

    [Fact]
    public void FormattingAndMetadataShareTheTextRoot()
    {
        var document = RichTextDocument.FromPlainText("first\nsecond\n");
        var text = document.CurrentSnapshot.TextBuffer;
        document.Edit(edit =>
        {
            edit.UpdateCharacterFormat(new(0, 5), format => format with { FontWeight = 700 });
            edit.SetMetadata("title", "test");
        });
        Assert.Same(text, document.CurrentSnapshot.TextBuffer);
        Assert.Equal("second", document.CurrentSnapshot.GetText(new(6, 6)));
    }
}
