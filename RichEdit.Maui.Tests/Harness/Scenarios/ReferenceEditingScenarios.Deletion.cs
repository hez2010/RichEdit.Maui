#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using RichEdit.Core;
using CoreSelection = RichEdit.Core.RichTextSelection;
using static RichEdit.Maui.Tests.EditingScenarioContext;

namespace RichEdit.Maui.Tests;

internal static partial class ReferenceEditingScenarios
{
    private static IEnumerable<EditingScenario> DeletionCases()
    {
        foreach (var reversed in new[] { false, true })
        {
            foreach (var paragraphsBeforeTable in new[] { 0, 1, 2 })
                yield return new($"rich/deletion/before-table-{paragraphsBeforeTable}-{reversed}", ["native deletion", "tables", "formatting", "selection", "caret", "undo", "redo"], async context =>
                {
                    await context.OpenReference();
                    var snapshot = context.Rich.Document.CurrentSnapshot;
                    var body = snapshot.Get<RichStory>(snapshot.Body);
                    var preceding = body.Blocks.TakeWhile(id => snapshot.Get<RichBlock>(id) is not RichTable).ToArray();
                    var first = new RichTextPoint(snapshot.Body, preceding[0], 0);
                    var last = preceding[^(paragraphsBeforeTable + 1)];
                    var end = new RichTextPoint(snapshot.Body, last, snapshot.GetParagraphLength(last));
                    await SelectDeletion(context, reversed ? end : first, reversed ? first : end);
                    await DeleteAndRestore(context, () => Check(!context.Text.StartsWith("It is an example", StringComparison.Ordinal), "selected introduction removed"));
                });

            yield return new($"rich/deletion/mixed-styles-{reversed}", ["native deletion", "formatting", "selection", "caret", "undo", "redo"], async context =>
            {
                await context.OpenReference();
                var start = context.Find("Font size 10");
                var end = context.Find("Bold text.", "Bold text.".Length);
                await SelectDeletion(context, reversed ? end : start, reversed ? start : end);
                await DeleteAndRestore(context, () => Check(!context.Text.Contains("Font size 10", StringComparison.Ordinal), "selected styled paragraphs removed"));
            });

            foreach (var numbered in new[] { false, true })
                foreach (var partial in new[] { false, true })
                    foreach (var selectionSet in new[] { false, true })
                        yield return new($"rich/deletion/list-{numbered}-{partial}-{reversed}-{selectionSet}", ["native deletion", "lists", "selection", "caret", "undo", "redo"], async context =>
                        {
                            await context.OpenReference();
                            var snapshot = context.Rich.Document.CurrentSnapshot;
                            var paragraphs = snapshot.ProjectText().Paragraphs.Select(paragraph => paragraph.Paragraph).Where(id =>
                                snapshot.Get<RichParagraph>(id).Parent is { } parent && snapshot.Get<RichNode>(parent) is RichListItem item &&
                                snapshot.Get<RichList>(item.Parent!.Value).Numbered == numbered).Take(2).ToArray();
                            Equal(2, paragraphs.Length, "reference contains two list items");
                            var start = new RichTextPoint(snapshot.Body, paragraphs[0], partial ? 2 : 0);
                            var end = new RichTextPoint(snapshot.Body, paragraphs[1], partial ? 3 : snapshot.GetParagraphLength(paragraphs[1]));
                            var selected = snapshot.GetSelectedText(new CoreSelection(start, end));
                            var removed = string.Join("\n", selected.Select(range => snapshot.ProjectText(range.Start.Paragraph).Text.GetText(new(range.Start.Offset, range.Length))));
                            await SelectDeletion(context, reversed ? end : start, reversed ? start : end, selectionSet);
                            await DeleteAndRestore(context, () => Check(!context.Text.Contains(removed, StringComparison.Ordinal), "selected list text removed"));
                        });
        }

        foreach (var entireTable in new[] { false, true })
            yield return new($"rich/deletion/table-{(entireTable ? "whole" : "rectangle")}", ["native deletion", "tables", "cell selection", "focus", "undo", "redo"], async context =>
            {
                await context.OpenReference();
                var snapshot = context.Rich.Document.CurrentSnapshot;
                var table = snapshot.Nodes.Values.OfType<RichTable>().First();
                var grid = snapshot.GetTableGrid(table.Id);
                var selection = entireTable ? new RichCellSelection(table.Id, 0, 0, grid.Rows, grid.Columns) : new(table.Id, 1, 1, 2, 2);
                context.Rich.Selection = selection;
                context.Rich.Focus();
                await Settle();
                Equal<RichSelection>(selection, context.Rich.Selection, "cell selection survives focus");
                await DeleteAndRestore(context, () =>
                {
                    var current = context.Rich.Document.CurrentSnapshot;
                    if (entireTable) Check(!current.Nodes.ContainsKey(table.Id), "complete table removed");
                    else
                    {
                        Check(current.Nodes.ContainsKey(table.Id), "partial deletion retains table");
                        for (var row = 0; row < grid.Rows; row++)
                            for (var column = 0; column < grid.Columns; column++)
                            {
                                var cell = grid[row, column];
                                var expected = row is 1 or 2 && column is 1 or 2 ? "" : snapshot.ProjectText(cell).Text.ToString();
                                Equal(expected, current.ProjectText(cell).Text.ToString(), $"cell {row},{column}");
                            }
                    }
                });
            });
    }

    private static async Task SelectDeletion(EditingScenarioContext context, RichTextPoint anchor, RichTextPoint active, bool selectionSet = false)
    {
        var text = new CoreSelection(anchor, active);
        context.Rich.Selection = selectionSet ? new RichTextSelectionSet(context.Rich.Document.CurrentSnapshot, new[] { new RichTextSelectionItem(text) }) : text;
        context.Rich.Focus();
        context.Rich.TextLayout.ScrollIntoView(active);
        await Settle();
        context.AssertNativeSelection();
    }

    private static async Task DeleteAndRestore(EditingScenarioContext context, Action verifyDeleted)
    {
        var before = await context.SaveRich();
        var selection = context.Rich.Selection;
        var nativeBefore = context.RichNativeTexts();
        await context.ObserveEdit("native Backspace", context.DeleteSelection, stableScroll: false);
        verifyDeleted();
        var deleted = await context.SaveRich();
        Check(!before.AsSpan().SequenceEqual(deleted), "native deletion changes document");
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await context.ObserveEdit("Undo", context.Rich.Undo, stableScroll: false);
            var restored = await context.SaveRich();
            Check(before.AsSpan().SequenceEqual(restored), "Undo restores the reference RTF including formatting and structure");
            Equal(selection, context.Rich.Selection, "Undo restores selection");
            Check(nativeBefore.SequenceEqual(context.RichNativeTexts()), "Undo restores native text and table boundaries");
            if (cycle == 0)
            {
                await context.ObserveEdit("Redo", context.Rich.Redo, stableScroll: false);
                verifyDeleted();
                var redone = await context.SaveRich();
                Check(deleted.AsSpan().SequenceEqual(redone), "Redo restores the deletion");
            }
        }
    }
}
#endif
