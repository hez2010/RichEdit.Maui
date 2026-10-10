using Xunit;

namespace RichEdit.Core.Tests;

public sealed class RichSelectionFormattingTests
{
    [Fact]
    public void ColumnFormattingIncludesEmptyAndNestedCellsWithoutTouchingOtherColumns()
    {
        RichNodeId table = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, [new(1440), new(1440)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(table);
            foreach (var row in Enumerable.Range(0, 2))
                edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(grid[row, 1]).Blocks[0], 0), 0, "untouched");
            var first = snapshot.Get<RichTableCell>(grid[0, 0]);
            edit.ReplaceText(new(edit.Body, first.Blocks[0], 0), 0, "selected");
            edit.InsertTable(first.Id, 1, 1, [new(720)]);
        }));
        using var session = document.CreateSession();
        var cells = new RichCellSelection(table, 0, 0, 2, 1);
        session.Selection = cells;
        var selected = document.CurrentSnapshot.GetSelectedText(cells);
        Assert.Equal(3, selected.Length);
        Assert.True(session.ToggleBold().Accepted);
        foreach (var range in selected)
        {
            var paragraph = document.CurrentSnapshot.Get<RichParagraph>(range.Start.Paragraph);
            Assert.True(paragraph.Character.Bold.Value);
            Assert.All(paragraph.Inlines, id => Assert.True(document.CurrentSnapshot.ResolveCharacter(id).Bold));
        }
        Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>().Where(run => run.Content.ToString() == "untouched"),
            run => Assert.False(document.CurrentSnapshot.ResolveCharacter(run.Id).Bold));
        session.ToggleBold();
        Assert.All(document.CurrentSnapshot.GetSelectedCharacterFormats(cells), format => Assert.False(format.Bold));
        session.Undo();
        Assert.Equal(cells, session.Selection);
        Assert.All(document.CurrentSnapshot.GetSelectedCharacterFormats(cells), format => Assert.True(format.Bold));
    }

    [Fact]
    public void DisjointRangesRetainStylesAndExplicitInheritanceCanBeRestored()
    {
        var document = RichDocument.FromPlainText("abcdef");
        using var session = document.CreateSession();
        var paragraph = new RichNodeId(2);
        RichTextSelectionItem Range(int start, int end) => new(new(new(new(1), paragraph, start), new(new(1), paragraph, end)));
        session.Selection = new RichTextSelectionSet(document.CurrentSnapshot, [Range(0, 2), Range(4, 6)]);
        session.ApplyCharacterFormat(new() { Italic = true });
        var runs = document.CurrentSnapshot.Get<RichParagraph>(paragraph).Inlines.Select(document.CurrentSnapshot.Get<RichText>).ToArray();
        Assert.Equal(["ab", "cd", "ef"], runs.Select(run => run.Content.ToString()));
        Assert.Equal([true, false, true], runs.Select(run => document.CurrentSnapshot.ResolveCharacter(run.Id).Italic));
        session.Edit(edit => edit.UpdateCharacterFormat(session.Selection, format => format with { Italic = StyleValue<bool>.Inherit }));
        Assert.Single(document.CurrentSnapshot.Get<RichParagraph>(paragraph).Inlines);
    }

    [Fact]
    public void CaretTogglesAreViewStateAndReadonlyOrCompositionRejectThem()
    {
        var document = RichDocument.FromPlainText("text");
        using var session = document.CreateSession();
        Assert.True(session.ToggleBold().Accepted);
        Assert.False(document.IsModified);
        Assert.False(document.CanUndo);
        Assert.True(session.ResolveTypingCharacterFormat().Bold);
        session.ToggleBold();
        Assert.False(session.ResolveTypingCharacterFormat().Bold);
        session.IsReadOnly = true;
        Assert.False(session.ToggleBold().Accepted);
        session.IsReadOnly = false;
        using var composition = session.BeginComposition();
        Assert.False(session.ToggleBold().Accepted);
    }

    [Fact]
    public void SelectionEndingAtParagraphStartExcludesThatParagraph()
    {
        var document = RichDocument.FromPlainText("first\nsecond");
        var blocks = document.CurrentSnapshot.Get<RichStory>(new(1)).Blocks;
        using var session = document.CreateSession();
        session.Selection = new RichTextSelection(new(new(1), blocks[1], 0), new(new(1), blocks[0], 2));
        session.Edit(edit => edit.ApplyParagraphFormat(session.Selection, new() { Alignment = RichAlignment.Center }));
        Assert.Equal(RichAlignment.Center, document.CurrentSnapshot.ResolveParagraph(blocks[0]).Alignment);
        Assert.Equal(RichAlignment.Left, document.CurrentSnapshot.ResolveParagraph(blocks[1]).Alignment);
    }
}
