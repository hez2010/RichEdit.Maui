using CodeEdit.Maui;
using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.Tests;

public class EditorPrimitiveTests
{
    [Fact]
    public void RevisionsCarryIdentityAcrossSnapshotsAndHistory()
    {
        var first = RichTextDocument.FromPlainText("abc");
        var second = RichTextDocument.FromPlainText("abc");
        Assert.NotEqual(default, first.Revision);
        Assert.NotEqual(first.Revision, second.Revision);
        var snapshot = first.CurrentSnapshot;
        Assert.Equal(first.Revision, snapshot.Revision);
        first.Edit(edit => edit.InsertText(0, "x"));
        first.Undo();
        Assert.Equal(snapshot.Text, first.Text);
        Assert.NotEqual(snapshot.Revision, first.Revision);
        Assert.Equal(2, first.Revision.Version);
        var code = new CodeDocument("a\n😀\n");
        var captured = code.CurrentSnapshot;
        Assert.Equal(code.Source.Revision, captured.Revision);
        Assert.Equal(3, captured.LineCount);
        Assert.Equal(new CodePosition(2, 3), captured.GetPosition(4));
        Assert.Equal(4, captured.GetOffset(new(2, 3)));
        code.Edit(edit => edit.InsertText(0, "new\n"));
        Assert.Equal(new RichTextRange(2, 2), captured.GetLineRange(2));
    }

