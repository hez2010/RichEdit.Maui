using System.Globalization;
using RichEdit.Core;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage
{
    private static readonly string[] ColorNames = ["Default / clear", "Coral", "Blue", "Green", "Yellow", "Black", "White"];
    private static StyleValue<RichColor?> ColorValue(string name) => name switch
    {
        "Coral" => new RichColor(224, 108, 117), "Blue" => new RichColor(50, 124, 203), "Green" => new RichColor(58, 143, 69),
        "Yellow" => new RichColor(255, 243, 163), "Black" => new RichColor(0, 0, 0), "White" => new RichColor(255, 255, 255),
        _ => StyleValue<RichColor?>.Initial,
    };
    private void Format(RichCharacterStyle format) => RunRich(() => Editor.ApplyCharacterFormat(format));
    private void Paragraph(RichParagraphStyle format) => RunRich(() => Editor.Edit(edit => edit.ApplyParagraphFormat(Editor.Selection, format), new("Paragraph formatting")));
    private void Script(RichVerticalPosition position) => Format(new() { VerticalPosition = Formats().All(x => x.VerticalPosition == position) ? RichVerticalPosition.Normal : position });
    private void ClearCharacterFormatting()
    {
        if (Editor.Document.CurrentSnapshot.GetSelectedText(Editor.Selection).All(range => range.Length == 0))
        {
            if (!Editor.IsReadOnly) Editor.TypingCharacterFormat = new();
        }
        else RunRich(() => Editor.Edit(edit => edit.UpdateCharacterFormat(Editor.Selection, _ => new()), new("Clear direct formatting")));
        UpdateFormattingTools();
    }

    private sealed record ToolTarget(RichDocument Document, TextEdit.Core.DocumentRevision Revision, long Generation, RichSelection Selection);
    private ToolTarget CaptureTarget() => new(Editor.Document, Editor.Document.Revision, Editor.Session.SelectionGeneration, Editor.Selection);
    private bool IsCurrent(ToolTarget target)
    {
        if (ReferenceEquals(target.Document, Editor.Document) && target.Revision == Editor.Document.Revision && target.Generation == Editor.Session.SelectionGeneration) return true;
        ResultLabel.Text = "The document or selection changed. Choose the action again.";
        return false;
    }

    private async void ShowCharacterOptions()
    {
        var target = CaptureTarget();
        var choice = await DisplayActionSheetAsync("Character options", "Cancel", null, "Small caps", "All caps", "Outline", "Shadow", "Hidden", "Show hidden text",
            "Double strike", "No strike", "Normal baseline", "Raise 2 pt", "Lower 2 pt", "Spacing 1 pt", "Normal spacing", "Width 120%", "Normal width", "Language", "Left to right", "Right to left");
        if (!IsCurrent(target)) return;
        var formats = Formats();
        RichCharacterStyle? format = choice switch
        {
            "Small caps" => new() { SmallCaps = !formats.All(x => x.SmallCaps) }, "All caps" => new() { AllCaps = !formats.All(x => x.AllCaps) },
            "Outline" => new() { Outline = !formats.All(x => x.Outline) }, "Shadow" => new() { Shadow = !formats.All(x => x.Shadow) },
            "Hidden" => new() { Hidden = true }, "Show hidden text" => new() { Hidden = false }, "Double strike" => new() { Strike = RichStrike.Double },
            "No strike" => new() { Strike = RichStrike.None }, "Normal baseline" => new() { VerticalPosition = RichVerticalPosition.Normal },
            "Raise 2 pt" => new() { VerticalPosition = RichVerticalPosition.Offset(4) }, "Lower 2 pt" => new() { VerticalPosition = RichVerticalPosition.Offset(-4) },
            "Spacing 1 pt" => new() { CharacterSpacing = RichLength.FromPoints(1) }, "Normal spacing" => new() { CharacterSpacing = default(RichLength) },
            "Width 120%" => new() { HorizontalScalePercent = 120 }, "Normal width" => new() { HorizontalScalePercent = 100 },
            "Left to right" => new() { Direction = RichDirection.LeftToRight }, "Right to left" => new() { Direction = RichDirection.RightToLeft }, _ => null,
        };
        if (choice == "Language")
        {
            var language = await DisplayPromptAsync("Text language", "Language tag, for example en-US or ja-JP", initialValue: "en-US");
            if (language is null || !IsCurrent(target)) return;
            format = new() { Language = string.IsNullOrWhiteSpace(language) ? StyleValue<string?>.Initial : language };
        }
        if (format is not null) Format(format);
    }

    private async void ShowParagraphOptions()
    {
        var target = CaptureTarget();
        var choice = await DisplayActionSheetAsync("Paragraph options", "Cancel", null, "Left to right", "Right to left", "Space before 12 pt", "Space after 12 pt",
            "No paragraph spacing", "Tab stops at 36 and 72 pt", "Clear tab stops", "Keep with next", "Keep lines together", "Allow line breaks", "Widow control", "Clear paragraph formatting");
        if (!IsCurrent(target)) return;
        if (choice == "Clear paragraph formatting") { RunRich(() => Editor.Edit(edit => edit.UpdateParagraphFormat(target.Selection, _ => new()))); return; }
        RichParagraphStyle? format = choice switch
        {
            "Left to right" => new() { Direction = RichDirection.LeftToRight }, "Right to left" => new() { Direction = RichDirection.RightToLeft },
            "Space before 12 pt" => new() { SpaceBefore = RichLength.FromPoints(12) }, "Space after 12 pt" => new() { SpaceAfter = RichLength.FromPoints(12) },
            "No paragraph spacing" => new() { SpaceBefore = default(RichLength), SpaceAfter = default(RichLength) },
            "Tab stops at 36 and 72 pt" => new() { TabStops = new RichTabStops((RichTabStop[])[new(RichLength.FromPoints(36)), new(RichLength.FromPoints(72))]) },
            "Clear tab stops" => new() { TabStops = RichTabStops.Empty }, "Keep with next" => new() { KeepWithNext = true },
            "Keep lines together" => new() { KeepTogether = true }, "Allow line breaks" => new() { KeepTogether = false, KeepWithNext = false },
            "Widow control" => new() { WidowControl = true }, _ => null,
        };
        if (format is not null) Paragraph(format);
    }

    private void ChangeIndent(double delta) => RunRich(() => Editor.Edit(edit =>
    {
        foreach (var paragraph in edit.Snapshot.GetSelectedText(Editor.Selection).Select(x => x.Start.Paragraph).Distinct())
        {
            var node = edit.Snapshot.Get<RichParagraph>(paragraph);
            var indent = Math.Max(0, edit.Snapshot.ResolveParagraph(paragraph).LeadingIndent.Points + delta);
            edit.SetParagraphFormat(paragraph, node.Format with { LeadingIndent = RichLength.FromPoints(indent) });
        }
    }, new(delta > 0 ? "Indent" : "Outdent")));

    private RichTextPoint? ActivePoint() => Editor.Selection switch
    {
        RichEdit.Core.RichTextSelection text => text.Active, RichTextSelectionSet set => set.Primary.Selection.Active,
        _ => Editor.Document.CurrentSnapshot.GetSelectedText(Editor.Selection).Select(range => (RichTextPoint?)range.Start).FirstOrDefault(),
    };
    private RichList? CurrentList()
    {
        if (ActivePoint() is not { } point) return null;
        RichNode node = Editor.Document.CurrentSnapshot.Get<RichParagraph>(point.Paragraph);
        while (node.Parent is { } parent)
        { node = Editor.Document.CurrentSnapshot.Get<RichNode>(parent); if (node is RichList list) return list; }
        return null;
    }
    private void ChangeNumbering(RichListNumberingStyle? style)
    {
        if (CurrentList() is not { } list) { ResultLabel.Text = "Place the caret in a list first."; return; }
        RunRich(() => Editor.Edit(edit => edit.SetListNumbering(list.Id, true, 1, style ?? list.NumberStyle), new("List numbering")));
    }
    private void AddNestedList()
    {
        if (ActivePoint() is not { } point || Editor.Document.CurrentSnapshot.Get<RichParagraph>(point.Paragraph).Parent is not { } parent ||
            Editor.Document.CurrentSnapshot.Get<RichNode>(parent) is not RichListItem item)
        { ResultLabel.Text = "Place the caret in a list item first."; return; }
        RunRich(() => Editor.Edit(edit =>
        {
            var paragraph = edit.InsertParagraph(item.Id, item.Blocks.Count, "Nested item");
            edit.WrapInList(item.Id, item.Blocks.Count, 1);
            edit.SetSelection(new RichEdit.Core.RichTextSelection(new(edit.Body, paragraph, 0), new(edit.Body, paragraph, 11)));
        }, new("Nested list")));
    }

    private void SetHyperlink(string? url)
    {
        if (url is not null && !Uri.TryCreate(url, UriKind.Absolute, out _)) { ResultLabel.Text = "Enter an absolute link address."; return; }
        var ranges = Editor.Document.CurrentSnapshot.GetSelectedText(Editor.Selection).Where(x => x.Length > 0).ToArray();
        if (ranges.Length == 0) { ResultLabel.Text = "Select the text to link or unlink."; return; }
        RunRich(() => Editor.Edit(edit => { foreach (var range in ranges) edit.SetHyperlink(range.Start, range.Length, url is null ? null : new(url)); }, new(url is null ? "Remove links" : "Add link")));
    }
    private void InsertDate() => InsertField("DATE \\@ \"yyyy-MM-dd\"", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    private void InsertField(string instruction, string result) => RunRich(() => Editor.ReplaceSelection(new RichDocumentFragment(RichDocumentSnapshot.Build(edit => edit.InsertField(new(edit.Body, new(2), 0), instruction, result)))));
    private async void InsertCustomField()
    {
        var target = CaptureTarget();
        var instruction = await DisplayPromptAsync("Field", "RTF field instruction (stored as data)", initialValue: "AUTHOR");
        if (string.IsNullOrWhiteSpace(instruction)) return;
        var result = await DisplayPromptAsync("Field", "Displayed result", initialValue: "Sample author");
        if (result is not null && IsCurrent(target)) InsertField(instruction, result);
    }
    private async void InsertSampleImage()
    {
        var target = CaptureTarget();
        try
        {
            await using var input = await FileSystem.OpenAppPackageFileAsync("sample-image.png");
            using var bytes = new MemoryStream();
            await input.CopyToAsync(bytes);
            if (!IsCurrent(target)) return;
            var fragment = new RichDocumentFragment(RichDocumentSnapshot.Build(edit =>
            {
                var asset = edit.AddImageAsset(RichImageKind.Png, bytes.ToArray());
                edit.InsertImage(new(edit.Body, new(2), 0), asset, RichLength.FromPoints(96), RichLength.FromPoints(96), ".NET sample robot");
            }));
            RunRich(() => Editor.ReplaceSelection(fragment));
        }
        catch (Exception error) { ResultLabel.Text = error.Message; }
    }
    private async void PastePlainText() { try { await Editor.PastePlainTextAsync(); } catch (Exception error) { ResultLabel.Text = error.Message; } }
    private void KeepPrimarySelection() { if (Editor.Selection is RichTextSelectionSet set) Editor.Selection = set.Primary.Selection; Editor.Focus(); }
    private void SelectParagraph()
    {
        if (ActivePoint() is not { } point) return;
        Editor.Selection = new RichEdit.Core.RichTextSelection(point with { Offset = 0 }, point with { Offset = Editor.Document.CurrentSnapshot.GetParagraphLength(point.Paragraph) });
        Editor.Focus();
    }
    private void SelectTable()
    {
        if (SelectedTable() is not { } table) { ResultLabel.Text = "Place the caret in a table first."; return; }
        var grid = Editor.Document.CurrentSnapshot.GetTableGrid(table);
        Editor.Selection = new RichCellSelection(table, 0, 0, grid.Rows, grid.Columns);
    }
    private void DeleteTable()
    {
        if (SelectedTable() is { } table) RunRich(() => Editor.Edit(edit => edit.DeleteBlock(table), new("Delete table")));
        else ResultLabel.Text = "Place the caret in a table first.";
    }
    private async void ShowCellAppearance()
    {
        if (Editor.Selection is not RichCellSelection cells) { ResultLabel.Text = "Select table cells first."; return; }
        var target = CaptureTarget();
        var choice = await DisplayActionSheetAsync("Cell appearance", "Cancel", null, "Top", "Center", "Bottom", "Padding 6 pt", "Blue shading", "Solid borders", "Dashed borders", "Double borders", "Clear appearance");
        if (choice is null or "Cancel" || !IsCurrent(target)) return;
        RunRich(() => Editor.Edit(edit =>
        {
            var grid = edit.Snapshot.GetTableGrid(cells.Table);
            var format = edit.Snapshot.Get<RichTableCell>(grid[cells.Row, cells.Column]).Format;
            format = choice switch
            {
                "Top" => format with { VerticalAlignment = RichCellVerticalAlignment.Top }, "Center" => format with { VerticalAlignment = RichCellVerticalAlignment.Center },
                "Bottom" => format with { VerticalAlignment = RichCellVerticalAlignment.Bottom }, "Padding 6 pt" => format with { Padding = RichCellPadding.Uniform(RichLength.FromPoints(6)) },
                "Blue shading" => format with { Shading = new(0, background: new(220, 235, 255)) },
                "Solid borders" or "Dashed borders" or "Double borders" => format with { Borders = RichCellBorders.Uniform(new(choice == "Dashed borders" ? RichBorderStyle.Dashed : choice == "Double borders" ? RichBorderStyle.Double : RichBorderStyle.Single, RichLength.FromPoints(1), new(23, 109, 101))) },
                _ => new(),
            };
            edit.SetCellFormat(cells, format);
        }, new("Cell appearance"), target.Revision, target.Generation));
    }
    private async void ShowCapabilities() => await DisplayAlertAsync("RTF support", "RTF is the rich-document format. Character and paragraph formats, lists, tables, images, fields, styles, comments, bookmarks, and retained change groups can be saved. Display fidelity varies by platform; import diagnostics identify unsupported content. Page layout, printing, and inline redline markup are outside this preview.", "OK");
}
