using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextCompositionTests
{
    [Fact]
    public void ProvisionalUpdatesHoldOneLeaseThenMirrorAndCommitOnce()
    {
        var document = new TextDocument("aa bb");
        using var view = document.CreateSession();
        using var peer = document.CreateSession();
        view.Selections = new((TextSelection[])[new(2, 2), new(5, 5)], 1);
        peer.Selections = TextSelectionSet.Caret(3);
        var before = document.CurrentSnapshot;
        var observed = 0;
        document.Changed += (_, _) => observed++;
        using var composition = view.BeginComposition();
        composition.Update(edit => edit.Insert(5, "か"), new(6, 6));
        composition.Update(edit => edit.Replace(new(5, 1), "仮名"), new(7, 7));
        Assert.Equal("aa bb仮名", composition.Snapshot.ToString());
        Assert.False(composition.Snapshot.Revision.IsValid);
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Equal(0, observed);
        Assert.Throws<InvalidOperationException>(() => peer.BeginComposition());
        Assert.Throws<InvalidOperationException>(() => document.CaptureSave());
        Assert.Equal(TextEditRejection.CompositionActive, peer.Edit(edit => edit.Insert(0, "wrong")).Rejection);
        Assert.Throws<InvalidOperationException>(() => view.Selections = TextSelectionSet.Caret(0));
        Assert.True(composition.Commit().Accepted);
        Assert.Null(view.Composition);
        Assert.Equal("aa仮名 bb仮名", document.Text);
        Assert.Equal(new[] { 4, 9 }, view.Selections.Select(item => item.Active));
        Assert.Equal(5, peer.Selections.Primary.Active);
        Assert.Equal(1, observed);
        view.Undo();
        Assert.Equal("aa bb", document.Text);
        Assert.Equal(new[] { 2, 5 }, view.Selections.Select(item => item.Active));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void CancelAndEmptyCompletionPreserveTheOriginalSelectionsAndHistory()
    {
        var document = new TextDocument("abc def");
        using var view = document.CreateSession();
        view.Selections = new((TextSelection[])[new(0, 3), new(4, 7)]);
        var before = document.CurrentSnapshot;
        var selections = view.Selections;
        using (var composition = view.BeginComposition())
            composition.Update(edit => edit.Replace(new(0, 3), "marked"), new(6, 6));
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Same(selections, view.Selections);
        using (var composition = view.BeginComposition())
            Assert.True(composition.Commit().Accepted);
        Assert.Same(before, document.CurrentSnapshot);
        Assert.Equal(selections, view.Selections);
        Assert.False(document.IsModified);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void CompositionRulesRunOnceAndCannotRecursivelyEditTheLiveSource()
    {
        var document = new TextDocument("Con");
        using var view = document.CreateSession();
        view.Selections = TextSelectionSet.Caret(3);
        var count = 0;
        using var rule = view.InputRules.Register(input =>
        {
            count++;
            Assert.True(input.IsCompositionCommit);
            Assert.Throws<InvalidOperationException>(() => document.Edit(edit => edit.Insert(0, "wrong")));
            input.Replace(new(0, 4), "Console.");
        });
        using var composition = view.BeginComposition();
        composition.Update(edit => edit.Insert(3, "."), new(4, 4));
        Assert.Equal(0, count);
        composition.Commit();
        Assert.Equal(1, count);
        Assert.Equal("Console.", document.Text);
        Assert.Equal(8, view.Selections.Primary.Active);
        view.Undo();
        Assert.Equal("Con", document.Text);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void SurroundingTextReconversionDoesNotGuessMirroredReplacements()
    {
        var document = new TextDocument("aa bb");
        using var view = document.CreateSession();
        view.Selections = new((TextSelection[])[new(2, 2), new(5, 5)]);
        using var composition = view.BeginComposition();
        composition.Update(edit => edit.Replace(new(0, 2), "x"), new(1, 1));
        Assert.False(composition.CanMirrorSecondarySelections);
        composition.Commit();
        Assert.Equal("x bb", document.Text);
        Assert.Single(view.Selections);
        view.Undo();
        Assert.Equal("aa bb", document.Text);
        Assert.Equal(2, view.Selections.Count);
    }
}
