using RichEdit.Core;
using RichEdit.Core.Rtf;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage : ContentPage
{
    public RichEditorPage()
    {
        InitializeComponent();
        EditorColumns.SizeChanged += (_, _) => SplitPaneLayout.Update(EditorColumns, PeerPane);
        Editor.Document = new RichDocument();
        Peer.Document = Editor.Document;
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.ContentChanged += (_, _) => UpdateStatus();
        Editor.Pasting += (_, args) =>
        {
            if (!args.Diagnostics.IsEmpty) StatusLabel.Text = string.Join(" ", args.Diagnostics.Select(item => item.Message));
        };
        InitializeTools();
        UpdateStatus();
    }

    private static RichDocument CreateFeatureDocument()
    {
        return new RichDocument(RichDocumentSnapshot.Build(edit =>
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
            var notes = edit.InsertParagraph(edit.Body, 4, "Reusable styles change every matching paragraph.", new(1));
            edit.InsertParagraph(edit.Body, 5, "Comments and bookmarks stay with the document when you save RTF.", new(1));
            edit.WrapInList(edit.Body, 4, 2, numbered: true);
            var noteRange = new RichEdit.Core.RichTextSelection(new(edit.Body, notes, 0), new(edit.Body, notes, 15));
            edit.AddComment(noteRange, "Try Styles & lists → Restyle headings, then Undo.", "Sample");
            edit.AddBookmark("StyleExample", noteRange);
        }));
    }

    private void OnBold(object? sender, EventArgs args) => RunRich(Editor.ToggleBold);
    private void OnItalic(object? sender, EventArgs args) => RunRich(Editor.ToggleItalic);
    private void OnUnderline(object? sender, EventArgs args) => RunRich(Editor.ToggleUnderline);
    private void OnAddCaret(object? sender, EventArgs args) { Editor.AddCaretBelow(); Editor.Focus(); }
    private void OnSplitChanged(object? sender, ToggledEventArgs args)
    {
        if (Peer is null) return;
        PeerPane.IsVisible = args.Value;
        SplitPaneLayout.Update(EditorColumns, PeerPane);
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
            UseDocument(new RichDocument(result.Snapshot), file.FileName);
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
        var snapshot = Editor.Document.CurrentSnapshot;
        StatusLabel.Text = $"{selection} · {(Editor.IsModified ? "Modified" : "Saved")} · Comments: {snapshot.Comments.Count} · Changes: {snapshot.Revisions.Values.Select(item => item.Info.Group).Distinct().Count()}";
    }
}
