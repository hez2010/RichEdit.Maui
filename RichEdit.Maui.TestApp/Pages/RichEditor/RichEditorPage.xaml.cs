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
