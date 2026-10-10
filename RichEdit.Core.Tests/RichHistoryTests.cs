using Xunit;

namespace RichEdit.Core.Tests;

public class RichHistoryTests
{
    [Fact]
    public void NestedGroupsPublishEachTransactionButUndoAsOneStructuralUnit()
    {
        var document = RichDocument.FromPlainText("abc");
        using var view = document.CreateSession();
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        var original = new RichTextSelection(new(document.CurrentSnapshot.Body, paragraph, 1), new(document.CurrentSnapshot.Body, paragraph, 1));
        view.Selection = original;
        var notifications = 0;
        document.Changed += (_, _) => notifications++;
        using (document.BeginUndoGroup("Compound"))
        {
            view.Edit(edit => edit.ReplaceText(original.Active, 0, "X"));
            using (document.BeginUndoGroup()) view.Edit(edit => edit.SplitParagraph(new(edit.Body, paragraph, 2)));
            Assert.True(document.IsUndoGroupOpen);
            Assert.False(document.CanUndo);
            Assert.Throws<InvalidOperationException>(document.Undo);
        }
        Assert.Equal(2, notifications);
        Assert.Equal("Compound", document.UndoDescription);
        view.Undo();
        Assert.Equal("abc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal(original, view.Selection);
        Assert.False(document.CanUndo);
        view.Redo();
        Assert.Equal("aX\nbc", document.CurrentSnapshot.ProjectText().Text.ToString());
    }

    [Fact]
    public void RestoringGroupedContentRestoresItsSavedStateIdentity()
    {
        var document = RichDocument.FromPlainText("abc");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        using (document.BeginUndoGroup())
        {
            document.Edit(edit => edit.ReplaceText(new(edit.Body, paragraph, 1), 0, "X"));
            document.Edit(edit => edit.ReplaceText(new(edit.Body, paragraph, 1), 1, ""));
        }
        Assert.Equal("abc", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.False(document.CanUndo);
        Assert.False(document.IsModified);
        Assert.Equal(2, document.Revision.Version);
    }

    [Fact]
    public void GroupScopesRejectOutOfOrderDisposalAndFailedOpeningDoesNotStrandAGroup()
    {
        var document = new RichDocument();
        var outer = document.BeginUndoGroup();
        var inner = document.BeginUndoGroup();
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        Assert.True(document.IsUndoGroupOpen);
        inner.Dispose();
        outer.Dispose();
        Assert.False(document.IsUndoGroupOpen);
        document.StateChanged += (_, _) => { if (document.IsUndoGroupOpen) throw new InvalidOperationException("observer"); };
        Assert.Throws<AggregateException>(() => document.BeginUndoGroup());
        Assert.False(document.IsUndoGroupOpen);
    }
}
