using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichReviewProjectionTests
{
    [Fact]
    public void OriginalAndFinalAreFrozenProjectionsWithMappedAddressesAndNoHistory()
    {
        var document = RichDocument.FromPlainText("ab\ndeleted");
        var first = document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks[0];
        var deleted = document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks[1];
        document.Edit(edit =>
        {
            edit.ReplaceText(new(edit.Body, first, 1), 0, "XX");
            edit.DeleteBlock(deleted);
        }, new(Revision: new("Author")));
        var source = document.CurrentSnapshot;
        var original = source.CreateReviewProjection(RichReviewMode.Original, TestContext.Current.CancellationToken);
        var final = source.CreateReviewProjection(RichReviewMode.Final, TestContext.Current.CancellationToken);
        Assert.Same(source, original.Source);
        Assert.Equal("ab\ndeleted", original.Snapshot.ProjectText().Text.ToString());
        Assert.Equal("aXXb", final.Snapshot.ProjectText().Text.ToString());
        Assert.Empty(original.Snapshot.Revisions);
        Assert.Empty(final.Snapshot.Revisions);
        Assert.False(original.Snapshot.Revision.IsValid);
        var current = new RichTextPoint(source.Body, first, 3, CaretAffinity.Downstream);
        Assert.Equal(current with { Offset = 1 }, original.ToReview(current));
        Assert.Equal(current, original.ToSource(current with { Offset = 1 }));
        Assert.Null(original.ToSource(new(source.Body, deleted, 0)));
        Assert.Same(source, document.CurrentSnapshot);
        Assert.True(document.CanUndo);
        document.Undo();
        Assert.Equal("aXXb", final.Snapshot.ProjectText().Text.ToString());
        Assert.Equal("ab\ndeleted", document.CurrentSnapshot.ProjectText().Text.ToString());
    }
}
