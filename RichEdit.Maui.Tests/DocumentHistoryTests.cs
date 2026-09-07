using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.Tests;

public class DocumentHistoryTests
{
    [Fact]
    public void DerivedFormattingPreservesBothHistoryDirections()
    {
        var document = RichTextDocument.FromPlainText("a");
        document.Edit(edit => edit.InsertText(1, "b"));
        document.Edit(edit => edit.InsertText(2, "c"));
        document.Undo();
        var version = document.Version;
        var changes = document.Edit(edit => edit.UpdateCharacterFormat(new RichTextRange(0, 2),
            format => format with { ForegroundColor = Colors.Green }), new RichTextEditOptions(RichTextUndoBehavior.PreserveHistory));
        Assert.True(document.CanUndo);
        Assert.True(document.CanRedo);
        Assert.True(document.Version > version);
        Assert.False(changes.IsTextChanged);
        Assert.Equal(Colors.Green, document.CurrentSnapshot.Runs[0].Format.ForegroundColor);
        document.Redo();
        Assert.Equal("abc", document.Text);
        document.Undo();
        document.Undo();
        Assert.Equal("a", document.Text);
        Assert.False(document.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservingHistoryRejectsStructuralEditsAtomically(bool semantic)
    {
        var document = RichTextDocument.FromPlainText("text");
        document.Edit(edit => edit.InsertText(4, "!"));
        var before = document.CurrentSnapshot;
        Assert.Throws<InvalidOperationException>(() => document.Edit(edit =>
        {
            edit.UpdateCharacterFormat(new RichTextRange(0, 4), format => format with { Italic = true });
            if (semantic) edit.SetLink(new RichTextRange(0, 4), "https://example.com");
            else edit.InsertText(0, "unsafe");
        }, new RichTextEditOptions(RichTextUndoBehavior.PreserveHistory)));
        Assert.Same(before, document.CurrentSnapshot);
        document.Undo();
        Assert.Equal("text", document.Text);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void NestedGroupsKeepIndependentCommitsButOneHistoryUnit()
    {
        var document = RichTextDocument.FromPlainText("start");
        var notifications = 0;
        document.Changed += (_, _) => notifications++;
        using (document.BeginUndoGroup("Compound operation"))
        {
            document.Edit(edit => edit.InsertText(5, " one"));
            using (document.BeginUndoGroup()) document.Edit(edit => edit.InsertText(9, " two"));
            document.Edit(edit => edit.UpdateCharacterFormat(new RichTextRange(0, 5), format => format with { Italic = true }));
            Assert.True(document.IsUndoGroupOpen);
            Assert.False(document.CanUndo);
            Assert.False(document.CanRedo);
        }
        Assert.Equal(3, notifications);
        Assert.False(document.IsUndoGroupOpen);
        document.Undo();
        Assert.Equal("start", document.Text);
        Assert.False(document.CurrentSnapshot.Runs[0].Format.Italic);
        Assert.False(document.CanUndo);
        document.Redo();
        Assert.Equal("start one two", document.Text);
        Assert.True(document.CurrentSnapshot.Runs[0].Format.Italic);
    }

    [Fact]
    public void EmptyAndFormattingOnlyGroupsPreserveRedo()
    {
        var document = RichTextDocument.FromPlainText("a");
        document.Edit(edit => edit.InsertText(1, "b"));
        document.Undo();
        using (document.BeginUndoGroup()) { }
        Assert.True(document.CanRedo);
        using (document.BeginUndoGroup())
            document.Edit(edit => edit.UpdateCharacterFormat(new RichTextRange(0, 1), format => format with { ForegroundColor = Colors.Red }),
                new RichTextEditOptions(RichTextUndoBehavior.PreserveHistory));
        Assert.False(document.CanUndo);
        Assert.True(document.CanRedo);
        document.Redo();
        Assert.Equal("ab", document.Text);
    }

    [Fact]
    public void ExplicitGroupsDoNotMergeAcrossTheirBoundary()
    {
        var document = new RichTextDocument();
        document.Edit(edit => edit.InsertText(0, "before"));
        using (document.BeginUndoGroup())
        {
            document.Edit(edit => edit.InsertText(document.Length, " A"), new RichTextEditOptions(RichTextUndoBehavior.MergeWithPrevious));
            document.Edit(edit => edit.InsertText(document.Length, " B"));
        }
        document.Undo();
        Assert.Equal("before", document.Text);
        Assert.True(document.CanUndo);
        document.Undo();
        Assert.Empty(document.Text);
    }

    [Fact]
    public void ScopesCloseOnExceptionsAndEnforceReverseDisposal()
    {
        var document = new RichTextDocument();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var group = document.BeginUndoGroup();
            document.Edit(edit => edit.InsertText(0, "committed"));
            throw new InvalidOperationException();
        }));
        Assert.False(document.IsUndoGroupOpen);
        document.Undo();
        Assert.Empty(document.Text);
        var outer = document.BeginUndoGroup();
        var inner = document.BeginUndoGroup();
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        inner.Dispose();
        outer.Dispose();
        outer.Dispose();
        Assert.False(document.IsUndoGroupOpen);
    }

    [Theory]
    [InlineData(RichTextUndoBehavior.ClearHistory)]
    public void HistoryCannotBeClearedInsideAnOpenGroup(RichTextUndoBehavior behavior)
    {
        var document = RichTextDocument.FromPlainText("a");
        using var group = document.BeginUndoGroup();
        document.Edit(edit => edit.InsertText(1, "b"));
        var before = document.CurrentSnapshot;
        Assert.Throws<InvalidOperationException>(document.ClearUndoHistory);
        Assert.Throws<InvalidOperationException>(() => document.Edit(edit => edit.InsertText(2, "c"), new RichTextEditOptions(behavior)));
        Assert.Same(before, document.CurrentSnapshot);
    }

    [Fact]
    public void ObserverFailuresDoNotLeaveAnUnreachableUndoScope()
    {
        var document = new RichTextDocument();
        System.ComponentModel.PropertyChangedEventHandler failure = (_, args) =>
        {
            if (args.PropertyName == nameof(RichTextDocument.IsUndoGroupOpen)) throw new InvalidOperationException();
        };
        document.PropertyChanged += failure;
        Assert.Throws<InvalidOperationException>(() => document.BeginUndoGroup());
        Assert.False(document.IsUndoGroupOpen);
        document.PropertyChanged -= failure;
        var group = document.BeginUndoGroup();
        document.Edit(edit => edit.InsertText(0, "text"));
        document.PropertyChanged += failure;
        Assert.Throws<InvalidOperationException>(group.Dispose);
        Assert.False(document.IsUndoGroupOpen);
        document.PropertyChanged -= failure;
        group.Dispose();
        document.Undo();
        Assert.Empty(document.Text);
    }
}
