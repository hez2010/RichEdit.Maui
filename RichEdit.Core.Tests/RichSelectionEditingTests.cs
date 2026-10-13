using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RichSelectionEditingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CrossListDeletionPreservesUnselectedTextFormattingAndTrailingItems(bool startsInList, bool endsInList)
    {
        RichNodeId first = default, last = default, tail = default, endingList = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "before");
            edit.InsertParagraph(edit.Body, 1, "lead");
            first = edit.InsertParagraph(edit.Body, 2, "abcd");
            edit.InsertParagraph(edit.Body, 3, "middle A");
            edit.InsertParagraph(edit.Body, 4, "middle B");
            last = edit.InsertParagraph(edit.Body, 5, "GHIJ");
            tail = edit.InsertParagraph(edit.Body, 6, "tail");
            edit.InsertParagraph(edit.Body, 7, "after");
            edit.SetCharacterFormat(new(edit.Body, first, 0), 4, new() { Bold = true });
            edit.SetCharacterFormat(new(edit.Body, last, 0), 4, new() { Italic = true });
            if (startsInList) edit.WrapInList(edit.Body, 1, 3, numbered: true);
            if (endsInList) endingList = edit.WrapInList(edit.Body, edit.Snapshot.Get<RichStory>(edit.Body).Blocks.IndexOf(last), 2, numbered: true, startNumber: 7);
            edit.InsertTable(edit.Body, edit.Snapshot.Get<RichStory>(edit.Body).Blocks.IndexOf(endsInList ? endingList : last), 1, [new(1000)]);
        }));
        var before = document.CurrentSnapshot;
        using var session = document.CreateSession();
        using var peer = document.CreateSession();
        var peerSelection = new RichTextSelection(new(before.Body, tail, 1), new(before.Body, tail, 1));
        peer.Selection = peerSelection;
        var selected = new RichTextSelection(new(before.Body, first, 2), new(before.Body, last, 2));
        session.Selection = selected;
        session.ReplaceSelection("");
        var after = document.CurrentSnapshot;
        Assert.Equal("before\nlead\nabIJ\ntail\nafter", after.ProjectText().Text.ToString());
        Assert.Empty(after.Nodes.Values.OfType<RichTable>());
        Assert.All(after.GetSelectedCharacterFormats(new RichTextSelection(new(after.Body, first, 0), new(after.Body, first, 2))), format => Assert.True(format.Bold));
        Assert.All(after.GetSelectedCharacterFormats(new RichTextSelection(new(after.Body, first, 2), new(after.Body, first, 4))), format => Assert.True(format.Italic));
        Assert.Equal(peerSelection, peer.Selection);
        if (endsInList) Assert.Equal(8, after.Get<RichList>(endingList).StartNumber);
        session.Undo();
        Assert.True(before.ContentEquals(document.CurrentSnapshot));
        Assert.Equal(selected, session.Selection);
        session.Redo();
        Assert.True(after.ContentEquals(document.CurrentSnapshot));
        Assert.Equal(peerSelection, peer.Selection);
    }

    [Fact]
    public void SelectingAllBlocksRemovesTablesWhileSelectingCellsRetainsTheirGrid()
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, [new(1000), new(1000)]);
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "before");
            edit.InsertParagraph(edit.Body, 2, "after");
        }));
        using var session = document.CreateSession();
        session.Selection = new RichCellSelection(table, 0, 0, 2, 2);
        session.ReplaceSelection("");
        Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Rows);
        session.SelectAll();
        var selection = Assert.IsType<RichBlockSelection>(session.Selection);
        var copied = RichDocumentFragment.FromSelection(document.CurrentSnapshot, selection);
        Assert.Equal(3, copied.Content.Get<RichStory>(copied.Content.Body).Blocks.Count);
        Assert.Single(copied.Content.Nodes.Values.OfType<RichTable>());
        session.ReplaceSelection("replacement");
        Assert.Equal("replacement", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(document.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
        session.Undo();
        Assert.Equal(selection, session.Selection);
        Assert.Single(document.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
    }

    [Fact]
    public void IdenticalTextReplacementOnlyCollapsesTheViewSelection()
    {
        var document = RichDocument.FromPlainText("abc");
        using var view = document.CreateSession();
        view.Selection = new RichTextSelection(new(new(1), new(2), 1), new(new(1), new(2), 2));
        var before = document.CurrentSnapshot;
        view.ReplaceSelection("b");
        Assert.Same(before, document.CurrentSnapshot);
        Assert.False(document.IsModified);
        Assert.False(document.CanUndo);
        Assert.Equal(new RichTextSelection(new(new(1), new(2), 2), new(new(1), new(2), 2)), view.Selection);
    }

    [Fact]
    public void ReplacingAWholeStyledRunRetainsItsFormattingAndDoesNotInheritAnAdjacentLink()
    {
        var document = RichDocument.FromPlainText("ab");
        document.Edit(edit =>
        {
            edit.SetHyperlink(new(edit.Body, new(2), 0), 1, new("#target"));
            edit.SetCharacterFormat(new(edit.Body, new(2), 1), 1, new() { Bold = true });
        });
        using var view = document.CreateSession();
        view.Selection = new RichTextSelection(new(new(1), new(2), 1), new(new(1), new(2), 2));
        view.ReplaceSelection("X\nY");
        var runs = document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Where(run => run.Content.ToString() is "X" or "Y").ToArray();
        Assert.Equal(2, runs.Length);
        Assert.All(runs, run => { Assert.Equal(new RichCharacterStyle { Bold = true }, run.Format); Assert.Null(run.Link); });
    }

    [Fact]
    public void TypingOverridesAreViewOwnedAndOnlyAuthorExplicitPropertiesOnInsertion()
    {
        var document = RichDocument.FromPlainText("abc");
        using var view = document.CreateSession();
        using var peer = document.CreateSession();
        view.TypingCharacterFormat = new() { Bold = true };
        Assert.False(document.CanUndo);
        Assert.False(document.IsModified);
        Assert.True(view.ResolveTypingCharacterFormat().Bold);
        Assert.False(peer.ResolveTypingCharacterFormat().Bold);
        view.ReplaceSelection("X\nY");
        var inserted = document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Where(run => run.Content.ToString() is "X" or "Y").ToArray();
        Assert.Equal(2, inserted.Length);
        Assert.All(inserted, run => Assert.Equal(new RichCharacterStyle { Bold = true }, run.Format));
        view.Selection = document.CurrentSnapshot.FirstSelection();
        Assert.Equal(new RichCharacterStyle(), view.TypingCharacterFormat);
        Assert.Equal("Inherit", StyleValue<bool>.Inherit.ToString());
    }

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
