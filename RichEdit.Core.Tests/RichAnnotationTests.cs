using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichAnnotationTests
{
    [Fact]
    public void CommentsAndBookmarksBecomeOrphanedWithoutLosingContentAndUndoRestoresTheirAnchors()
    {
        var document = RichDocument.FromPlainText("alpha beta");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        var range = new RichTextSelection(new(document.CurrentSnapshot.Body, paragraph, 6), new(document.CurrentSnapshot.Body, paragraph, 10, CaretAffinity.Upstream));
        RichAnnotationId bookmark = default, comment = default;
        document.Edit(edit =>
        {
            bookmark = edit.AddBookmark("Target", range);
            comment = edit.AddComment(range, "Keep this explanation", "A", new(2026, 10, 10, 11, 22, 59, TimeSpan.FromHours(9)));
        });
        var saved = document.CaptureSave();
        document.MarkSaved(saved);
        document.Edit(edit => edit.ReplaceText(range.Anchor, 4, "replacement"));
        Assert.True(document.CurrentSnapshot.Comments[comment].IsOrphaned);
        Assert.True(document.CurrentSnapshot.Bookmarks[bookmark].IsOrphaned);
        Assert.Equal("Keep this explanation", document.CurrentSnapshot.Comments[comment].Text);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 2, 22, 0, TimeSpan.Zero), document.CurrentSnapshot.Comments[comment].Created);
        document.Undo();
        Assert.Equal(range, document.CurrentSnapshot.Comments[comment].Range);
        Assert.Equal(range, document.CurrentSnapshot.Bookmarks[bookmark].Range);
        Assert.False(document.IsModified);
        document.Redo();
        Assert.True(document.CurrentSnapshot.Comments[comment].IsOrphaned);
        Assert.Equal(range, saved.Snapshot.Comments[comment].Range);
    }

    [Fact]
    public void StructuralSplitsAndSubsequentEditsMapPersistentAnchorsInOperationOrder()
    {
        var document = RichDocument.FromPlainText("abcdef");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        RichAnnotationId before = default, after = default;
        RichNodeId split = default;
        document.Edit(edit =>
        {
            before = edit.AddComment(new(new(edit.Body, paragraph, 1), new(edit.Body, paragraph, 5)), "before split");
            split = edit.SplitParagraph(new(edit.Body, paragraph, 3));
            after = edit.AddBookmark("AfterSplit", new(new(edit.Body, split, 0), new(edit.Body, split, 3)));
            edit.ReplaceText(new(edit.Body, split, 0), 0, "X");
        });
        var comment = document.CurrentSnapshot.Comments[before].Range!;
        Assert.Equal(paragraph, comment.Anchor.Paragraph);
        Assert.Equal(new RichTextPoint(document.CurrentSnapshot.Body, split, 3), comment.Active);
        var bookmark = document.CurrentSnapshot.Bookmarks[after].Range!;
        Assert.Equal(1, bookmark.Anchor.Offset);
        Assert.Equal(4, bookmark.Active.Offset);
        document.Edit(edit => edit.JoinParagraphs(paragraph, split));
        Assert.Equal(paragraph, document.CurrentSnapshot.Comments[before].Range!.Active.Paragraph);
        Assert.Equal(6, document.CurrentSnapshot.Comments[before].Range!.Active.Offset);
    }

    [Fact]
    public void AnnotationOnlyEditsParticipateInHistoryAndNamesAreUnique()
    {
        var document = RichDocument.FromPlainText("text");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        var range = new RichTextSelection(new(document.CurrentSnapshot.Body, paragraph, 0), new(document.CurrentSnapshot.Body, paragraph, 4));
        RichAnnotationId comment = default;
        document.Edit(edit => { edit.AddBookmark("Name", range); comment = edit.AddComment(range, "old"); });
        var before = document.CurrentSnapshot;
        Assert.Throws<ArgumentException>(() => document.Edit(edit => edit.AddBookmark("name", range)));
        Assert.Same(before, document.CurrentSnapshot);
        document.Edit(edit => edit.SetCommentText(comment, "new"));
        Assert.Equal("text", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal("new", document.CurrentSnapshot.Comments[comment].Text);
        document.Undo();
        Assert.Equal("old", document.CurrentSnapshot.Comments[comment].Text);
        document.Edit(edit => edit.DeleteBlock(paragraph));
        Assert.True(document.CurrentSnapshot.Comments[comment].IsOrphaned);
        var next = document.CurrentSnapshot.FirstSelection();
        document.Edit(edit => edit.SetAnnotationRange(comment, next));
        Assert.False(document.CurrentSnapshot.Comments[comment].IsOrphaned);
        document.Undo();
        Assert.True(document.CurrentSnapshot.Comments[comment].IsOrphaned);
    }
}
