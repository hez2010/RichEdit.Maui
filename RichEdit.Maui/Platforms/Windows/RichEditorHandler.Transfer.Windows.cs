using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using DragEventArgs = Microsoft.UI.Xaml.DragEventArgs;
using DragStartingEventArgs = Microsoft.UI.Xaml.DragStartingEventArgs;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private async void OnSourceDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (!VirtualView.HasClipboardSelection || VirtualView.Composition.IsActive)
        {
            args.Cancel = true;
            return;
        }
        var deferral = args.GetDeferral();
        var supplied = false;
        try
        {
            await VirtualView.Commands.ExecuteClipboardAsync(async () =>
            {
                if (await VirtualView.CaptureTransferAsync() is not { } content) return;
                
                args.Data.SetText(content.Text);
                if (content.Rtf is { } rtf) args.Data.SetRtf(rtf);
                args.AllowedOperations = DataPackageOperation.Copy;
                supplied = true;
            });
        }
        finally { args.Cancel = !supplied; deferral.Complete(); }
    }

    private void OnNativeDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        args.AcceptedOperation = !VirtualView.IsReadOnly && !VirtualView.Document.HasActiveComposition &&
            (args.DataView.Contains(StandardDataFormats.Text) || VirtualView.SourceSession is null && args.DataView.Contains(StandardDataFormats.Rtf))
            ? DataPackageOperation.Copy : DataPackageOperation.None;
    }

    private async void OnNativeDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        args.AcceptedOperation = DataPackageOperation.None;
        if (VirtualView.IsReadOnly || VirtualView.Document.HasActiveComposition ||
            !args.DataView.Contains(StandardDataFormats.Text) && (VirtualView.SourceSession is not null || !args.DataView.Contains(StandardDataFormats.Rtf))) return;
        var point = args.GetPosition(PlatformView);
        if (HitTestText(new(point.X, point.Y)) is not { } hit) return;
        var deferral = args.GetDeferral();
        try
        {
            var editor = VirtualView;
            var plain = editor.SourceSession is not null;
            await editor.Commands.ExecuteClipboardAsync(async () =>
            {
                var accepted = await editor.InsertTransferAsync(hit.Position, async () =>
                {
                    var text = args.DataView.Contains(StandardDataFormats.Text) ? await args.DataView.GetTextAsync() : "";
                    var rtf = !plain && args.DataView.Contains(StandardDataFormats.Rtf) ? await args.DataView.GetRtfAsync() : null;
                    return new(text, rtf);
                });
                if (accepted) args.AcceptedOperation = DataPackageOperation.Copy;
            });
        }
        finally { deferral.Complete(); }
    }
}
