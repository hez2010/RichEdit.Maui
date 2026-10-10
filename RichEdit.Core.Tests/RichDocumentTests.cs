using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichDocumentTests
{
    [Fact]
    public void NativeTableEndCanAddressABoundaryWithoutCreatingContentUntilTyping()
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, [new(1000), new(1000)]);
            edit.DeleteBlock(new(2));
        }));
        using var view = document.CreateSession();
        var before = document.CurrentSnapshot;
        var boundary = new RichBoundarySelection(before.Body, table, RichBlockEdge.After);
        view.Selection = boundary;
        Assert.Same(before, document.CurrentSnapshot);
        Assert.False(document.IsModified);
        RichNodeId paragraph = default;
        view.Edit(edit =>
        {
            paragraph = edit.InsertParagraph(boundary, "tail");
            var point = new RichTextPoint(edit.Body, paragraph, 4);
            edit.SetSelection(new RichTextSelection(point, point));
        });
        Assert.Equal(paragraph, document.CurrentSnapshot.Get<RichStory>(before.Body).Blocks[^1]);
        Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Rows);
        view.Undo();
        Assert.Equal(boundary, view.Selection);
        Assert.Single(document.CurrentSnapshot.Get<RichStory>(before.Body).Blocks);
        Assert.False(document.IsModified);
    }

    [Fact]
    public void DocumentDefaultsResolveWithoutRewritingContentAndParticipateInHistory()
    {
        var document = RichDocument.FromPlainText("text");
        var before = document.CurrentSnapshot;
        var paragraph = before.Get<RichStory>(before.Body).Blocks[0];
        var run = before.Get<RichParagraph>(paragraph).Inlines[0];
        document.Edit(edit =>
        {
            edit.SetDefaultCharacterFormat(new() { FontFamily = "Example", FontSize = RichFontSize.FromPoints(14), Bold = true });
            edit.SetDefaultParagraphFormat(new() { Alignment = RichAlignment.Center });
        });
        Assert.Same(before.Nodes, document.CurrentSnapshot.Nodes);
        Assert.True(document.CurrentSnapshot.ResolveCharacter(run).Bold);
        Assert.Equal("Example", document.CurrentSnapshot.ResolveCharacter(run).FontFamily);
        Assert.Equal(RichAlignment.Center, document.CurrentSnapshot.ResolveParagraph(paragraph).Alignment);
        document.Edit(edit => edit.SetCharacterFormat(new(edit.Body, paragraph, 0), 4, new() { Bold = StyleValue<bool>.Initial }));
        Assert.False(document.CurrentSnapshot.ResolveCharacter(run).Bold);
        document.Undo();
        Assert.True(document.CurrentSnapshot.ResolveCharacter(run).Bold);
        document.Undo();
        Assert.False(document.CurrentSnapshot.ResolveCharacter(run).Bold);
        Assert.Null(document.CurrentSnapshot.ResolveCharacter(run).FontFamily);
        Assert.False(document.IsModified);
    }

    [Fact]
    public void CellSelectionsFollowRowAndColumnChangesAcrossDocumentUndo()
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit => table = edit.InsertTable(edit.Body, 1, 3, [new(1000), new(1000), new(1000)])));
        using var view = document.CreateSession();
        view.Selection = new RichCellSelection(table, 2, 2, 1, 1);
        document.Edit(edit => { edit.InsertRows(table, 1, 2); edit.InsertColumns(table, 0, [new(500)]); });
        Assert.Equal(new RichCellSelection(table, 4, 3, 1, 1), view.Selection);
        document.Undo();
        Assert.Equal(new RichCellSelection(table, 2, 2, 1, 1), view.Selection);
        document.Edit(edit => edit.DeleteRows(table, 2));
        Assert.Equal(new RichCellSelection(table, 1, 2, 1, 1), view.Selection);
    }

    [Fact]
    public void SharedSessionsSynchronizeBeforeObserversAndUndoRestoresTheInvokingSelection()
    {
        var document = RichDocument.FromPlainText("abcdef");
        using var first = document.CreateSession();
        using var second = document.CreateSession();
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        RichTextPoint Point(int offset) => new(document.CurrentSnapshot.Body, paragraph, offset);
        first.Selection = new RichTextSelection(Point(1), Point(3));
        second.Selection = new RichTextSelection(Point(5), Point(5));
        var calls = 0;
        document.Changed += (_, args) =>
        {
            calls++;
            if (args.Options.Origin == TextChangeOrigin.Programmatic)
            {
                Assert.Equal(4, ((RichTextSelection)first.Selection).Active.Offset);
                Assert.Equal(6, ((RichTextSelection)second.Selection).Active.Offset);
            }
            Assert.Throws<InvalidOperationException>(() => document.Edit(_ => { }));
            Assert.Throws<InvalidOperationException>(() => second.Selection = new RichTextSelection(Point(0), Point(0)));
        };
        first.Edit(edit => { edit.ReplaceText(Point(1), 2, "XYZ"); edit.SetSelection(new RichTextSelection(Point(4), Point(4))); });
        Assert.Equal("aXYZdef", document.CurrentSnapshot.ProjectText().Text.ToString());
        second.Undo();
        Assert.Equal(new RichTextSelection(Point(1), Point(3)), second.Selection);
        Assert.Equal("abcdef", document.CurrentSnapshot.ProjectText().Text.ToString());
        first.Redo();
        Assert.Equal(new RichTextSelection(Point(4), Point(4)), first.Selection);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void SplitParagraphUndoRestoresIdentityAndAnEarlierSaveCapture()
    {
        var document = RichDocument.FromPlainText("abcd");
        using var view = document.CreateSession();
        var before = document.CurrentSnapshot;
        var paragraph = before.Get<RichStory>(before.Body).Blocks[0];
        RichTextPoint Point(int offset) => new(before.Body, paragraph, offset);
        view.Selection = new RichTextSelection(Point(4), Point(2));
        var save = document.CaptureSave();
        RichNodeId split = default;
        view.Edit(edit => split = edit.SplitParagraph(Point(2)));
        document.MarkSaved(save);
        Assert.True(document.IsModified);
        Assert.Equal(new RichTextSelection(new(before.Body, split, 2), new(before.Body, split, 0)), view.Selection);
        view.Undo();
        Assert.False(document.IsModified);
        Assert.Equal(new RichTextSelection(Point(4), Point(2)), view.Selection);
        Assert.DoesNotContain(split, document.CurrentSnapshot.Nodes.Keys);
        view.Redo();
        Assert.Contains(split, document.CurrentSnapshot.Nodes.Keys);
        Assert.Equal("ab\ncd", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.Equal("abcd", save.Snapshot.ProjectText().Text.ToString());
        Assert.Throws<ArgumentException>(() => new RichDocument().MarkSaved(save));
    }

    [Fact]
    public void InvalidTransactionsAndStaleRequestsNeverChangeTheLiveRoot()
    {
        var document = RichDocument.FromPlainText("text");
        using var view = document.CreateSession();
        var before = document.CurrentSnapshot;
        var paragraph = before.Get<RichStory>(before.Body).Blocks[0];
        Assert.Throws<ArgumentException>(() => document.Edit(edit =>
        {
            edit.ReplaceText(new(before.Body, paragraph, 0), 4, "candidate");
            edit.SetParagraphStyle(paragraph, new(999));
        }));
        Assert.Same(before, document.CurrentSnapshot);
        Assert.False(document.CanUndo);
        Assert.Equal(TextEditRejection.StaleRevision, document.TryEdit(default, _ => throw new Exception()).Rejection);
        var generation = view.SelectionGeneration;
        view.Selection = new RichTextSelection(new(before.Body, paragraph, 2), new(before.Body, paragraph, 2));
        Assert.Equal(TextEditRejection.StaleSelection, view.Edit(_ => throw new Exception(), expectedSelectionGeneration: generation).Rejection);
        view.IsReadOnly = true;
        Assert.Equal(TextEditRejection.ReadOnly, view.Edit(_ => throw new Exception()).Rejection);
    }

    [Fact]
    public void ProjectionFailuresDoNotPreventPeersAndPublicObserversFromSeeingCommittedState()
    {
        var document = RichDocument.FromPlainText("a");
        using var first = document.CreateSession();
        using var second = document.CreateSession();
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        var point = new RichTextPoint(document.CurrentSnapshot.Body, paragraph, 1);
        first.Selection = second.Selection = new RichTextSelection(point, point);
        var observers = 0;
        using var failed = document.SubscribeProjection(_ => throw new InvalidOperationException("renderer"));
        using var successful = document.SubscribeProjection(_ =>
        {
            Assert.Equal(2, ((RichTextSelection)second.Selection).Active.Offset);
            observers++;
        });
        document.Changed += (_, _) => observers++;
        Assert.Throws<AggregateException>(() => first.Edit(edit => edit.ReplaceText(point, 0, "b")));
        Assert.Equal(2, observers);
        Assert.Equal("ab", document.CurrentSnapshot.ProjectText().Text.ToString());
        Assert.True(document.CanUndo);
    }

    [Fact]
    public void BoundedHistoryAndClearingDoNotAcknowledgeUnsavedContent()
    {
        var document = new RichDocument(RichDocumentSnapshot.FromPlainText("a")) { HistoryLimit = 2 };
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        for (var index = 0; index < 4; index++) document.Edit(edit => edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "x"));
        Assert.True(document.RetainedHistoryByteEstimate > 0);
        document.Undo();
        document.Undo();
        Assert.False(document.CanUndo);
        Assert.True(document.IsModified);
        document.ClearHistory();
        Assert.False(document.CanRedo);
        Assert.True(document.IsModified);
        Assert.Equal(0, document.RetainedHistoryByteEstimate);
        document.MarkSaved(document.CaptureSave());
        Assert.False(document.IsModified);
    }

    [Fact]
    public void BackgroundSnapshotReadsDoNotGrantAnotherThreadMutationAccess()
    {
        var document = RichDocument.FromPlainText("a");
        using var session = document.CreateSession();
        var snapshot = document.CurrentSnapshot;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Assert.Equal("a", snapshot.ProjectText().Text.ToString());
                Assert.Throws<InvalidOperationException>(() => document.Edit(_ => { }));
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start();
        worker.Join();
        Assert.Null(failure);
    }
}
