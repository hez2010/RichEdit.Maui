using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RichFragmentTests
{
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
