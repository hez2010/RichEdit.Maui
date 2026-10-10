using Xunit;

namespace RichEdit.Core.Tests;

public class RichListEditTests
{
    [Fact]
    public void WrappingAndUnwrappingListsPreservesParagraphIdentitiesAndSharedSelections()
    {
        var document = RichDocument.FromPlainText("one\ntwo\nthree");
        var initial = document.CurrentSnapshot;
        var paragraphs = initial.Get<RichStory>(initial.Body).Blocks;
        using var session = document.CreateSession();
        var point = new RichTextPoint(initial.Body, paragraphs[1], 2);
        session.Selection = new RichTextSelection(point, point);
        RichNodeId list = default;
        document.Edit(edit => list = edit.WrapInList(edit.Body, 0, 2, numbered: true, startNumber: 3));
        Assert.Equal(new RichTextSelection(point, point), session.Selection);
        Assert.Equal("one\ntwo\nthree", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(3, document.CurrentSnapshot.Get<RichList>(list).StartNumber);
        document.Edit(edit => edit.UnwrapList(list));
        Assert.Equal(paragraphs, document.CurrentSnapshot.Get<RichStory>(initial.Body).Blocks);
        document.Undo();
        Assert.Contains(list, document.CurrentSnapshot.Nodes.Keys);
        document.Undo();
        Assert.Equal(paragraphs, document.CurrentSnapshot.Get<RichStory>(initial.Body).Blocks);
        Assert.False(document.IsModified);
    }

    [Fact]
    public void ATableCanBelongToAListItemWithAnExplicitMarkerParagraph()
    {
        RichNodeId table = default, list = default;
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 1, [new(1440)]);
            list = edit.WrapInList(edit.Body, 1, 1);
        });
        var item = snapshot.Get<RichListItem>(snapshot.Get<RichList>(list).Items[0]);
        Assert.IsType<RichParagraph>(snapshot.Get<RichNode>(item.Blocks[0]));
        Assert.Equal(table, item.Blocks[1]);
        Assert.Equal(item.Id, snapshot.Get<RichTable>(table).Parent);
    }
}
