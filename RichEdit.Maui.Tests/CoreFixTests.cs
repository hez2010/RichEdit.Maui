namespace RichEdit.Maui.Tests;

public sealed class CoreFixTests
{
    [Fact]
    public void UndoOfALinkAdditionReportsTheRestoredLinkRange()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "0123456789"));
        document.Edit(edit => edit.SetLink(
            new RichTextRange(2, 6),
            "https://example.test"));

        RichTextChangeSet? changes = null;
        document.Changed += (_, eventArgs) => changes = eventArgs.ChangeSet;
        document.Undo();

        Assert.NotNull(changes);
        Assert.Empty(document.CurrentSnapshot.Links);
        var affected = changes.GetAffectedRange(document.Length);
        Assert.True(affected.Start <= 2);
        Assert.True(affected.End >= 8);
    }

    [Fact]
    public void DeletingAFieldsResultTextRemovesTheField()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "before after"));
        document.Edit(edit => edit.InsertField(7, "DATE", "2026-07-30"));
        Assert.Single(document.CurrentSnapshot.Fields);

        // Mirrors a native undo readback where the platform removed the
        // field's result text: the model overlay must not survive as a
        // ghost zero-length field.
        document.Edit(edit => edit.ReplaceText(
            new RichTextRange(7, "2026-07-30".Length),
            string.Empty));

        Assert.Empty(document.CurrentSnapshot.Fields);
        Assert.Equal("before after", document.Text);
    }

    [Fact]
    public void ImagesWithEqualPayloadsAreEqual()
    {
        var first = RichTextImage.FromBytes(0, "image/png", [1, 2, 3], 8, 8);
        var second = RichTextImage.FromBytes(0, "image/png", [1, 2, 3], 8, 8);
        var different = RichTextImage.FromBytes(0, "image/png", [9, 9, 9], 8, 8);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void ListPicturesWithEqualPayloadsAreEqual()
    {
        var first = RichTextListPicture.FromBytes("p1", "image/png", [1, 2, 3], 8, 8);
        var second = RichTextListPicture.FromBytes("p1", "image/png", [1, 2, 3], 8, 8);
        var different = RichTextListPicture.FromBytes("p1", "image/png", [4], 8, 8);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, different);
    }
}
