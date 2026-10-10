using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RichFragmentTests
{
    [Fact]
    public void InlinePasteKeepsCopiedAppearanceAndDestinationSuffixWithoutAddingParagraphs()
    {
        var source = RichDocumentSnapshot.Build(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { Bold = true, Italic = false });
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "paste");
            edit.AddBookmark("Inside", new(new(edit.Body, new(2), 1), new(edit.Body, new(2), 4)));
        });
        var document = RichDocument.FromPlainText("abcd");
        document.Edit(edit => edit.SetDefaultCharacterFormat(new() { Bold = false, Italic = true }));
        using var session = document.CreateSession();
        session.Selection = new RichTextSelection(new(new(1), new(2), 1), new(new(1), new(2), 3));
        session.ReplaceSelection(new RichDocumentFragment(source));
        var snapshot = document.CurrentSnapshot;
        Assert.Equal("apasted", snapshot.ProjectText().Text.ToString());
        Assert.Single(snapshot.Get<RichStory>(snapshot.Body).Blocks);
        var pasted = Assert.Single(snapshot.Nodes.Values.OfType<RichText>(), run => run.Content.ToString() == "paste");
        Assert.True(snapshot.ResolveCharacter(pasted.Id).Bold);
        Assert.False(snapshot.ResolveCharacter(pasted.Id).Italic);
        Assert.Equal(new RichCharacterStyle(), pasted.Format);
        Assert.All(snapshot.Nodes.Values.OfType<RichText>().Where(run => run != pasted), run => Assert.True(snapshot.ResolveCharacter(run.Id).Italic));
        Assert.Equal(6, Assert.IsType<RichTextSelection>(session.Selection).Active.Offset);
        var bookmark = Assert.Single(snapshot.Bookmarks.Values);
        Assert.Equal(2, bookmark.Range!.Anchor.Offset);
        Assert.Equal(5, bookmark.Range.Active.Offset);
        session.Undo();
        Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(document.CurrentSnapshot.Bookmarks);
    }

    [Fact]
    public void MultilinePastePreservesParagraphStylesAndUnchangedSuffixAppearance()
    {
        var source = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "one");
            var last = edit.InsertParagraph(edit.Body, 1, "two");
            edit.SetParagraphFormat(last, new() { Alignment = RichAlignment.Center });
            edit.SetParagraphCharacterFormat(last, new() { Bold = true });
        });
        var document = RichDocument.FromPlainText("ab");
        using var session = document.CreateSession();
        session.Selection = new RichTextSelection(new(new(1), new(2), 1), new(new(1), new(2), 1));
        session.ReplaceSelection(new RichDocumentFragment(source));
        Assert.Equal("aone\ntwob", document.CurrentSnapshot.ProjectText().Text.ToString());
        var lastParagraph = document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks[^1];
        Assert.Equal(RichAlignment.Center, document.CurrentSnapshot.ResolveParagraph(lastParagraph).Alignment);
        Assert.Equal(3, Assert.IsType<RichTextSelection>(session.Selection).Active.Offset);
        var suffix = Assert.Single(document.CurrentSnapshot.Nodes.Values.OfType<RichText>(), run => run.Content.ToString() == "b");
        Assert.False(document.CurrentSnapshot.ResolveCharacter(suffix.Id).Bold);
    }

    [Fact]
    public void PastingATableIntoAnEmptyDocumentRetainsOnlyTheTableAndABoundaryCaret()
    {
        var table = RichDocumentSnapshot.Build(edit =>
        {
            edit.InsertTable(edit.Body, 1, 2, [new(1000), new(1000)]);
            edit.DeleteBlock(new(2));
        });
        var document = new RichDocument();
        using var session = document.CreateSession();
        session.ReplaceSelection(new RichDocumentFragment(table));
        var block = Assert.Single(document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks);
        Assert.IsType<RichTable>(document.CurrentSnapshot.Get<RichBlock>(block));
        Assert.Equal(new RichBoundarySelection(new(1), block, RichBlockEdge.After), session.Selection);
        session.Undo();
        Assert.Equal("", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public void RichPasteAtSeveralCaretsIsOneUndoUnitWithStableSelectionIdentities()
    {
        var document = RichDocument.FromPlainText("abcd");
        using var session = document.CreateSession();
        RichTextSelectionItem Caret(int offset) => new(new(new(new(1), new(2), offset), new(new(1), new(2), offset)));
        var selected = new RichTextSelectionSet(document.CurrentSnapshot, [Caret(1), Caret(3)], 1);
        session.Selection = selected;
        session.ReplaceSelection(RichDocumentFragment.FromPlainText("X\nY"));
        Assert.Equal("aX\nYbcX\nYd", document.CurrentSnapshot.ProjectText().Text.ToString());
        var after = Assert.IsType<RichTextSelectionSet>(session.Selection);
        Assert.Equal(selected.Primary.Id, after.Primary.Id);
        session.Undo();
        Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(selected, session.Selection);
        Assert.False(document.CanUndo);
    }

    [Theory]
    [InlineData(" REF Target \\h ", " REF \"Target_2\" \\h ")]
    [InlineData("PAGEREF \"Target\"", "PAGEREF \"Target_2\"")]
    [InlineData("DATE \\@ \"Target\"", "DATE \\@ \"Target\"")]
    [InlineData("HYPERLINK \\l \"Target\"", "HYPERLINK \"#Target_2\"")]
    public void BookmarkReferencesRemapOnlyRecognizedFieldArguments(string instruction, string expected)
    {
        Assert.Equal(expected, Rtf.RtfFieldSyntax.RemapBookmarkReferences(instruction, new Dictionary<string, string> { ["Target"] = "Target_2" }));
    }

    [Fact]
    public void TextFragmentsSliceAndRemapAnnotationsStylesImagesAndLinks()
    {
        var source = RichDocumentSnapshot.Build(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { Bold = true });
            edit.SetStyles(new([new(new(1), "Body", RichStyleKind.Paragraph) { Character = new() { Foreground = new RichColor(1, 2, 3) } }]));
            edit.SetParagraphStyle(new(2), new(1));
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "abcdef");
            edit.AddBookmark("Target", new(new(edit.Body, new(2), 2), new(edit.Body, new(2), 4)));
            edit.AddComment(new(new(edit.Body, new(2), 3), new(edit.Body, new(2), 4)), "selected");
            edit.AddComment(new(new(edit.Body, new(2), 0), new(edit.Body, new(2), 1)), "outside");
            edit.SetHyperlink(new(edit.Body, new(2), 2), 2, new("#Target"));
            var asset = edit.AddImageAsset(RichImageKind.Png, new byte[] { 1, 2, 3 });
            edit.InsertImage(new(edit.Body, new(2), 4), asset, new(100), new(200));
        });
        var fragment = RichDocumentFragment.FromSelection(source, new RichTextSelection(new(source.Body, new(2), 1), new(source.Body, new(2), 6)));
        Assert.Equal("bcd\uFFFCe", fragment.Content.ProjectText().Text.ToString());
        Assert.Single(fragment.Content.Comments);
        Assert.Single(fragment.Content.Assets);
        var destination = RichDocument.FromPlainText("prefix");
        destination.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { Bold = false, Italic = true });
            edit.AddBookmark("Target", new(new(edit.Body, new(2), 0), new(edit.Body, new(2), 1)));
            edit.InsertFragment(edit.Body, 1, fragment);
        });
        var after = destination.CurrentSnapshot;
        Assert.Equal("prefix\nbcd\uFFFCe", after.ProjectText().Text.ToString());
        var bookmark = Assert.Single(after.Bookmarks.Values, value => value.Name != "Target");
        Assert.Equal("Target_2", bookmark.Name);
        Assert.Equal(1, bookmark.Range!.Anchor.Offset);
        Assert.Equal(4, bookmark.Range.Active.Offset);
        var linked = after.Nodes.Values.OfType<RichInline>().First(inline => inline.Link is not null);
        Assert.Equal("#Target_2", linked.Link!.Target);
        var format = after.ResolveCharacter(linked.Id);
        Assert.True(format.Bold);
        Assert.False(format.Italic);
        Assert.Equal(new RichColor(1, 2, 3), format.Foreground);
        destination.Undo();
        Assert.Equal("prefix", destination.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(destination.CurrentSnapshot.Assets);
    }

    [Fact]
    public void RectangularFragmentsPreserveMergedGridAndNestedBlocks()
    {
        RichNodeId table = default;
        var source = RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 3, [new(100), new(200), new(300)]);
            edit.MergeCells(new(table, 0, 1, 2, 2));
            var cell = edit.Snapshot.GetTableGrid(table)[0, 1];
            edit.InsertTable(cell, 1, 1, [new(50)]);
        });
        var fragment = RichDocumentFragment.FromSelection(source, new RichCellSelection(table, 1, 2, 1, 1));
        var top = fragment.Content.Get<RichTable>(table);
        Assert.Equal(new[] { 200, 300 }, top.Columns.Select(width => width.Twips));
        Assert.Equal(2, top.Rows.Count);
        Assert.Equal(2, fragment.Content.Nodes.Values.OfType<RichTable>().Count());
        var grid = fragment.Content.GetTableGrid(table);
        Assert.Equal(grid[0, 0], grid[1, 1]);
        var destination = new RichDocument();
        destination.Edit(edit => edit.InsertFragment(edit.Body, 1, fragment));
        Assert.Equal(2, destination.CurrentSnapshot.Nodes.Values.OfType<RichTable>().Count());
    }
}
