using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichStructuralEditTests
{
    [Fact]
    public void SplitJoinAndMovePreserveParagraphIdentityAndMapAddresses()
    {
        var initial = RichDocumentSnapshot.FromPlainText("abc😀def\ntail");
        var body = initial.Get<RichStory>(initial.Body);
        var first = body.Blocks[0];
        var tail = body.Blocks[1];
        var point = new RichTextPoint(initial.Body, first, 7);
        var edit = Edit(initial);
        var second = edit.SplitParagraph(new(initial.Body, first, 3));
        var split = edit.Finish();
        Assert.Equal("abc\n😀def\ntail", split.Snapshot.ProjectText().Text.ToString());
        Assert.Equal(point with { Paragraph = second, Offset = 4 }, split.Map.Map(point));
        Assert.Equal(first, split.Snapshot.Get<RichStory>(initial.Body).Blocks[0]);
        edit = Edit(split.Snapshot);
        edit.JoinParagraphs(first, second);
        edit.MoveBlock(tail, initial.Body, 0);
        var joined = edit.Finish();
        Assert.Equal("tail\nabc😀def", joined.Snapshot.ProjectText().Text.ToString());
        Assert.Equal(point, joined.Map.Map(split.Map.Map(point)!.Value));
        Assert.Equal("abc😀def\ntail", initial.ProjectText().Text.ToString());
    }

    [Fact]
    public void TextReplacementPreservesUnaffectedFormattingAndUsesLocalUtf16Maps()
    {
        var initial = RichDocumentSnapshot.FromPlainText("abcdef");
        var paragraph = initial.Get<RichStory>(initial.Body).Blocks[0];
        var edit = Edit(initial);
        edit.SetCharacterFormat(new(initial.Body, paragraph, 2), 2, new() { Bold = true });
        edit.ReplaceText(new(initial.Body, paragraph, 3), 1, "XYZ");
        edit.InsertSoftBreak(new(initial.Body, paragraph, 6));
        var result = edit.Finish();
        Assert.Equal("abcXYZ\u2028ef", result.Snapshot.ProjectText().Text.ToString());
        var runs = result.Snapshot.Get<RichParagraph>(paragraph).Inlines.Select(id => result.Snapshot.Get<RichInline>(id)).ToArray();
        Assert.True(runs.OfType<RichText>().Single(run => run.Content.ToString().Contains("XYZ", StringComparison.Ordinal)).Format.Bold.Value);
        Assert.Equal(9, result.Map.Map(new(initial.Body, paragraph, 6))!.Value.Offset);
        Assert.Equal("abcdef", initial.ProjectText().Text.ToString());
        Assert.Throws<InvalidOperationException>(() => edit.InsertParagraph(initial.Body, 0));
    }

    [Fact]
    public void DeleteBlockInvalidatesItsPointsAndLeavesAValidTextContainer()
    {
        var initial = RichDocumentSnapshot.FromPlainText("only paragraph");
        var paragraph = initial.Get<RichStory>(initial.Body).Blocks[0];
        var edit = Edit(initial);
        edit.DeleteBlock(paragraph);
        var result = edit.Finish();
        Assert.Null(result.Map.Map(new(initial.Body, paragraph, 3)));
        Assert.Equal("", result.Snapshot.ProjectText().Text.ToString());
        Assert.NotEqual(paragraph, Assert.Single(result.Snapshot.Get<RichStory>(initial.Body).Blocks));
    }

    [Fact]
    public void DetachedBuilderProducesValidatedSnapshotsAndExpiresItsTransaction()
    {
        RichDocumentEdit? captured = null;
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            captured = edit;
            edit.InsertParagraph(edit.Body, 1, "second");
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "first");
        });
        Assert.Equal("first\nsecond", snapshot.ProjectText().Text.ToString());
        Assert.False(snapshot.Revision.IsValid);
        Assert.Throws<InvalidOperationException>(() => captured!.SetStyles(RichStyleSheet.Empty));
        Assert.Throws<ArgumentException>(() => RichDocumentSnapshot.Build(edit => edit.InsertParagraph(edit.Body, 1, "dangling style", new(5))));
    }

    private static RichDocumentEdit Edit(RichDocumentSnapshot snapshot) => new(snapshot, snapshot.Nodes.Keys.Max(id => id.Value));

    [Fact]
    public void RepeatedTypingSharesTextStorageWithoutAccumulatingFormatRuns()
    {
        var initial = RichDocumentSnapshot.FromPlainText(new string('x', 100_000));
        var paragraph = initial.Get<RichStory>(initial.Body).Blocks[0];
        var edit = Edit(initial);
        for (var index = 0; index < 200; index++) edit.ReplaceText(new(initial.Body, paragraph, 50_000 + index), 0, "a");
        var result = edit.Finish().Snapshot;
        var run = Assert.Single(result.Get<RichParagraph>(paragraph).Inlines);
        Assert.Equal(100_200, result.Get<RichText>(run).Content.Length);
        Assert.Equal(100_000, initial.GetParagraphLength(paragraph));
    }
}
