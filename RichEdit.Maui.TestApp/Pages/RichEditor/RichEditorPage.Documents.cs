using System.Text;
using RichEdit.Core;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage
{
    private bool _referenceLoaded;
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_referenceLoaded) return;
        _referenceLoaded = true;
        await LoadBundledDocument("ReferenceDocument", "RTF reference");
    }
    private void OnNewDocument(object? sender, EventArgs args) => UseDocument(new RichDocument(), "Untitled.rtf");
    private async void OnLoadReference(object? sender, EventArgs args) => await LoadBundledDocument("ReferenceDocument", "RTF reference");
    private void OnToggleTheme(object? sender, EventArgs args) => StudioTheme.Toggle();

    private async Task LoadBundledDocument(string resource, string title)
    {
        if (!Editor.TryCompleteComposition()) { ResultLabel.Text = "Finish the current input before changing documents."; return; }
        var target = CaptureTarget();
        try
        {
            using var input = typeof(RichEditorPage).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The bundled document is unavailable.");
            var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
            if (!ReferenceEquals(target.Document, Editor.Document) || target.Revision != Editor.Document.Revision)
            { ResultLabel.Text = "The document changed while loading. Open the example again."; return; }
            UseDocument(new(imported.Snapshot), title);
            ResultLabel.Text = imported.Diagnostics.IsEmpty ? "Loaded " + title + "." : $"Loaded {title}. {imported.Diagnostics.Length} import notes: " +
                string.Join(" ", imported.Diagnostics.Take(2).Select(item => item.Message));
        }
        catch (Exception error) { ResultLabel.Text = "Could not load the document: " + error.Message; }
    }
    private void UseDocument(RichDocument document, string title)
    {
        if (!Editor.TryCompleteComposition()) { ResultLabel.Text = "Finish the current input before changing documents."; return; }
        Editor.Document = document;
        Peer.Document = document;
        TrackingSwitch.IsToggled = false;
        DocumentTitle.Text = title;
        UpdateStatus();
    }
    private async void OnCopyRtf(object? sender, EventArgs args)
    {
        if (!Editor.TryCompleteComposition()) { ResultLabel.Text = "Finish the current input before copying RTF."; return; }
        try
        {
            using var stream = new MemoryStream();
            await RichEdit.Core.Rtf.RtfCodec.WriteAsync(Editor.Document.CurrentSnapshot, stream);
            await Clipboard.Default.SetTextAsync(Encoding.ASCII.GetString(stream.GetBuffer(), 0, checked((int)stream.Length)));
            ResultLabel.Text = "Copied the document's RTF source.";
        }
        catch (Exception error) { ResultLabel.Text = error.Message; }
    }
}
