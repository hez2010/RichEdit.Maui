using RichEdit.Core;
using RichEdit.Core.Rtf;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage : ContentPage
{
    public RichEditorPage()
    {
        InitializeComponent();
        Editor.Document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            edit.SetStyles(new((RichStyleDefinition[])
            [
                new(new(1), "Body", RichStyleKind.Paragraph),
                new(new(2), "Heading", RichStyleKind.Paragraph)
                {
                    BasedOn = new(1), NextStyle = new(1),
                    Character = new() { Bold = true, FontSize = RichFontSize.FromPoints(22) },
                    Paragraph = new() { HeadingLevel = 1, SpaceAfter = RichLength.FromPoints(8) },
                },
            ]));
            edit.SetParagraphStyle(new(2), new(2));
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "A shared rich document");
            edit.InsertParagraph(edit.Body, 1, "Edit text, apply styles, and work with tables. Both views share content and undo history while keeping independent selections.", new(1));
            var table = edit.InsertTable(edit.Body, 2, 2, (RichLength[])[new(2400), new(2400)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(table);
            var labels = new[] { "Feature", "Document", "Clipboard", "RTF" };
            for (var row = 0; row < 2; row++)
                for (var column = 0; column < 2; column++)
                    edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0], 0), 0, labels[row * 2 + column]);
            edit.ApplyCharacterFormat(new RichCellSelection(table, 0, 0, 1, 2), new() { Bold = true });
            edit.InsertParagraph(edit.Body, 3, "Turn on Split view to try shared editing. Use Caret below to add another insertion point.", new(1));
        }));
        Peer.Document = Editor.Document;
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.ContentChanged += (_, _) => UpdateStatus();
        Editor.Pasting += (_, args) =>
        {
            if (!args.Diagnostics.IsEmpty) StatusLabel.Text = string.Join(" ", args.Diagnostics.Select(item => item.Message));
        };
        UpdateStatus();
    }

    private void OnBold(object? sender, EventArgs args) { Editor.ToggleBold(); Editor.Focus(); }
    private void OnItalic(object? sender, EventArgs args) { Editor.ToggleItalic(); Editor.Focus(); }
    private void OnUnderline(object? sender, EventArgs args) { Editor.ToggleUnderline(); Editor.Focus(); }
    private void OnAddCaret(object? sender, EventArgs args) { Editor.AddCaretBelow(); Editor.Focus(); }
    private void OnSplitChanged(object? sender, ToggledEventArgs args)
    {
        if (Peer is null) return;
        Peer.IsVisible = args.Value;
        EditorColumns.ColumnDefinitions[1].Width = args.Value ? GridLength.Star : new GridLength(0);
    }
    private void OnInsertTable(object? sender, EventArgs args)
    {
        Editor.ReplaceSelection(new RichDocumentFragment(RichDocumentSnapshot.Build(edit =>
        {
            edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(2400), new(2400)]);
            edit.DeleteBlock(new(2));
        })));
        Editor.Focus();
    }
    private RichNodeId? SelectedTable()
    {
        if (Editor.Selection is RichCellSelection cells) return cells.Table;
        var snapshot = Editor.Document.CurrentSnapshot;
        var paragraph = snapshot.GetSelectedText(Editor.Selection).FirstOrDefault().Start.Paragraph;
        if (!snapshot.Nodes.TryGetValue(paragraph, out var node)) return null;
        while (node.Parent is { } parent)
        {
            node = snapshot.Get<RichNode>(parent);
            if (node is RichTable) return node.Id;
        }
        return null;
    }
    private void OnSelectCells(object? sender, EventArgs args)
    {
        if (SelectedTable() is not { } table) { StatusLabel.Text = "Place the caret in a table first."; return; }
        var grid = Editor.Document.CurrentSnapshot.GetTableGrid(table);
        Editor.Selection = new RichCellSelection(table, 0, 0, 1, grid.Columns);
        Editor.Focus();
    }
    private void OnAddRow(object? sender, EventArgs args)
    {
        if (SelectedTable() is not { } table) { StatusLabel.Text = "Place the caret in a table first."; return; }
        Editor.Edit(edit => edit.InsertRows(table, edit.Snapshot.GetTableGrid(table).Rows), new("Insert row"));
    }
    private void OnMergeCells(object? sender, EventArgs args)
    {
        if (Editor.Selection is not RichCellSelection cells) { StatusLabel.Text = "Select table cells first."; return; }
        Editor.Edit(edit => edit.MergeCells(cells), new("Merge cells"));
    }
    private async void OnOpenRtf(object? sender, EventArgs args)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open RTF document",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".rtf" }, [DevicePlatform.Android] = new[] { "application/rtf", "text/rtf" },
                    [DevicePlatform.iOS] = new[] { "public.rtf" }, [DevicePlatform.MacCatalyst] = new[] { "public.rtf" },
                }),
            });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            var result = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(stream);
            Editor.Document = new RichDocument(result.Snapshot);
            Peer.Document = Editor.Document;
            UpdateStatus();
            if (!result.Diagnostics.IsEmpty) StatusLabel.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
        }
        catch (Exception error) { await DisplayAlertAsync("Open RTF", error.Message, "OK"); }
    }
    private async void OnSaveRtf(object? sender, EventArgs args)
    {
        try
        {
            if (!Editor.TryCompleteComposition()) { StatusLabel.Text = "Finish the current input before saving."; return; }
            var document = Editor.Document;
            var capture = document.CaptureSave();
            var path = Path.Combine(FileSystem.AppDataDirectory, "Document.rtf");
            var temporary = path + ".tmp";
            try
            {
                await using (var output = File.Create(temporary)) await RichEdit.Core.Rtf.RtfCodec.WriteAsync(capture.Snapshot, output);
                File.Move(temporary, path, overwrite: true);
                document.MarkSaved(capture);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            UpdateStatus();
            await Share.Default.RequestAsync(new ShareFileRequest("RTF document", new ShareFile(path, "application/rtf")));
        }
        catch (Exception error) { await DisplayAlertAsync("Save RTF", error.Message, "OK"); }
    }
    private void UpdateStatus()
    {
        var selection = Editor.Selection switch
        {
            RichTextSelectionSet set => $"{set.Count} text selections",
            RichCellSelection cells => $"{cells.RowCount} × {cells.ColumnCount} cells",
            RichBlockSelection => "Complete blocks selected",
            RichBoundarySelection => "Caret at a block boundary",
            RichObjectSelection => "Object selected",
            _ => "Text selection",
        };
        StatusLabel.Text = $"{selection} · {(Editor.IsModified ? "Modified" : "Saved")}";
    }
}
