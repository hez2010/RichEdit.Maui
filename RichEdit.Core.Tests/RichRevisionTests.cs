using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichRevisionTests
{
    [Fact]
    public void DeletedContentSurvivesClearedHistoryAndRejectIsOneUndoUnit()
    {
        var document = RichDocument.FromPlainText("old text\nunrelated");
        var original = document.CurrentSnapshot;
        var group = new RichRevisionInfo("Editor");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 3, "new"), new(Revision: group));
        var revision = Assert.Single(document.CurrentSnapshot.Revisions.Values);
        Assert.Equal("old text", Assert.Single(revision.Nodes.Values.Select(value => value.Before).OfType<RichText>()).Content.ToString());
        Assert.DoesNotContain(original.Get<RichStory>(original.Body).Blocks[1], revision.Nodes.Keys);
        document.ClearHistory();
        Assert.False(document.CanUndo);
        Assert.Single(document.CurrentSnapshot.Revisions);
        Assert.Equal(original.ProjectText().Text.ToString(), document.CurrentSnapshot.CreateOriginalReviewSnapshot().ProjectText().Text.ToString());
        document.Edit(edit => edit.RejectRevision(group.Group));
        Assert.Equal(original.ProjectText().Text.ToString(), document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(document.CurrentSnapshot.Revisions);
        document.Undo();
        Assert.Equal("new text\nunrelated", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Single(document.CurrentSnapshot.Revisions);
        document.Redo();
        Assert.Empty(document.CurrentSnapshot.Revisions);
    }

    [Fact]
    public void IndependentParagraphEditsCanBeResolvedSeparately()
    {
        var document = RichDocument.FromPlainText("first\nsecond");
        var first = new RichRevisionInfo("One");
        var second = new RichRevisionInfo("Two");
        var paragraphs = document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks;
        document.Edit(edit => edit.ReplaceText(new(edit.Body, paragraphs[0], 0), 5, "FIRST"), new(Revision: first));
        document.Edit(edit => edit.ReplaceText(new(edit.Body, paragraphs[1], 0), 6, "SECOND"), new(Revision: second));
        Assert.All(document.CurrentSnapshot.Revisions.Values, revision => Assert.Empty(revision.Dependencies));
        document.Edit(edit => edit.AcceptRevision(first.Group));
        document.Edit(edit => edit.RejectRevision(second.Group));
        Assert.Equal("FIRST\nsecond", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public void GroupResolutionComputesTransitiveDependenciesBeforeChangingContent()
    {
        var document = RichDocument.FromPlainText("a");
        var first = new RichRevisionInfo("One");
        var second = new RichRevisionInfo("Two");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 1), 0, "b"), new(Revision: first));
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 2), 0, "c"), new(Revision: second));
        Assert.Equal(2, document.CurrentSnapshot.GetRevisionResolution(first.Group, accept: false).Length);
        Assert.Single(document.CurrentSnapshot.GetRevisionResolution(first.Group, accept: true));
        Assert.Equal(2, document.CurrentSnapshot.GetRevisionResolution(second.Group, accept: true).Length);
        document.Edit(edit => edit.RejectRevision(first.Group));
        Assert.Equal("a", document.CurrentSnapshot.ProjectText().Text.ToString());
        document.Undo();
        document.Edit(edit => edit.AcceptRevision(second.Group));
        Assert.Equal("abc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Empty(document.CurrentSnapshot.Revisions);
    }

    [Fact]
    public void StructuralAndFormatChangesRetainTheirExactGraphAndStyles()
    {
        var document = RichDocument.FromPlainText("heading\nbody");
        var before = document.CurrentSnapshot;
        var group = new RichRevisionInfo("Author");
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { Bold = true });
            edit.SetParagraphFormat(new(2), new() { Alignment = RichAlignment.Center });
            edit.InsertTable(edit.Body, 1, 2, [new(1000), new(2000)]);
            edit.DeleteBlock(before.Get<RichStory>(before.Body).Blocks[1]);
        }, new(Revision: group));
        document.HistoryLimit = 0;
        document.Edit(edit => edit.RejectRevision(group.Group));
        Assert.True(before.ContentEquals(document.CurrentSnapshot));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void TrackingIncludesCommittedCompositionButNotProvisionalUpdates()
    {
        var document = RichDocument.FromPlainText("a");
        using var view = document.CreateSession();
        view.RevisionTracking = new("IME");
        using var composition = view.BeginComposition();
        composition.Update(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "仮"));
        composition.Update(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "確定"));
        Assert.Empty(document.CurrentSnapshot.Revisions);
        composition.Commit();
        Assert.Single(document.CurrentSnapshot.Revisions);
        document.Edit(edit => edit.RejectRevision(view.RevisionTracking.Group));
        Assert.Equal("a", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public void UntrackedOverlapAndMixedResolutionFailAtomically()
    {
        var document = RichDocument.FromPlainText("a\nz");
        var group = new RichRevisionInfo("Author");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "b"), new(Revision: group));
        var before = document.CurrentSnapshot;
        Assert.Throws<InvalidOperationException>(() => document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "c")));
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Throws<InvalidOperationException>(() => document.Edit(edit => { edit.AcceptRevision(group.Group); edit.ReplaceText(new(edit.Body, new(2), 0), 1, "c"); }));
        Assert.Throws<InvalidOperationException>(() => document.Edit(edit => { edit.ReplaceText(new(edit.Body, new(2), 0), 1, "c"); edit.AcceptRevision(group.Group); }));
        Assert.Same(before, document.CurrentSnapshot);
        var unrelated = before.Get<RichStory>(before.Body).Blocks[1];
        document.Edit(edit => edit.ReplaceText(new(edit.Body, unrelated, 0), 1, "Z"));
        document.Edit(edit => edit.RejectRevision(group.Group));
        Assert.Equal("a\nZ", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public void AcceptanceRetainsDependentRevisionsAndRemovesResolvedDependencies()
    {
        var document = RichDocument.FromPlainText("a");
        var first = new RichRevisionInfo("One");
        var second = new RichRevisionInfo("Two");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "b"), new(Revision: first));
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "c"), new(Revision: second));
        document.Edit(edit => edit.AcceptRevision(first.Group));
        Assert.Empty(Assert.Single(document.CurrentSnapshot.Revisions.Values).Dependencies);
        document.Edit(edit => edit.RejectRevision(second.Group));
        Assert.Equal("b", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public async Task PendingReviewStateCannotBeSilentlyLostByTheCodec()
    {
        var document = RichDocument.FromPlainText("a");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 1, "b"), new(Revision: new("One")));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RtfFidelityException>(() => RtfCodec.WriteAsync(document.CurrentSnapshot, output, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void AddedStylesAndTheirConsumersResolveTogether()
    {
        var document = RichDocument.FromPlainText("a\nb");
        var styleGroup = new RichRevisionInfo("Styles");
        var textGroup = new RichRevisionInfo("Writer");
        var style = new RichStyleId(1);
        document.Edit(edit => edit.SetStyles(new([new(style, "Heading", RichStyleKind.Paragraph) { Character = new() { Bold = true } }])), new(Revision: styleGroup));
        var second = document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks[1];
        document.Edit(edit => edit.SetParagraphStyle(second, style), new(Revision: textGroup));
        Assert.Equal(2, document.CurrentSnapshot.GetRevisionResolution(styleGroup.Group, accept: false).Length);
        document.Edit(edit => edit.RejectRevision(styleGroup.Group));
        Assert.Null(document.CurrentSnapshot.Get<RichParagraph>(second).Style);
        Assert.Empty(document.CurrentSnapshot.Styles.Definitions);
    }

    [Fact]
    public void AcceptRetainsBeforeContentInBudgetedUndoAndAffectsSaveState()
    {
        var document = RichDocument.FromPlainText(new string('a', 10_000));
        var group = new RichRevisionInfo("Author");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 10_000, "b"), new(Revision: group));
        var capture = document.CaptureSave();
        document.MarkSaved(capture);
        document.ClearHistory();
        document.Edit(edit => edit.AcceptRevision(group.Group));
        Assert.True(document.IsModified);
        Assert.True(document.RetainedHistoryByteEstimate > 20_000);
        document.Undo();
        Assert.False(document.IsModified);
        Assert.Single(document.CurrentSnapshot.Revisions);
    }

    [Fact]
    public void AdoptingReviewStateNeverReusesDeletedNodeIdentities()
    {
        var source = RichDocument.FromPlainText("a\nz");
        var deleted = source.CurrentSnapshot.Get<RichStory>(new(1)).Blocks[1];
        var group = new RichRevisionInfo("Author");
        source.Edit(edit => edit.DeleteBlock(deleted), new(Revision: group));
        var document = new RichDocument(source.CurrentSnapshot);
        RichNodeId added = default;
        document.Edit(edit => added = edit.InsertParagraph(edit.Body, 1, "new"), new(Revision: new("Other")));
        Assert.True(added.Value > deleted.Value);
        document.Edit(edit => edit.RejectRevision(group.Group));
        Assert.Equal("a\nz", document.CurrentSnapshot.ProjectText().Text.ToString());
    }
}
