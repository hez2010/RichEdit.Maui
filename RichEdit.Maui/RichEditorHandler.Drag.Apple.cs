#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private SourceTextDragDelegate? _sourceDragDelegate;
    private sealed class SourceTextDragDelegate(RichEditorHandler handler) : UITextDragDelegate
    {
        public override UIDragItem[] GetItemsForDrag(IUITextDraggable textDraggable, IUITextDragRequest request)
        {
            if (handler.NativeProjection.IsEmpty) return request.SuggestedItems;
            var view = handler.PlatformView;
            var start = handler.NativeProjection.ToSource((int)view.GetDisplayOffset(view.BeginningOfDocument, request.DragRange.Start));
            var end = handler.NativeProjection.ToSource((int)view.GetDisplayOffset(view.BeginningOfDocument, request.DragRange.End));
            var fragment = RichTextDocumentFragment.FromRange(handler.VirtualView.Document.CurrentSnapshot, new(start, end - start));
            if (fragment.Text.Length == 0) return [];
            using var text = new NSString(fragment.Text);
            var provider = new NSItemProvider(text, "public.utf8-plain-text");
            provider.RegisterDataRepresentation("public.rtf", NSItemProviderRepresentationVisibility.All, completion =>
            {
                completion(NSData.FromString(fragment.RtfText, NSStringEncoding.UTF8), null);
                var progress = NSProgress.FromTotalUnitCount(1);
                progress.CompletedUnitCount = 1;
                return progress;
            });
            return [new UIDragItem(provider)];
        }
    }
}
#endif