    [Fact]
    public Task CheckedBatchesUseOriginalCoordinatesAndStableInsertionOrder() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcdef"), SelectionState = new(6, 0) };
        var before = editor.Document.Revision;
        Assert.True(editor.TryApplyEdits(before, (RichTextEdit[])[
            new(new(4, 2), "END"), new(new(1, 2), "R"), new(new(1, 0), "one"),
            new(new(3, 0), "\r\ntwo\r"), new(new(1, 0), "last") ]));
        Assert.Equal("aonelastR\ntwo\ndEND", editor.Document.Text);
        Assert.Equal(new RichTextSelectionState(editor.Document.Length, 0), editor.SelectionState);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Equal("abcdef", editor.Document.Text);
        Assert.Equal(new RichTextSelectionState(6, 0), editor.SelectionState);
        Assert.False(editor.CanUndo);
        editor.Redo();
        Assert.Equal("aonelastR\ntwo\ndEND", editor.Document.Text);
    });

    [Fact]
    public Task CheckedEditsRejectInvalidAndStaleRequestsAtomically() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("abcd"), SelectionState = new(3, 1) };
        var snapshot = editor.Document.CurrentSnapshot;
        RichTextEdit[] insert = [new(new(0, 0), "x")];
        Assert.False(editor.TryApplyEdits(default, insert));
        Assert.False(editor.TryApplyEdits(new CodeDocument("abcd").Revision, insert));
        Assert.Throws<ArgumentException>(() => editor.TryApplyEdits(snapshot.Revision, (RichTextEdit[])[new(new(0, 3), "a"), new(new(2, 0), "b")]));
        Assert.Throws<ArgumentException>(() => editor.TryApplyEdits(snapshot.Revision, (RichTextEdit[])[new(new(0, 3), "a"), new(new(2, 2), "b")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.TryApplyEdits(snapshot.Revision, insert, new(9, 9)));
        Assert.Same(snapshot, editor.Document.CurrentSnapshot);
        Assert.Equal(new RichTextSelectionState(3, 1), editor.SelectionState);
        Assert.False(editor.CanUndo);
        editor.MaxLength = 4;
        Assert.False(editor.TryApplyEdits(snapshot.Revision, insert));
        editor.MaxLength = 1;
        Assert.True(editor.TryApplyEdits(snapshot.Revision, (RichTextEdit[])[new(new(2, 2), "")]));
        Assert.Equal("ab", editor.Document.Text);
        Assert.False(editor.TryApplyEdits(snapshot.Revision, insert));
        editor.IsReadOnly = true;
        Assert.False(editor.TryApplyEdits(editor.Document.Revision, Array.Empty<RichTextEdit>()));
    });

    [Fact]
    public Task IdenticalEditsKeepSelectionAndHistoryAndExplicitSelectionIsRecorded() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcd"), SelectionState = new(3, 1) };
        var revision = editor.Document.Revision;
        Assert.True(editor.TryApplyEdits(revision, (RichTextEdit[])[new(new(0, 4), "abcd")]));
        Assert.Equal(new RichTextSelectionState(3, 1), editor.SelectionState);
        Assert.Equal(revision, editor.Document.Revision);
        Assert.True(editor.TryApplyEdits(revision, Array.Empty<RichTextEdit>(), new(0, 0)));
        Assert.False(editor.CanUndo);
        Assert.True(editor.TryApplyEdits(revision, (RichTextEdit[])[new(new(1, 1), "\r\n")], new(2, 2)));
        Assert.Equal("a\ncd", editor.Document.Text);
        editor.Undo();
        Assert.Equal(new RichTextSelectionState(0, 0), editor.SelectionState);
        editor.Redo();
        Assert.Equal(new RichTextSelectionState(2, 2), editor.SelectionState);
    });

    [Theory]
    [InlineData(RichTextTrackingAffinity.BeforeInsertion, 2)]
    [InlineData(RichTextTrackingAffinity.AfterInsertion, 4)]
    public void PositionsHonorAffinity(RichTextTrackingAffinity affinity, int expected)
    {
        var document = RichTextDocument.FromPlainText("abcd");
        using var handle = document.Tracking.TrackPosition(2, affinity);
        document.Edit(edit => edit.InsertText(2, "XY"));
        Assert.Equal(expected, handle.Position);
        document.Undo();
        // An after-insertion anchor is the end boundary of the removed text and survives.
        Assert.Equal(affinity == RichTextTrackingAffinity.BeforeInsertion ? null : 2, handle.Position);
    }

    [Fact]
    public void InclusiveAndExclusiveRangesTrackEmptyAndInteriorInsertions()
    {
        var document = RichTextDocument.FromPlainText("abcd");
        using var exclusive = document.Tracking.TrackRange(new(1, 2));
        using var inclusive = document.Tracking.TrackRange(new(1, 2), RichTextTrackingAffinity.BeforeInsertion, RichTextTrackingAffinity.AfterInsertion);
        using var empty = document.Tracking.TrackRange(new(1, 0));
        using var inclusiveEmpty = document.Tracking.TrackRange(new(1, 0), RichTextTrackingAffinity.BeforeInsertion, RichTextTrackingAffinity.AfterInsertion);
        document.Edit(edit => edit.InsertText(1, "X"));
        Assert.Equal(new RichTextRange(2, 2), exclusive.Range);
        Assert.Equal(new RichTextRange(1, 3), inclusive.Range);
        Assert.Equal(new RichTextRange(2, 0), empty.Range);
        Assert.Equal(new RichTextRange(1, 1), inclusiveEmpty.Range);
        document.Edit(edit => edit.InsertText(3, "Y"));
        Assert.Equal(new RichTextRange(2, 3), exclusive.Range);
    }

    [Fact]
    public void DeletionInvalidatesContentEvenWhenEndpointsSurvive()
    {
        var document = RichTextDocument.FromPlainText("abcdef");
        using var invalidated = document.Tracking.TrackRange(new(1, 4));
        using var preserved = document.Tracking.TrackRange(new(1, 4), deletion: RichTextTrackingDeletionBehavior.Preserve);
        using var point = document.Tracking.TrackPosition(2, deletion: RichTextTrackingDeletionBehavior.Preserve);
        document.Edit(edit => edit.ReplaceText(new(2, 2), "X"));
        Assert.Null(invalidated.Range);
        Assert.Equal(new RichTextRange(1, 3), preserved.Range);
        Assert.Equal(3, point.Position);
        document.Undo();
        Assert.Null(invalidated.Range);
        Assert.Equal(new RichTextRange(1, 4), preserved.Range);
        point.Dispose();
        point.Dispose();
        Assert.Null(point.Position);
    }

    [Fact]
    public void DisjointGroupedAndMergedHistoryKeepsUntouchedBookmarks()
    {
        var document = RichTextDocument.FromPlainText("abc middle xyz");
        using var position = document.Tracking.TrackPosition(6);
        using var range = document.Tracking.TrackRange(new(4, 6));
        using (document.BeginUndoGroup("Surrounding edits"))
        {
            document.Edit(edit => edit.ReplaceText(new(11, 3), "end"));
            document.Edit(edit => edit.ReplaceText(new(0, 3), "start"));
        }
        Assert.Equal(8, position.Position);
        document.Undo();
        Assert.Equal(6, position.Position);
        Assert.Equal(new RichTextRange(4, 6), range.Range);
        document.Redo();
        Assert.Equal(8, position.Position);
        document.Edit(edit => edit.InsertText(document.Length, "!"), new(RichTextUndoBehavior.MergeWithPrevious));
        document.Undo();
        Assert.Equal(6, position.Position);
        Assert.Equal("abc middle xyz", document.Text);
    }

    [Fact]
    public void TrackingIsCoherentBeforeNotificationsAndResetInvalidates()
    {
        var document = RichTextDocument.FromPlainText("abc");
        using var position = document.Tracking.TrackPosition(2);
        document.Changed += CheckPosition;
        document.Edit(edit => edit.InsertText(0, "x"));
        document.Changed -= CheckPosition;
        document.Edit(edit => edit.SetCharacterFormat(new(0, 1), new() { FontWeight = 700 }));
        Assert.Equal(3, position.Position);
        document.Edit(edit => edit.ReplaceText(new(0, document.Length), "reset"));
        Assert.Null(position.Position);
        document.Undo();
        Assert.Null(position.Position);
        void CheckPosition(object? sender, RichTextDocumentChangedEventArgs args) => Assert.Equal(3, position.Position);
    }

    [Fact]
    public Task DecorationPublicationChecksIdentityAndValidatesBeforeReplacing() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new RichEditor { Document = RichTextDocument.FromPlainText("abcd") };
        using var layer = editor.Decorations.CreateLayer();
        RichTextDecoration[] valid = [new(new(0, 2), new() { ForegroundColor = Colors.Green })];
        Assert.True(layer.TrySet(editor.Document.Revision, valid));
        var before = editor.Decorations.Version;
        Assert.False(layer.TrySet(default, valid));
        Assert.False(layer.TrySet(RichTextDocument.FromPlainText("abcd").Revision, valid));
        Assert.Throws<ArgumentException>(() => layer.TrySet(editor.Document.Revision, (RichTextDecoration[])[valid[0], new(new(1, 2), valid[0].Style)]));
        Assert.Equal(before, editor.Decorations.Version);
        Assert.Equal(valid, layer.Items);
        var previous = editor.Document;
        var replaced = false;
        editor.DocumentChanged += (_, args) =>
        {
            Assert.Same(previous, args.OldDocument);
            Assert.Same(editor.Document, args.NewDocument);
            Assert.Empty(layer.Items);
            replaced = true;
        };
        editor.Document = RichTextDocument.FromPlainText("abcd");
        Assert.True(replaced);
        Assert.False(layer.TrySet(previous.Revision, valid));
        Assert.True(layer.TrySet(editor.Document.Revision, valid));
    });
}
