#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        internal NSAttributedString SourceAccessibilityText()
        {
            var storage = PlatformView.TextStorage;
            var map = ReadDisplayReservationMetadata(storage.Value ?? "");
            var value = new NSMutableAttributedString(storage);
            foreach (var item in map.Reservations.Reverse()) value.DeleteRange(new(item.DisplayStart, item.Text.Length));
            return value;
        }
    }
}

namespace RichEdit.Maui.Platforms.Apple
{
    public partial class RichTextView : IUIAccessibilityReadingContent
    {
        /// <inheritdoc />
        public override string? AccessibilityValue
        {
            get => ProjectionHandler?.VirtualView.Document.Text ?? base.AccessibilityValue;
            set => base.AccessibilityValue = value;
        }
        /// <inheritdoc />
        public override NSAttributedString? AccessibilityAttributedValue
        {
            get => ProjectionHandler?.SourceAccessibilityText() ?? base.AccessibilityAttributedValue;
            set => base.AccessibilityAttributedValue = value;
        }
        /// <inheritdoc />
        public nint GetAccessibilityLineNumber(CGPoint point)
        {
            if (ProjectionHandler is not { VirtualView: var editor } || editor.TextLayout.Capture() is not { } layout) return -1;
            var local = ConvertPointFromView(point, null);
            if (editor.TextLayout.HitTest(layout, new(local.X, local.Y)) is not { } hit) return -1;
            return editor.Document.CurrentSnapshot.Paragraphs.TakeWhile(paragraph => paragraph.Start <= hit.Position).Count() - 1;
        }
        private RichTextRange? AccessibilityLine(nint line)
        {
            if (ProjectionHandler is not { VirtualView.Document.CurrentSnapshot: var source } || line < 0 || line >= source.Paragraphs.Length) return null;
            var start = source.Paragraphs[(int)line].Start;
            var end = line + 1 < source.Paragraphs.Length ? source.Paragraphs[(int)line + 1].Start : source.Length;
            return new(start, end - start);
        }
        /// <inheritdoc />
        public string GetAccessibilityContent(nint lineNumber) => AccessibilityLine(lineNumber) is { } line ?
            ProjectionHandler!.VirtualView.Document.Text.Substring(line.Start, line.Length) : "";
        /// <inheritdoc />
        public CGRect GetAccessibilityFrame(nint lineNumber)
        {
            if (AccessibilityLine(lineNumber) is not { } line || ProjectionHandler!.VirtualView.TextLayout.Capture() is not { } layout) return CGRect.Empty;
            var rectangles = ProjectionHandler.VirtualView.TextLayout.GetRangeBounds(layout, line);
            if (rectangles.Count == 0) return CGRect.Empty;
            var bounds = rectangles.Aggregate(static (first, next) => first.Union(next));
            return UIAccessibility.ConvertFrameToScreenCoordinates(new(bounds.X, bounds.Y, bounds.Width, bounds.Height), this);
        }
        /// <inheritdoc />
        public string GetAccessibilityPageContent() => ProjectionHandler?.VirtualView.Document.Text ?? "";
    }
}
#endif
