using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextHistoryTests
{
    [Fact]
    public void ExplicitNestedGroupsKeepOneHistoryUnitAndRejectOutOfOrderDisposal()
    {
        var document = new TextDocument();
        var outer = document.BeginUndoGroup("Create pair");
        document.Edit(edit => edit.Insert(0, "a"));
        var inner = document.BeginUndoGroup();
        Assert.Throws<InvalidOperationException>(() => outer.Dispose());
        document.Edit(edit => edit.Insert(1, "b"));
        inner.Dispose();
        Assert.True(document.IsUndoGroupOpen);
        Assert.Throws<InvalidOperationException>(() => document.Undo());
        outer.Dispose();
        Assert.False(document.IsUndoGroupOpen);
        Assert.Equal("Create pair", document.UndoDescription);
        document.Undo();
        Assert.Equal("", document.Text);
        Assert.False(document.CanUndo);
        document.Redo();
        Assert.Equal("ab", document.Text);
    }

    [Fact]
    public void TypingCoalescesOnlyWithSelectionContinuityAndOneInvokingSession()
    {
        var document = new TextDocument();
        using var first = document.CreateSession();
        using var second = document.CreateSession();
        void Type(TextEditorSession view, string text) => view.Edit(edit => edit.Insert(view.Selections.Primary.Active, text), new(Origin: TextChangeOrigin.User));
        Type(first, "a");
        Type(first, "b");
        var saved = document.CaptureSave();
        Type(first, "c");
        Type(second, "d");
        Assert.Equal("abcd", document.Text);
        first.Undo();
        Assert.Equal("abc", document.Text);
        first.Undo();
        Assert.Equal("ab", document.Text);
        document.MarkSaved(saved);
        Assert.False(document.IsModified);
        first.Undo();
        Assert.Equal("", document.Text);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void SelectionChangesAndCompositionBoundariesEndTypingGroups()
    {
        var document = new TextDocument();
        using var view = document.CreateSession();
        view.Edit(edit => edit.Insert(0, "a"), new(Origin: TextChangeOrigin.User));
        view.Selections = TextSelectionSet.Caret(1);
        view.Edit(edit => edit.Insert(1, "b"), new(Origin: TextChangeOrigin.User));
        using (var composition = view.BeginComposition())
        {
            composition.Update(edit => edit.Insert(2, "c"), new(3, 3));
            composition.Commit();
        }
        view.Edit(edit => edit.Insert(3, "d"), new(Origin: TextChangeOrigin.User));
        for (var length = 3; length >= 0; length--)
        {
            view.Undo();
            Assert.Equal("abcd"[..length], document.Text);
        }
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void ByteBudgetAndSaveCapturesDoNotDependOnExternalSnapshotLifetimes()
    {
        var document = new TextDocument("start") { HistoryByteLimit = 20_000 };
        var captures = new List<TextSaveCapture>();
        for (var i = 0; i < 20; i++)
        {
            document.Edit(edit => edit.Insert(0, "x"));
            captures.Add(document.CaptureSave());
        }
        Assert.InRange(document.RetainedHistoryByteEstimate, 1, 20_000);
        Assert.Equal("xstart", captures[0].Snapshot.ToString());
        document.HistoryByteLimit = 0;
        Assert.False(document.CanUndo);
        Assert.Equal(0, document.RetainedHistoryByteEstimate);
        Assert.Equal("xstart", captures[0].Snapshot.ToString());
    }

    [Fact]
    public void ChangeObserversCanCaptureTheExactCommittedStateForAutosave()
    {
        var document = new TextDocument();
        TextSaveCapture? captured = null;
        document.Changed += (_, _) => captured = document.CaptureSave();
        document.Edit(edit => edit.Insert(0, "saved"));
        Assert.Equal(document.Revision, captured!.Snapshot.Revision);
        Assert.Equal("saved", captured.Snapshot.ToString());
        document.MarkSaved(captured);
        Assert.False(document.IsModified);
    }

    [Fact]
    public void CountBudgetIncludesBothUndoAndRedo()
    {
        var document = new TextDocument();
        document.Edit(edit => edit.Insert(0, "a"));
        document.Edit(edit => edit.Insert(1, "b"));
        document.Undo();
        document.HistoryLimit = 1;
        Assert.False(document.CanUndo);
        Assert.True(document.CanRedo);
        document.Redo();
        Assert.Equal("ab", document.Text);
    }
}
