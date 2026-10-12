#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using RichEdit.Core;
using CoreSelection = RichEdit.Core.RichTextSelection;
using static RichEdit.Maui.Tests.EditingScenarioContext;

namespace RichEdit.Maui.Tests;

internal static partial class ReferenceEditingScenarios
{
    private static IEnumerable<EditingScenario> ContentCases()
    {
        yield return new("rich/inline/hyperlink", ["links", "selection", "scroll", "undo", "RTF"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point, 4); var before = await context.SaveRich();
            await context.ObserveEdit("add link", () => context.Rich.Edit(edit => edit.SetHyperlink(point, 4, new("https://example.test/reference", "Reference link"))), sameSelection: true);
            Check(context.Rich.Document.CurrentSnapshot.Get<RichParagraph>(point.Paragraph).Inlines.Select(context.Rich.Document.CurrentSnapshot.Get<RichInline>).Any(item => item.Link?.Target == "https://example.test/reference"), "link target retained");
            await context.CheckUndo(before, () => Check(context.Rich.Document.CurrentSnapshot.Nodes.Values.OfType<RichInline>().Any(item => item.Link?.ToolTip == "Reference link"), "link tooltip retained"));
        });
        yield return new("rich/inline/field", ["fields", "typing", "caret", "scroll", "undo", "atomic objects"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point); var before = await context.SaveRich(); RichNodeId field = default;
            await context.ObserveEdit("insert field", () => context.Rich.Edit(edit =>
            {
                field = edit.InsertField(point, "AUTHOR", "Sample author");
                var caret = point with { Offset = point.Offset + 1 }; edit.SetSelection(new CoreSelection(caret, caret));
            }));
            Equal("Sample author", context.Rich.Document.CurrentSnapshot.Get<RichField>(field).Result, "field result retained");
            await context.ObserveEdit("type after field", () => context.Type("!"));
            context.Rich.Undo(); await Settle(80);
            await context.CheckUndo(before, () => Check(context.Rich.Document.CurrentSnapshot.Nodes.ContainsKey(field), "field redo"));
        });
        yield return new("rich/inline/image", ["images", "resources", "object selection", "undo", "RTF"], async context =>
        {
            await context.OpenReference();
            var snapshot = context.Rich.Document.CurrentSnapshot;
            var image = snapshot.Nodes.Values.OfType<RichImage>().First();
            var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point); var before = await context.SaveRich(); RichNodeId inserted = default;
            await context.ObserveEdit("insert image", () => context.Rich.Edit(edit =>
            {
                inserted = edit.InsertImage(point, image.Asset, new(480), new(480), "Reference image copy");
                var caret = point with { Offset = point.Offset + 1 }; edit.SetSelection(new CoreSelection(caret, caret));
            }));
            Equal(image.Asset, context.Rich.Document.CurrentSnapshot.Get<RichImage>(inserted).Asset, "image reuses its document resource");
            await context.CheckUndo(before, () => Equal("Reference image copy", context.Rich.Document.CurrentSnapshot.Get<RichImage>(inserted).AlternativeText, "image alt text"));
        });
        yield return new("rich/fragment/replace", ["fragments", "resources", "selection", "undo", "RTF"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            var snapshot = context.Rich.Document.CurrentSnapshot;
            var fragment = RichDocumentFragment.FromSelection(snapshot, new CoreSelection(point, point with { Offset = point.Offset + 4 }));
            await context.Select(point); var before = await context.SaveRich(); var original = context.Text;
            var start = snapshot.ProjectText().GetOffset(point);
            await context.ObserveEdit("paste fragment", () => context.Rich.ReplaceSelection(fragment));
            Equal(original.Insert(start, original.Substring(start, 4)), context.Text, "fragment inserted at caret");
            await context.CheckUndo(before, () => Equal(original.Length + 4, context.Text.Length, "fragment redo"));
        });
        yield return new("rich/annotations/comments-bookmarks", ["comments", "bookmarks", "tracked addresses", "selection", "scroll", "undo", "RTF"], async context =>
        {
            await context.OpenReference(); var point = context.Find("The text you are reading is aligned left", 4);
            await context.Select(point, 4); var before = await context.SaveRich();
            await context.ObserveEdit("annotate selection", () => context.Rich.Edit(edit =>
            {
                edit.AddBookmark("scenario_bookmark", new(point, point with { Offset = point.Offset + 4 }));
                edit.AddComment(new(point, point with { Offset = point.Offset + 4 }), "Comment on selected text", "Scenario");
            }), sameSelection: true);
            Check(context.Rich.Document.CurrentSnapshot.Comments.Values.Any(item => item.Author == "Scenario"), "comment retained");
            await context.CheckUndo(before, () => Check(context.Rich.Document.CurrentSnapshot.Bookmarks.Values.Any(item => item.Name == "scenario_bookmark"), "bookmark restored"));
        });
        foreach (var operation in new[] { "row", "column", "width", "cell-format", "merge-split" })
            yield return new("rich/table/" + operation, ["tables", "caret", "selection", "scroll", "undo", "RTF"], async context =>
            {
                await context.OpenReference(); var snapshot = context.Rich.Document.CurrentSnapshot;
                var table = snapshot.Nodes.Values.OfType<RichTable>().First();
                var grid = snapshot.GetTableGrid(table.Id);
                var cell = snapshot.Get<RichTableCell>(grid[0, 0]); var paragraph = cell.Blocks[0];
                var point = new RichTextPoint(snapshot.Body, paragraph, Math.Min(1, snapshot.GetParagraphLength(paragraph)));
                await context.Select(point); var before = await context.SaveRich();
                await context.ObserveEdit(operation, () => context.Rich.Edit(edit =>
                {
                    switch (operation)
                    {
                        case "row": edit.InsertRows(table.Id, 1, 1); break;
                        case "column": edit.InsertColumns(table.Id, 1, new RichLength[] { new(1200) }); break;
                        case "width": edit.SetColumnWidths(table.Id, table.Columns.SetItem(0, new(table.Columns[0].Twips + 240))); break;
                        case "cell-format": edit.SetCellFormat(new(table.Id, 0, 0, 1, 1), new() { Padding = new(new(80), new(80), new(80), new(80)), Shading = new(10000, new(230, 240, 250)) }); break;
                        case "merge-split": edit.MergeCells(new(table.Id, 0, 0, 1, 2)); break;
                    }
                }));
                Check(context.Rich.Document.CurrentSnapshot.Nodes.ContainsKey(paragraph), "original caret paragraph survives");
                await context.CheckUndo(before, () => Check(context.Rich.Document.CurrentSnapshot.Nodes.ContainsKey(table.Id), "table identity retained after redo"));
                if (operation == "merge-split") { context.Rich.Edit(edit => edit.SplitCell(cell.Id)); await Settle(100); }
            });
        yield return new("rich/table/native-cell-input", ["tables", "typing", "caret", "scroll", "undo"], async context =>
        {
            await context.OpenReference(); var snapshot = context.Rich.Document.CurrentSnapshot;
            var table = snapshot.Nodes.Values.OfType<RichTable>().First();
            var paragraph = snapshot.Get<RichTableCell>(snapshot.GetTableGrid(table.Id)[0, 1]).Blocks[0];
            var point = new RichTextPoint(snapshot.Body, paragraph, Math.Min(1, snapshot.GetParagraphLength(paragraph)));
            await context.Select(point); var before = await context.SaveRich();
            await context.ObserveEdit("cell typing", () => context.Type("cell"));
            await context.CheckUndo(before, () => Check(context.Rich.Document.CurrentSnapshot.ProjectText(paragraph).Text.ToString().Contains("cell", StringComparison.Ordinal), "cell input restored"));
        });
    }
}
#endif
