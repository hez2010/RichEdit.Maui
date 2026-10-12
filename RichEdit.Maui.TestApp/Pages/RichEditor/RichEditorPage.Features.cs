using RichEdit.Core;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage
{
    private bool _restoringTracking;
    private void RunRich(Func<RichEditResult> action)
    {
        try
        {
            var result = action();
            if (!result.Accepted) StatusLabel.Text = $"Edit not applied: {result.Rejection}.";
            else { UpdateStatus(); UpdateFormattingTools(); Editor.Focus(); }
        }
        catch (ArgumentException error) { StatusLabel.Text = error.Message; }
        catch (InvalidOperationException error) { StatusLabel.Text = error.Message; }
    }

    private async void OnStyles(object? sender, EventArgs args)
    {
        var document = Editor.Document;
        var revision = document.Revision;
        var generation = Editor.Session.SelectionGeneration;
        var selection = Editor.Selection;
        var choice = await DisplayActionSheetAsync("Styles & lists", "Cancel", null,
            "Body", "Heading", "Restyle headings", "Bullet list", "Numbered list", "Remove list");
        if (choice is null or "Cancel" || !ReferenceEquals(document, Editor.Document)) return;
        RunRich(() => Editor.Edit(edit =>
        {
            var snapshot = edit.Snapshot;
            var paragraphs = snapshot.GetSelectedText(selection).Select(range => range.Start.Paragraph).Distinct().ToArray();
            if (choice is "Body" or "Heading" or "Restyle headings")
            {
                var name = choice == "Body" ? "Body" : "Heading";
                var style = snapshot.Styles.Definitions.Values.FirstOrDefault(item => item.Kind == RichStyleKind.Paragraph && item.Name == name);
                if (style is null)
                {
                    var id = new RichStyleId(snapshot.Styles.Definitions.Keys.Select(item => item.Value).DefaultIfEmpty().Max() + 1);
                    style = new(id, name, RichStyleKind.Paragraph)
                    {
                        Character = name == "Heading" ? new() { Bold = true, FontSize = RichFontSize.FromPoints(22) } : new(),
                        Paragraph = name == "Heading" ? new() { HeadingLevel = 1 } : new(),
                    };
                }
                if (choice == "Restyle headings") style = style with
                {
                    Character = style.Character with { Foreground = new RichColor(23, 109, 101), FontSize = RichFontSize.FromPoints(26) },
                };
                edit.SetStyles(new(snapshot.Styles.Definitions.SetItem(style.Id, style).Values));
                if (choice != "Restyle headings") foreach (var paragraph in paragraphs) edit.SetParagraphStyle(paragraph, style.Id);
                return;
            }
            if (paragraphs.Length == 0) throw new InvalidOperationException("Select a paragraph first.");
            var first = snapshot.Get<RichParagraph>(paragraphs[0]);
            if (choice == "Remove list")
            {
                var node = (RichNode)first;
                while (node is not RichList && node.Parent is { } parent) node = snapshot.Get<RichNode>(parent);
                if (node is not RichList list) throw new InvalidOperationException("Place the caret inside a list first.");
                edit.UnwrapList(list.Id);
                return;
            }
            var parentNode = snapshot.Get<RichNode>(first.Parent!.Value);
            var blocks = parentNode switch
            {
                RichStory story => story.Blocks,
                RichTableCell cell => cell.Blocks,
                RichListItem item => item.Blocks,
                _ => throw new InvalidOperationException("Select paragraphs in one text flow."),
            };
            var start = blocks.IndexOf(first.Id);
            if (!paragraphs.SequenceEqual(blocks.Skip(start).Take(paragraphs.Length)))
                throw new InvalidOperationException("Select consecutive paragraphs in one text flow.");
            edit.WrapInList(parentNode.Id, start, paragraphs.Length, numbered: choice == "Numbered list");
        }, new(choice), revision, generation));
    }

    private async void OnTableTools(object? sender, EventArgs args)
    {
        var document = Editor.Document;
        var revision = document.Revision;
        var generation = Editor.Session.SelectionGeneration;
        var selection = Editor.Selection;
        var table = SelectedTable();
        var choice = await DisplayActionSheetAsync("Table", "Cancel", null, "Insert table", "Select first row", "Add row",
            "Add column", "Select first column", "Select all cells", "Clear selected cells", "Delete selected rows", "Delete selected columns", "Merge selected cells", "Split selected cells", "Equal column widths", "Next cell");
        if (choice is null or "Cancel" || !ReferenceEquals(document, Editor.Document)) return;
        if (document.Revision != revision || Editor.Session.SelectionGeneration != generation)
        { StatusLabel.Text = "The selection changed. Open Table again."; return; }
        if (choice == "Insert table") { RunRich(() => Editor.ReplaceSelection(SampleTable())); return; }
        if (table is not { } tableId) { StatusLabel.Text = "Place the caret in a table first."; return; }
        if (choice == "Next cell") { Editor.MoveToAdjacentCell(); Editor.Focus(); return; }
        var grid = document.CurrentSnapshot.GetTableGrid(tableId);
        if (choice == "Select first row") { Editor.Selection = new RichCellSelection(tableId, 0, 0, 1, grid.Columns); Editor.Focus(); return; }
        if (choice == "Select first column") { Editor.Selection = new RichCellSelection(tableId, 0, 0, grid.Rows, 1); Editor.Focus(); return; }
        if (choice == "Select all cells") { Editor.Selection = new RichCellSelection(tableId, 0, 0, grid.Rows, grid.Columns); Editor.Focus(); return; }
        RunRich(() => Editor.Edit(edit =>
        {
            switch (choice)
            {
                case "Add row": edit.InsertRows(tableId, grid.Rows); break;
                case "Add column": edit.InsertColumns(tableId, grid.Columns, (RichLength[])[RichLength.FromPoints(100)]); break;
                case "Clear selected cells" when selection is RichCellSelection cleared: edit.ClearCells(cleared); break;
                case "Delete selected rows" when selection is RichCellSelection rows: edit.DeleteRows(tableId, rows.Row, rows.RowCount); break;
                case "Delete selected columns" when selection is RichCellSelection columns: edit.DeleteColumns(tableId, columns.Column, columns.ColumnCount); break;
                case "Merge selected cells" when selection is RichCellSelection cells: edit.MergeCells(cells); break;
                case "Split selected cells" when selection is RichCellSelection cells:
                    var ids = new HashSet<RichNodeId>();
                    for (var row = cells.Row; row < cells.Row + cells.RowCount; row++)
                        for (var column = cells.Column; column < cells.Column + cells.ColumnCount; column++) ids.Add(grid[row, column]);
                    foreach (var id in ids) edit.SplitCell(id);
                    break;
                case "Equal column widths": edit.SetColumnWidths(tableId, Enumerable.Repeat(RichLength.FromPoints(120), grid.Columns)); break;
                default: throw new InvalidOperationException("Select table cells first.");
            }
        }, new(choice), revision, generation));
    }

    private static RichDocumentFragment SampleTable() => new(RichDocumentSnapshot.Build(edit =>
    {
        edit.InsertTable(edit.Body, 1, 2, (RichLength[])[RichLength.FromPoints(120), RichLength.FromPoints(120)]);
        edit.DeleteBlock(new(2));
    }));

    private async void OnAnnotations(object? sender, EventArgs args)
    {
        var document = Editor.Document;
        var revision = document.Revision;
        var generation = Editor.Session.SelectionGeneration;
        var range = Editor.Selection switch { RichEdit.Core.RichTextSelection selected => selected, RichTextSelectionSet set => set.Primary.Selection, _ => null };
        var choice = await DisplayActionSheetAsync("Annotations", "Cancel", null, "Add comment", "Add bookmark", "Browse comments & bookmarks");
        if (choice == "Browse comments & bookmarks") { await ShowAnnotationsAsync(); return; }
        if (choice is not ("Add comment" or "Add bookmark")) return;
        if (range is null) { StatusLabel.Text = "Select text or place the caret in a paragraph first."; return; }
        var text = await DisplayPromptAsync(choice, choice == "Add comment" ? "Comment text" : "Bookmark name (letters, digits, underscore)", "Add", "Cancel");
        if (string.IsNullOrWhiteSpace(text) || !ReferenceEquals(document, Editor.Document)) return;
        RunRich(() => Editor.Edit(edit =>
        {
            if (choice == "Add comment") edit.AddComment(range, text, "You", DateTimeOffset.UtcNow);
            else edit.AddBookmark(text, range);
        }, new(choice), revision, generation));
    }

    private async Task ShowAnnotationsAsync()
    {
        var document = Editor.Document;
        var snapshot = document.CurrentSnapshot;
        var items = snapshot.Comments.Values.OrderBy(item => item.Id.Value)
            .Select(item => (item.Id, item.Range, Label: $"Comment {item.Id.Value}: {item.Text}"))
            .Concat(snapshot.Bookmarks.Values.OrderBy(item => item.Id.Value)
                .Select(item => (item.Id, item.Range, Label: $"Bookmark: {item.Name}"))).ToArray();
        if (items.Length == 0) { StatusLabel.Text = "This document has no comments or bookmarks."; return; }
        var choice = await DisplayActionSheetAsync("Comments & bookmarks", "Cancel", null, items.Select(item => item.Label).ToArray());
        var index = Array.FindIndex(items, item => item.Label == choice);
        if (index < 0 || !ReferenceEquals(document, Editor.Document)) return;
        var action = await DisplayActionSheetAsync(items[index].Label, "Cancel", "Delete", "Go to text");
        if (document.Revision != snapshot.Revision) { StatusLabel.Text = "The document changed. Open Annotations again."; return; }
        if (action == "Delete") RunRich(() => Editor.Edit(edit => edit.RemoveAnnotation(items[index].Id), new("Delete annotation"), snapshot.Revision));
        else if (action == "Go to text")
        {
            if (items[index].Range is not { } range) { StatusLabel.Text = "The annotated text was deleted. The annotation is retained."; return; }
            Editor.Selection = range;
            Editor.Focus();
        }
    }

    private void OnTrackingChanged(object? sender, ToggledEventArgs args)
    {
        if (Editor is null || Peer is null || _restoringTracking) return;
        if (!Editor.TryCompleteComposition())
        {
            _restoringTracking = true;
            try { TrackingSwitch.IsToggled = !args.Value; }
            finally { _restoringTracking = false; }
            StatusLabel.Text = "Finish the current input before changing tracking.";
            return;
        }
        Editor.Session.RevisionTracking = args.Value ? new("You") : null;
        Peer.Session.RevisionTracking = args.Value ? new("You (second view)") : null;
        UpdateStatus();
    }

    private async void OnReview(object? sender, EventArgs args)
    {
        if (!Editor.TryCompleteComposition()) { StatusLabel.Text = "Finish the current input before reviewing."; return; }
        await Navigation.PushAsync(new RichReviewPage(Editor.Document));
    }
}
