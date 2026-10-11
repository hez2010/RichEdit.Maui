using System.Collections.Immutable;
using System.Text;
using RichEdit.Core.Rtf;
using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RtfIdentityTests
{
    [Fact]
    public async Task RetainedSliceIdentitiesCoverStructureResourcesAndAnnotations()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "text");
            var table = edit.InsertTable(edit.Body, 1, 2, [new(1000), new(1500)]);
            edit.MergeCells(new(table, 0, 0, 1, 2));
            var paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[1, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "item");
            edit.WrapInList(edit.Snapshot.Get<RichParagraph>(paragraph).Parent!.Value, 0, 1, true);
            var point = new RichTextPoint(edit.Body, new(2), 1);
            edit.AddBookmark("named", new(point, point with { Offset = 3 }));
            edit.AddComment(new(point, point), "comment", "Author");
            var temporary = edit.InsertParagraph(edit.Body, 2, "temporary");
            var anchor = new RichTextPoint(edit.Body, temporary, 1);
            edit.AddComment(new(anchor, anchor), "orphan");
            edit.AddBookmark("orphan_anchor", new(anchor, anchor));
            edit.DeleteBlock(temporary);
            edit.AddImageAsset(RichImageKind.Png, new byte[] { 1, 2, 3 });
            var asset = edit.AddImageAsset(RichImageKind.Png, new byte[] { 4, 5, 6 });
            edit.InsertImage(point, asset, new(100), new(200));
        });
        using var stream = new MemoryStream();
        await new RtfProjectionWriter(snapshot, stream, semantic: true, includeIdentities: true).WriteAsync(TestContext.Current.CancellationToken);
        stream.Position = 0;
        var reader = new RtfDocumentReader(stream, new() { RequireLossless = true });
        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RtfIdentityMap.Preorder(snapshot).Select(node => node.Id.Value), RtfIdentityMap.Preorder(result.Snapshot).Select(node => reader.Identities.Nodes[node.Id]));
        Assert.Equal(snapshot.Assets.Keys.Select(id => id.Value).Order(), reader.Identities.Assets.Values.Order());
        Assert.Equal(snapshot.Bookmarks.Keys.Select(id => id.Value).Order(), reader.Identities.Bookmarks.Values.Order());
        Assert.Equal(snapshot.Comments.Keys.Select(id => id.Value).Order(), reader.Identities.Comments.Values.Order());
    }

    [Fact]
    public async Task EqualAdjacentRunsKeepDistinctIdentityBoundaries()
    {
        var snapshot = new RichDocumentSnapshot(new(1), new RichNode[]
        {
            new RichStory(new(1), [new(2)]), new RichParagraph(new(2), new(1), [new(3), new(4)]),
            new RichText(new(3), new(2), TextSnapshot.FromText("one")), new RichText(new(4), new(2), TextSnapshot.FromText("two")),
        }.ToImmutableDictionary(node => node.Id), RichStyleSheet.Empty);
        using var stream = new MemoryStream();
        await new RtfProjectionWriter(snapshot, stream, semantic: true, includeIdentities: true).WriteAsync(TestContext.Current.CancellationToken);
        stream.Position = 0;
        var reader = new RtfDocumentReader(stream, new() { RequireLossless = true });
        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Snapshot.Nodes.Values.OfType<RichText>().Count());
        Assert.Equal(new long[] { 1, 2, 3, 4 }, RtfIdentityMap.Preorder(result.Snapshot).Select(node => reader.Identities.Nodes[node.Id]));
    }

    [Theory]
    [InlineData("1/1 2/2 2/8")]
    [InlineData("1/1 0/2 3/8")]
    [InlineData("1/1 2/2 3/11")]
    [InlineData("1/1 2/2")]
    public async Task InvalidIdentityRelationshipsAreRejected(string identities)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1{\*\remodel1}{\*\reids " + identities + @"}text\par}"));
        await Assert.ThrowsAsync<FormatException>(() => RtfCodec.ReadAsync(input, cancellationToken: TestContext.Current.CancellationToken));
    }
}
