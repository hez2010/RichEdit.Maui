using RichEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public sealed class ListEditingTests
{
    [Fact]
    public void DeletingFirstItemWithItsLeadingBreakPreservesFollowingItemsAndUndo()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "heading");
            edit.InsertParagraph(edit.Body, 1, "first");
            edit.InsertParagraph(edit.Body, 2, "second");
            edit.WrapInList(edit.Body, 1, 2, numbered: true, startNumber: 4);
        });
        var document = new RichDocument(snapshot);
        var list = snapshot.Nodes.Values.OfType<RichList>().Single();
        var first = snapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
        document.Edit(edit => edit.ReplaceSelection(new RichTextSelection(new(edit.Body, new(2), 7), new(edit.Body, first, 5)), ""));
        Assert.Equal("heading\nsecond", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Single(document.CurrentSnapshot.Get<RichList>(list.Id).Items);
        Assert.Equal(5, document.CurrentSnapshot.Get<RichList>(list.Id).StartNumber);
        document.Undo();
        Assert.True(snapshot.ContentEquals(document.CurrentSnapshot));
    }

    [Theory]
    [InlineData(0, 0, "second\nthird")]
    [InlineData(2, 3, "fiond\nthird")]
    public void TextDeletionJoinsListItemsAndRetainsHistory(int start, int end, string expected)
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "first");
            edit.InsertParagraph(edit.Body, 1, "second");
            edit.InsertParagraph(edit.Body, 2, "third");
            edit.WrapInList(edit.Body, 0, 3, numbered: true, startNumber: 4, numberStyle: RichListNumberingStyle.LowerLetter);
        });
        var document = new RichDocument(snapshot);
        var list = snapshot.Nodes.Values.OfType<RichList>().Single();
        var first = snapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
        var second = snapshot.Get<RichListItem>(list.Items[1]).Blocks[0];
        document.Edit(edit => edit.ReplaceSelection(new RichTextSelection(new(edit.Body, first, start), new(edit.Body, second, end)), ""));
        var updated = document.CurrentSnapshot.Get<RichList>(list.Id);
        Assert.Equal(expected, document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(2, updated.Items.Count);
        Assert.Equal(list.StartNumber, updated.StartNumber);
        Assert.Equal(list.NumberStyle, updated.NumberStyle);
        document.Undo();
        Assert.True(snapshot.ContentEquals(document.CurrentSnapshot));
    }

    [Fact]
    public void SplitAndJoinPreserveFollowingBlocksAnnotationsAndOneUndoEntry()
    {
        var document = new RichDocument(RichDocumentSnapshot.FromPlainText("abcd\nnested"));
        var paragraphs = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks;
        RichNodeId listId = default;
        document.Edit(edit =>
        {
            listId = edit.WrapInList(edit.Body, 0, 1, numbered: true, startNumber: 3);
            edit.MoveBlock(paragraphs[1], edit.Snapshot.Get<RichList>(listId).Items[0], 1);
            edit.AddBookmark("tail", new(new(edit.Body, paragraphs[0], 3), new(edit.Body, paragraphs[1], 2)));
        });
        using var session = document.CreateSession();
        var before = document.CurrentSnapshot;
        RichTextPoint point = default;
        session.Edit(edit => { point = edit.SplitListItem(new(edit.Body, paragraphs[0], 2)); edit.SetSelection(new RichTextSelection(point, point)); });
        var list = document.CurrentSnapshot.Get<RichList>(listId);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(new[] { point.Paragraph, paragraphs[1] }, document.CurrentSnapshot.Get<RichListItem>(list.Items[1]).Blocks);
        Assert.Equal("ab\ncd\nnested", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(point.Paragraph, Assert.Single(document.CurrentSnapshot.Bookmarks.Values).Range!.Anchor.Paragraph);
        session.Edit(edit => { point = edit.JoinListItem(point); edit.SetSelection(new RichTextSelection(point, point)); });
        Assert.Equal("abcd\nnested", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Single(document.CurrentSnapshot.Get<RichList>(listId).Items);
        Assert.Equal(2, point.Offset);
        session.Undo();
        Assert.Equal(2, document.CurrentSnapshot.Get<RichList>(listId).Items.Count);
        session.Undo();
        Assert.True(before.ContentEquals(document.CurrentSnapshot));
    }

    [Fact]
    public void EmptyMiddleItemLeavesTwoListsWithStableNumberingAndParagraphIdentity()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, edit.Snapshot.FirstSelection().Active.Paragraph, 0), 0, "first");
            edit.InsertParagraph(edit.Body, 1);
            edit.InsertParagraph(edit.Body, 2, "last");
            edit.WrapInList(edit.Body, 0, 3, numbered: true, startNumber: 7);
        });
        var document = new RichDocument(snapshot);
        var list = Assert.Single(snapshot.Nodes.Values.OfType<RichList>());
        var empty = snapshot.Get<RichListItem>(list.Items[1]).Blocks[0];
        document.Edit(edit => Assert.Equal(empty, edit.SplitListItem(new(edit.Body, empty, 0)).Paragraph));
        var body = document.CurrentSnapshot.Get<RichStory>(snapshot.Body);
        Assert.Equal(3, body.Blocks.Count);
        Assert.Equal(empty, body.Blocks[1]);
        Assert.Equal(7, document.CurrentSnapshot.Get<RichList>(body.Blocks[0]).StartNumber);
        Assert.Equal(9, document.CurrentSnapshot.Get<RichList>(body.Blocks[2]).StartNumber);
        document.Undo();
        Assert.True(snapshot.ContentEquals(document.CurrentSnapshot));
    }
}
