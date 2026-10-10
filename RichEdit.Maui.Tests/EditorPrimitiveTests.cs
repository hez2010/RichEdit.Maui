using CodeEdit.Maui;
using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.Tests;

public class EditorPrimitiveTests
{
    [Fact]
    public Task DecorationRequestsRejectOlderSettingsAgainstUnchangedSource() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView { Document = RichTextDocument.FromPlainText("same source") };
        using var layer = editor.Decorations.CreateLayer();
        using var other = editor.Decorations.CreateLayer();
        RichTextDecoration[] items = [new(new(0, 4), new() { ForegroundColor = Colors.Red })];
        var older = layer.BeginUpdate();
        var current = layer.BeginUpdate();
        Assert.Equal(older.Revision, current.Revision);
        Assert.False(layer.TryPublish(older, items));
        Assert.False(other.TryPublish(current, items));
        Assert.True(layer.TryPublish(current, items));
        Assert.False(editor.CanUndo);
        layer.Clear();
        Assert.False(layer.TryPublish(current, items));
        var replaced = layer.BeginUpdate();
        editor.Document = RichTextDocument.FromPlainText("same source");
        Assert.False(layer.TryPublish(replaced, items));
        var disposed = layer.BeginUpdate();
        layer.Dispose();
        Assert.False(layer.TryPublish(disposed, items));
    });

    [Fact]
    public Task DecorationPrioritiesComposeSpecifiedPropertiesIndependently() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView { Document = RichTextDocument.FromPlainText("text") };
        using var high = editor.Decorations.CreateLayer(priority: 10);
        using var low = editor.Decorations.CreateLayer(priority: -10);
        Assert.True(high.TrySet(editor.Document.Revision, new[] { new RichTextDecoration(new(0, 4), new() { ForegroundColor = Colors.Red }) }));
        Assert.True(low.TrySet(editor.Document.Revision, new[] { new RichTextDecoration(new(0, 4), new() { ForegroundColor = Colors.Blue, Italic = true }) }));
        var run = Assert.Single(editor.PresentationSnapshot.Runs);
        Assert.Equal(Colors.Red, run.Format.ForegroundColor);
        Assert.True(run.Format.Italic);
        Assert.Null(editor.Document.CurrentSnapshot.Runs[0].Format.ForegroundColor);
        Assert.False(editor.Document.IsModified);
        high.Dispose();
        Assert.Equal(Colors.Blue, Assert.Single(editor.PresentationSnapshot.Runs).Format.ForegroundColor);
    });

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
        Assert.Equal(new TextPosition(1, 2), captured.GetPosition(4));
        Assert.Equal(4, captured.GetOffset(new(1, 2)));
        code.Edit(edit => edit.InsertText(0, "new\n"));
        Assert.Equal(new TextSpan(2, 2), captured.GetLineRange(1));
    }

    [Fact]
    public Task CheckedBatchesUseOriginalCoordinatesAndStableInsertionOrder() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView { Document = RichTextDocument.FromPlainText("abcdef"), SelectionState = new(6, 0) };
        var before = editor.Document.Revision;
        Assert.True(editor.TryApplyEdits(before, (RichTextEdit[])[
            new(new(4, 2), "END"), new(new(1, 2), "R"), new(new(1, 0), "one"),
            new(new(3, 0), "\r\ntwo\r"), new(new(1, 0), "last") ]));
        Assert.Equal("aonelastR\ntwo\ndEND", editor.Document.Text);
        Assert.Equal(new TextSelectionState(editor.Document.Length, 0), editor.SelectionState);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Equal("abcdef", editor.Document.Text);
        Assert.Equal(new TextSelectionState(6, 0), editor.SelectionState);
        Assert.False(editor.CanUndo);
        editor.Redo();
        Assert.Equal("aonelastR\ntwo\ndEND", editor.Document.Text);
    });

    [Fact]
    public Task CheckedEditsRejectInvalidAndStaleRequestsAtomically() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("abcd"), SelectionState = new(3, 1) };
        var snapshot = editor.Document.CurrentSnapshot;
        TextReplacement[] insert = [new(new(0, 0), "x")];
        Assert.False(editor.TryApplyEdits(default, insert));
        Assert.False(editor.TryApplyEdits(new CodeDocument("abcd").Revision, insert));
        Assert.Throws<ArgumentException>(() => editor.TryApplyEdits(snapshot.Revision, (TextReplacement[])[new(new(0, 3), "a"), new(new(2, 0), "b")]));
        Assert.Throws<ArgumentException>(() => editor.TryApplyEdits(snapshot.Revision, (TextReplacement[])[new(new(0, 3), "a"), new(new(2, 2), "b")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.TryApplyEdits(snapshot.Revision, insert, new(9, 9)));
        Assert.Same(snapshot, editor.Document.CurrentSnapshot);
        Assert.Equal(new TextSelectionState(3, 1), editor.SelectionState);
        Assert.False(editor.CanUndo);
        editor.MaxLength = 4;
        Assert.False(editor.TryApplyEdits(snapshot.Revision, insert));
        editor.MaxLength = 1;
        Assert.True(editor.TryApplyEdits(snapshot.Revision, (TextReplacement[])[new(new(2, 2), "")]));
        Assert.Equal("ab", editor.Document.Text);
        Assert.False(editor.TryApplyEdits(snapshot.Revision, insert));
        editor.IsReadOnly = true;
        Assert.False(editor.TryApplyEdits(editor.Document.Revision, Array.Empty<TextReplacement>()));
    });

    [Fact]
    public Task IdenticalEditsKeepSelectionAndHistoryAndExplicitSelectionIsRecorded() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new NativeEditorView { Document = RichTextDocument.FromPlainText("abcd"), SelectionState = new(3, 1) };
        var revision = editor.Document.Revision;
        Assert.True(editor.TryApplyEdits(revision, (RichTextEdit[])[new(new(0, 4), "abcd")]));
        Assert.Equal(new TextSelectionState(3, 1), editor.SelectionState);
        Assert.Equal(revision, editor.Document.Revision);
        Assert.True(editor.TryApplyEdits(revision, Array.Empty<RichTextEdit>(), new(0, 0)));
        Assert.False(editor.CanUndo);
        Assert.True(editor.TryApplyEdits(revision, (RichTextEdit[])[new(new(1, 1), "\r\n")], new(2, 2)));
        Assert.Equal("a\ncd", editor.Document.Text);
        editor.Undo();
        Assert.Equal(new TextSelectionState(0, 0), editor.SelectionState);
        editor.Redo();
        Assert.Equal(new TextSelectionState(2, 2), editor.SelectionState);
    });

    [Theory]
    [InlineData(PositionAffinity.BeforeInsertion, 2)]
    [InlineData(PositionAffinity.AfterInsertion, 4)]
    public void PositionsHonorAffinity(PositionAffinity affinity, int expected)
    {
        var document = RichTextDocument.FromPlainText("abcd");
        using var handle = document.Tracking.TrackPosition(2, affinity);
        document.Edit(edit => edit.InsertText(2, "XY"));
        Assert.Equal(expected, handle.Position);
        document.Undo();
        // An after-insertion anchor is the end boundary of the removed text and survives.
        Assert.Equal(affinity == PositionAffinity.BeforeInsertion ? null : 2, handle.Position);
    }

    [Fact]
    public void InclusiveAndExclusiveRangesTrackEmptyAndInteriorInsertions()
    {
        var document = RichTextDocument.FromPlainText("abcd");
        using var exclusive = document.Tracking.TrackRange(new(1, 2));
        using var inclusive = document.Tracking.TrackRange(new(1, 2), PositionAffinity.BeforeInsertion, PositionAffinity.AfterInsertion);
        using var empty = document.Tracking.TrackRange(new(1, 0));
        using var inclusiveEmpty = document.Tracking.TrackRange(new(1, 0), PositionAffinity.BeforeInsertion, PositionAffinity.AfterInsertion);
        document.Edit(edit => edit.InsertText(1, "X"));
        Assert.Equal(new TextSpan(2, 2), exclusive.Range);
        Assert.Equal(new TextSpan(1, 3), inclusive.Range);
        Assert.Equal(new TextSpan(2, 0), empty.Range);
        Assert.Equal(new TextSpan(1, 1), inclusiveEmpty.Range);
        document.Edit(edit => edit.InsertText(3, "Y"));
        Assert.Equal(new TextSpan(2, 3), exclusive.Range);
    }

    [Fact]
    public void DeletionInvalidatesContentEvenWhenEndpointsSurvive()
    {
        var document = RichTextDocument.FromPlainText("abcdef");
        using var invalidated = document.Tracking.TrackRange(new(1, 4));
        using var preserved = document.Tracking.TrackRange(new(1, 4), deletion: TrackingDeletionBehavior.Preserve);
        using var point = document.Tracking.TrackPosition(2, deletion: TrackingDeletionBehavior.Preserve);
        document.Edit(edit => edit.ReplaceText(new(2, 2), "X"));
        Assert.Null(invalidated.Range);
        Assert.Equal(new TextSpan(1, 3), preserved.Range);
        Assert.Equal(3, point.Position);
        document.Undo();
        Assert.Null(invalidated.Range);
        Assert.Equal(new TextSpan(1, 4), preserved.Range);
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
        Assert.Equal(new TextSpan(4, 6), range.Range);
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
        var editor = new NativeEditorView { Document = RichTextDocument.FromPlainText("abcd") };
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
