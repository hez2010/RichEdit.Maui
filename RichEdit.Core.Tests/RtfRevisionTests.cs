using System.Text;
using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public class RtfRevisionTests
{
    [Fact]
    public async Task BlockOrderOnlyRevisionsRestoreWithoutCopyingUnchangedText()
    {
        var document = RichDocument.FromPlainText("first\nsecond\nthird");
        var group = new RichRevisionInfo("Author");
        document.Edit(edit => edit.MoveBlock(new(2), edit.Body, 3), new(Revision: group));
        var rtf = await Save(document.CurrentSnapshot);
        Assert.Equal(1, rtf.Split("second", StringSplitOptions.None).Length - 1);
        var loaded = new RichDocument(await Load(rtf));
        Assert.Equal("second\nthird\nfirst", loaded.CurrentSnapshot.ProjectText().Text.ToString());
        loaded.Edit(edit => edit.RejectRevision(group.Group));
        Assert.Equal("first\nsecond\nthird", loaded.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task NestedRevisionContextsRetainGroupsWithoutRepeatingUnchangedBlocks()
    {
        var document = RichDocument.FromPlainText("old\nUNRELATED_CONTENT");
        var first = new RichRevisionInfo("編集者 α", created: new DateTimeOffset(2026, 10, 11, 9, 5, 31, TimeSpan.FromHours(9)));
        var second = new RichRevisionInfo("Second");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 3, "new"), new(Revision: first));
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 3), 0, "!"), new(Revision: second));
        var rtf = await Save(document.CurrentSnapshot);
        Assert.Equal(1, rtf.Split("UNRELATED_CONTENT", StringSplitOptions.None).Length - 1);
        var loaded = new RichDocument(await Load(rtf));
        Assert.Equal("new!\nUNRELATED_CONTENT", loaded.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(2, loaded.CurrentSnapshot.GetRevisionResolution(first.Group, accept: false).Length);
        Assert.Contains(loaded.CurrentSnapshot.Revisions.Values, revision => revision.Info == first);
        loaded.Edit(edit => edit.AcceptRevision(first.Group));
        var again = new RichDocument(await Load(await Save(loaded.CurrentSnapshot)));
        Assert.Single(again.CurrentSnapshot.Revisions);
        again.Edit(edit => edit.RejectRevision(second.Group));
        Assert.Equal("new\nUNRELATED_CONTENT", again.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task StructureFormattingStylesResourcesAndAnnotationsCanBeReviewedAfterRtfReload()
    {
        var document = RichDocument.FromPlainText("head\ntail");
        var first = new RichRevisionInfo("Author");
        var second = new RichRevisionInfo("Reviewer");
        RichNodeId table = default;
        RichAnnotationId comment = default;
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { Foreground = new RichColor(20, 40, 60) });
            edit.SetStyles(new([new(new(1), "Heading", RichStyleKind.Paragraph) { Character = new() { Bold = true } }]));
            edit.SetParagraphStyle(new(2), new(1));
            edit.AddImageAsset(RichImageKind.Png, new byte[] { 1, 2, 3 });
            var asset = edit.AddImageAsset(RichImageKind.Png, new byte[] { 4, 5, 6 });
            edit.InsertImage(new(edit.Body, new(2), 1), asset, new(100), new(200));
            var range = new RichTextSelection(new(edit.Body, new(2), 0), new(edit.Body, new(2), 3));
            edit.AddBookmark("reference", range);
            comment = edit.AddComment(range, "before", "Author");
            table = edit.InsertTable(edit.Body, 1, 2, [new(1200), new(1600)]);
        }, new(Revision: first));
        document.Edit(edit =>
        {
            edit.MergeCells(new(table, 0, 0, 1, 2));
            edit.SetCellFormat(new(table, 0, 0, 1, 2), new() { Shading = new(10000, new(30, 100, 200)), Padding = RichCellPadding.Uniform(new(80)) });
            var cell = edit.Snapshot.GetTableGrid(table)[1, 0];
            var paragraph = edit.Snapshot.Get<RichTableCell>(cell).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "item");
            edit.WrapInList(cell, 0, 1, true, 3, RichListNumberingStyle.UpperRoman);
            edit.SetCommentText(comment, "after");
        }, new(Revision: second));
        var loaded = new RichDocument(await Load(await Save(document.CurrentSnapshot)));
        Assert.Equal("after", Assert.Single(loaded.CurrentSnapshot.Comments.Values).Text);
        Assert.Equal(2, loaded.CurrentSnapshot.Assets.Count);
        Assert.Single(loaded.CurrentSnapshot.Nodes.Values.OfType<RichList>());
        var loadedTable = Assert.Single(loaded.CurrentSnapshot.Nodes.Values.OfType<RichTable>());
        var grid = loaded.CurrentSnapshot.GetTableGrid(loadedTable.Id);
        Assert.Equal(grid[0, 0], grid[0, 1]);
        loaded.ClearHistory();
        loaded.Edit(edit => edit.RejectRevision(second.Group));
        Assert.Empty(loaded.CurrentSnapshot.Nodes.Values.OfType<RichList>());
        Assert.Equal("before", Assert.Single(loaded.CurrentSnapshot.Comments.Values).Text);
        grid = loaded.CurrentSnapshot.GetTableGrid(loadedTable.Id);
        Assert.NotEqual(grid[0, 0], grid[0, 1]);
        loaded.Edit(edit => edit.RejectRevision(first.Group));
        Assert.Equal("head\ntail", loaded.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(loaded.CurrentSnapshot.Styles.Definitions);
        Assert.Empty(loaded.CurrentSnapshot.Assets);
        Assert.Empty(loaded.CurrentSnapshot.Bookmarks);
        Assert.Empty(loaded.CurrentSnapshot.Comments);
        Assert.Equal(new RichCharacterStyle(), loaded.CurrentSnapshot.DefaultCharacter);
    }

    [Fact]
    public async Task ResourceOnlyRevisionsUseEmptyContextScaffoldingAndFreshNodeIdentities()
    {
        var document = RichDocument.FromPlainText("text");
        var group = new RichRevisionInfo("Author");
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { Italic = true });
            edit.AddImageAsset(RichImageKind.Png, new byte[] { 10, 20, 30 });
        }, new(Revision: group));
        var rtf = (await Save(document.CurrentSnapshot)).Replace("1/1", "9223372036854775806/1", StringComparison.Ordinal);
        var loaded = new RichDocument(await Load(rtf));
        loaded.Edit(edit => edit.AcceptRevision(group.Group));
        loaded.Edit(edit => edit.InsertParagraph(edit.Body, 1, "more"));
        Assert.Equal("text\nmore", loaded.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.True(loaded.CurrentSnapshot.Nodes.Keys.Max(id => id.Value) < 100);
    }

    [Fact]
    public async Task RetainedDocumentsShareImportBudgetsAndRejectTamperedState()
    {
        var document = RichDocument.FromPlainText("original");
        for (var index = 0; index < 6; index++)
            document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 0, "x"), new(Revision: new("Author")));
        var rtf = await Save(document.CurrentSnapshot);
        using var limited = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        await Assert.ThrowsAsync<FormatException>(() => RtfCodec.ReadAsync(limited, new() { MaximumNodes = 80 }, TestContext.Current.CancellationToken));
        using var workLimited = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        await Assert.ThrowsAsync<FormatException>(() => RtfCodec.ReadAsync(workLimited, new() { MaximumRevisionWork = 5 }, TestContext.Current.CancellationToken));
        // Change only the live body's occurrence, retaining the recorded after-state.
        var first = rtf.IndexOf("xxxxxxoriginal", StringComparison.Ordinal);
        Assert.True(first >= 0);
        var tampered = rtf[..first] + "xxxxxxCORRUPT!" + rtf[(first + "xxxxxxoriginal".Length)..];
        await Assert.ThrowsAsync<FormatException>(() => Load(tampered));
    }

    private static async Task<string> Save(RichDocumentSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        await RtfCodec.WriteAsync(snapshot, stream, TestContext.Current.CancellationToken);
        return Encoding.ASCII.GetString(stream.ToArray());
    }
    private static async Task<RichDocumentSnapshot> Load(string rtf)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        return (await RtfCodec.ReadAsync(stream, new() { RequireLossless = true }, TestContext.Current.CancellationToken)).Snapshot;
    }
}
