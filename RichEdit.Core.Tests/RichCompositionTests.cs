using TextEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichCompositionTests
{
    [Fact]
    public void ProvisionalRichInputOwnsOneLeaseAndPublishesOneCompleteCommit()
    {
        var document = RichDocument.FromPlainText("ab");
        using var view = document.CreateSession();
        using var peer = document.CreateSession();
        var before = document.CurrentSnapshot;
        var paragraph = before.Get<RichStory>(before.Body).Blocks[0];
        RichTextPoint Point(int offset) => new(before.Body, paragraph, offset);
        view.Selection = new RichTextSelection(Point(1), Point(1));
        peer.Selection = new RichTextSelection(Point(2), Point(2));
        var published = 0;
        document.Changed += (_, _) =>
        {
            published++;
            Assert.Equal("aにb", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Equal(3, ((RichTextSelection)peer.Selection).Active.Offset);
        };
        using var composition = view.BeginComposition();
        var replaced = 0;
        foreach (var text in new[] { "n", "ni", "に" })
        {
            composition.Update(edit => edit.ReplaceText(Point(1), replaced, text), new RichTextSelection(Point(1 + text.Length), Point(1 + text.Length)));
            replaced = text.Length;
        }
        Assert.Same(before, document.CurrentSnapshot);
        Assert.False(composition.Snapshot.Revision.IsValid);
        Assert.Equal(3, composition.Generation);
        Assert.Equal(0, published);
        Assert.False(document.CanUndo);
        Assert.Equal(TextEditRejection.CompositionActive, peer.Edit(_ => { }).Rejection);
        Assert.Throws<InvalidOperationException>(() => document.Edit(_ => { }));
        Assert.Throws<InvalidOperationException>(() => document.CaptureSave());
        Assert.Throws<InvalidOperationException>(() => view.Selection = new RichTextSelection(Point(0), Point(0)));
        Assert.True(composition.Commit().Accepted);
        Assert.Equal(1, published);
        Assert.Null(view.Composition);
        Assert.True(document.CanUndo);
    }

    [Fact]
    public void ATableCellCompositionUsesParentHistoryAndCancellationRestoresTheWholeRoot()
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit => table = edit.InsertTable(edit.Body, 1, 2, [new(1000), new(1000)])));
        var initial = document.CurrentSnapshot;
        var paragraph = initial.Get<RichTableCell>(initial.GetTableGrid(table)[1, 1]).Blocks[0];
        var point = new RichTextPoint(initial.Body, paragraph, 0);
        using var session = document.CreateSession();
        session.Selection = new RichTextSelection(point, point);
        using (var canceled = session.BeginComposition())
        {
            canceled.Update(edit => edit.ReplaceText(point, 0, "temporary"));
            Assert.Equal(2, canceled.Snapshot.GetTableGrid(table).Rows);
        }
        Assert.Same(initial, document.CurrentSnapshot);
        Assert.False(document.CanUndo);
        using (var committed = session.BeginComposition())
        {
            committed.Update(edit => edit.ReplaceText(point, 0, "日本語"));
            committed.Commit();
        }
        Assert.Equal(3, document.CurrentSnapshot.GetParagraphLength(paragraph));
        session.Undo();
        Assert.Equal(0, document.CurrentSnapshot.GetParagraphLength(paragraph));
        Assert.Equal(2, document.CurrentSnapshot.GetTableGrid(table).Rows);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void CompositionCanMaterializeATableBoundaryParagraphOnlyAtCommit()
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 1, [new(1000)]);
            edit.DeleteBlock(new(2));
        }));
        using var session = document.CreateSession();
        var boundary = new RichBoundarySelection(document.CurrentSnapshot.Body, table, RichBlockEdge.After);
        session.Selection = boundary;
        using var composition = session.BeginComposition();
        composition.Update(edit =>
        {
            var paragraph = edit.InsertParagraph(boundary, "composed");
            var point = new RichTextPoint(edit.Body, paragraph, 8);
            edit.SetSelection(new RichTextSelection(point, point));
        });
        Assert.Single(document.CurrentSnapshot.Get<RichStory>(boundary.Story).Blocks);
        Assert.Equal(2, composition.Snapshot.Get<RichStory>(boundary.Story).Blocks.Count);
        composition.Commit();
        Assert.Equal(2, document.CurrentSnapshot.Get<RichStory>(boundary.Story).Blocks.Count);
        session.Undo();
        Assert.Equal(boundary, session.Selection);
        Assert.Single(document.CurrentSnapshot.Get<RichStory>(boundary.Story).Blocks);
    }

    [Fact]
    public void InvalidProvisionalUpdatesLeaveTheCurrentPreviewAndLeaseIntact()
    {
        var document = RichDocument.FromPlainText("ab");
        var paragraph = document.CurrentSnapshot.Get<RichStory>(document.CurrentSnapshot.Body).Blocks[0];
        using var session = document.CreateSession();
        using var composition = session.BeginComposition();
        var before = composition.Snapshot;
        Assert.Throws<ArgumentException>(() => composition.Update(edit =>
        {
            edit.ReplaceText(new(edit.Body, paragraph, 1), 0, "candidate");
            edit.SetParagraphStyle(paragraph, new(999));
        }));
        Assert.Same(before, composition.Snapshot);
        Assert.Same(composition, session.Composition);
        composition.Update(edit => edit.ReplaceText(new(edit.Body, paragraph, 1), 0, "x"));
        composition.Update(edit => edit.ReplaceText(new(edit.Body, paragraph, 1), 1, ""));
        composition.Commit();
        Assert.False(document.CanUndo);
        Assert.False(document.IsModified);
        Assert.Equal(0, document.Revision.Version);
    }
}
