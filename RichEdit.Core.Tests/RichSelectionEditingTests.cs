using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RichSelectionEditingTests
{
    [Fact]
    public void MultipleCaretsInsertSemanticParagraphsInOnePublicationAndUndo()
    {
        var document = RichDocument.FromPlainText("abcd");
        using var view = document.CreateSession();
        using var peer = document.CreateSession();
        var set = new RichTextSelectionSet(document.CurrentSnapshot, [Caret(1), Caret(3)], 1);
        view.Selection = set;
        var publications = 0;
        document.Changed += (_, _) => publications++;
        Assert.True(view.ReplaceSelection("X\nY").Accepted);
        Assert.Equal("aX\nYbcX\nYd", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(1, publications);
        var after = Assert.IsType<RichTextSelectionSet>(view.Selection);
        Assert.Equal(set.Primary.Id, after.Primary.Id);
        Assert.All(after, selection => Assert.Equal(1, selection.Selection.Active.Offset));
        peer.Undo();
        Assert.Equal(set, peer.Selection);
        Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
        peer.Redo();
        Assert.Equal(after, peer.Selection);
        Assert.Equal("aX\nYbcX\nYd", document.CurrentSnapshot.ProjectText().Text.ToString());
        RichTextSelectionItem Caret(int offset) => new(new(new(new(1), new(2), offset), new(new(1), new(2), offset)));
    }

    [Fact]
    public void CrossParagraphReplacementJoinsButCrossCellReplacementIsAtomicRejection()
    {
        var document = RichDocument.FromPlainText("abc\ndef\nghi");
        using var view = document.CreateSession();
        var paragraphs = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks;
        view.Selection = new RichTextSelection(new(new(1), paragraphs[0], 1), new(new(1), paragraphs[2], 2));
        view.ReplaceSelection("Q\u2028R");
        Assert.Equal("aQ\u2028Ri", document.CurrentSnapshot.ProjectText().Text.ToString());
        view.Undo();
        Assert.Equal("abc\ndef\nghi", document.CurrentSnapshot.ProjectText().Text.ToString());
        RichNodeId table = default;
        document.Edit(edit => table = edit.InsertTable(edit.Body, 1, 1, [new(100), new(100)]));
        var snapshot = document.CurrentSnapshot;
        var grid = snapshot.GetTableGrid(table);
        var first = snapshot.Get<RichTableCell>(grid[0, 0]).Blocks[0];
        var last = snapshot.Get<RichTableCell>(grid[0, 1]).Blocks[0];
        view.Selection = new RichTextSelection(new(snapshot.Body, first, 0), new(snapshot.Body, last, 0));
        Assert.Throws<ArgumentException>(() => view.ReplaceSelection("unsafe"));
        Assert.Same(snapshot, document.CurrentSnapshot);
        view.Selection = new RichCellSelection(table, 0, 0, 1, 2);
        Assert.True(view.ReplaceSelection("safe").Accepted);
        Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Columns);
        Assert.Contains("safe", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public void OverlapsMergeDeterministicallyWithPrimaryDirectionAndIdentity()
    {
        var snapshot = RichDocumentSnapshot.FromPlainText("abcdef");
        var primary = new RichTextSelectionItem(new(new(new(1), new(2), 5), new(new(1), new(2), 2)));
        var selections = new RichTextSelectionSet(snapshot, [new(new(new(new(1), new(2), 0), new(new(1), new(2), 3))), primary], 1);
        Assert.Single(selections);
        Assert.Equal(primary.Id, selections.Primary.Id);
        Assert.Equal(5, selections.Primary.Selection.Anchor.Offset);
        Assert.Equal(0, selections.Primary.Selection.Active.Offset);
        Assert.Equal(selections, new(snapshot, selections));
    }
}
