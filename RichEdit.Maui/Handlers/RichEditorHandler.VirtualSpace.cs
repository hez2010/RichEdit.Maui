#if IOS || MACCATALYST
using UIKit;
#endif

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        internal bool HasNativeVirtualSelection => _virtualSpace is not null &&
            NativeProjection.Reservations.Any(item => ReferenceEquals(item.Owner, _virtualSpace.Owner));

        internal Rect? GetVirtualCaretBounds(TextSelection selection)
        {
            if (!HasTextLayout || _virtualSpace is null || selection.Id != VirtualView.Selections.Primary.Id ||
                selection.Active != _virtualSpace.Position || NativeProjection.Reservations.FirstOrDefault(
                    item => ReferenceEquals(item.Owner, _virtualSpace.Owner)) is not { } padding) return null;
            return GetTextCaretBoundsCore(padding.DisplayStart + selection.ActiveVirtualSpaces, RichTextCaretAffinity.Downstream);
        }

        internal double SourceSpaceAdvance
        {
            get
            {
#if WINDOWS
                return Math.Max(0.1, PlatformView.Document.DefaultTabStop * 4.0 / 3 / VirtualView.SourceTabSize);
#elif ANDROID
                return Math.Max(0.1, PlatformView.Paint!.MeasureText(" ") / LayoutDensity);
#else
                using var space = new Foundation.NSString(" ");
                return Math.Max(0.1, space.GetSizeUsingAttributes(new UIKit.UIStringAttributes { Font = ResolveFont(RichTextCharacterFormat.Default) }).Width);
#endif
            }
        }

        internal double VirtualSpaceAdvance(int position)
        {
            var sign = VirtualView.FlowDirection == FlowDirection.RightToLeft ? -1 : 1;
#if WINDOWS
            var source = VirtualView.Document.CurrentSnapshot.TextBuffer;
            var start = source.GetLine(source.GetPosition(position).Line).Span.Start;
            if (start != position && GetTextCaretBounds(start, RichTextCaretAffinity.Downstream) is { } first &&
                GetTextCaretBounds(position, RichTextCaretAffinity.Upstream) is { } last && Math.Abs(last.X - first.X) > 0.1)
                sign = last.X > first.X ? 1 : -1;
#elif ANDROID
            if (PlatformView.Layout is { } layout)
                sign = (int)layout.GetParagraphDirection(layout.GetLineForOffset(NativeProjection.ToDisplayCaret(position, false)));
#elif IOS || MACCATALYST
            using var native = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, NativeProjection.ToDisplayCaret(position, false));
            if (native is not null) sign = PlatformView.GetBaseWritingDirection(native, UIKit.UITextStorageDirection.Forward) == Foundation.NSWritingDirection.RightToLeft ? -1 : 1;
#endif
            return sign * SourceSpaceAdvance;
        }

        private void UpdateVirtualPrimaryCaret(bool hidden)
        {
#if WINDOWS
            var type = hidden ? Microsoft.UI.Text.CaretType.Null : Microsoft.UI.Text.CaretType.Normal;
            // Setting CaretType also reveals the native caret. Layout/scroll
            // observation must not repeat that mutation when its state is unchanged.
            if (PlatformView.Document.CaretType != type) PlatformView.Document.CaretType = type;
#elif ANDROID
            PlatformView.SetCursorVisible(!hidden && !VirtualView.IsReadOnly);
#else
            PlatformView.HideVirtualCaret = hidden;
#endif
        }
    }
}

#if IOS || MACCATALYST
namespace RichEdit.Maui.Platforms.Apple
{
    public partial class RichTextView
    {
        internal bool HideVirtualCaret { get; set; }
        /// <inheritdoc />
        public override CoreGraphics.CGRect GetCaretRectForPosition(UIKit.UITextPosition? position) =>
            HideVirtualCaret && _displayInputDepth == 0 ? CoreGraphics.CGRect.Empty : base.GetCaretRectForPosition(position);
    }
}
#endif
