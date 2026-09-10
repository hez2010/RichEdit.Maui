#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        private RichTextDisplayProjection? _inputProjection;
        internal RichTextDisplayProjection InputProjection => _applyingDocument || _updatingDisplayProjection ? NativeProjection :
            _inputProjection ??= ReadDisplayReservationMetadata(PlatformView.TextStorage.Value ?? "");
    }
}

namespace RichEdit.Maui.Platforms.Apple
{
    public partial class RichTextView
    {
        private int _displayInputDepth;
        private RichTextDisplayProjection? SourceInputProjection => _displayInputDepth == 0 && ProjectionHandler is { NativeProjection.IsEmpty: false } handler ? handler.InputProjection : null;
        private readonly struct DisplayInputScope : IDisposable
        {
            private readonly RichTextView _view;
            internal DisplayInputScope(RichTextView view) { _view = view; view._displayInputDepth++; }
            public void Dispose() => _view._displayInputDepth--;
        }

        internal nint GetDisplayOffset(UITextPosition from, UITextPosition to)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetOffsetFromPosition(from, to);
        }
        internal UITextPosition? GetDisplayPosition(UITextPosition from, nint offset)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetPosition(from, offset);
        }
        internal UITextPosition? GetDisplayPosition(UITextPosition from, UITextLayoutDirection direction, nint offset)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetPosition(from, direction, offset);
        }
        internal UITextRange? GetDisplayRange(UITextPosition from, UITextPosition to)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetTextRange(from, to);
        }
        internal CGRect GetDisplayCaret(UITextPosition position)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetCaretRectForPosition(position);
        }
        internal UITextSelectionRect[] GetDisplaySelectionRects(UITextRange range)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetSelectionRects(range);
        }
        internal UITextPosition? GetDisplayClosestPosition(CGPoint point)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetClosestPositionToPoint(point);
        }
        internal UITextRange? GetDisplayCharacterRange(CGPoint point)
        {
            using var scope = new DisplayInputScope(this);
            return base.GetCharacterRangeAtPoint(point);
        }

        /// <inheritdoc />
        public override string TextInRange(UITextRange range)
        {
            var map = SourceInputProjection;
            string? text;
            using (new DisplayInputScope(this)) text = base.TextInRange(range);
            return text is null ? "" : map is null ? text : map.SourceText(text, (int)GetDisplayOffset(BeginningOfDocument, range.Start));
        }
        /// <inheritdoc />
        public override nint GetOffsetFromPosition(UITextPosition from, UITextPosition to) => SourceInputProjection is not { } map ?
            base.GetOffsetFromPosition(from, to) : map.ToSource((int)GetDisplayOffset(BeginningOfDocument, to)) - map.ToSource((int)GetDisplayOffset(BeginningOfDocument, from));
        /// <inheritdoc />
        public override UITextPosition GetPosition(UITextPosition from, nint offset)
        {
            if (SourceInputProjection is not { } map || offset == 0) return base.GetPosition(from, offset);
            var position = (long)map.ToSource((int)GetDisplayOffset(BeginningOfDocument, from)) + offset;
            if (position < 0 || position > map.ToSource((int)TextStorage.Length)) return null!;
            return GetDisplayPosition(BeginningOfDocument, map.ToDisplayCaret((int)position, offset > 0))!;
        }
        /// <inheritdoc />
        public override UITextPosition GetPosition(UITextRange range, nint offset)
        {
            if (SourceInputProjection is null) return base.GetPosition(range, offset);
            return offset < 0 || offset > GetOffsetFromPosition(range.Start, range.End) ? null! : GetPosition(range.Start, offset);
        }
        /// <inheritdoc />
        public override nint GetCharacterOffsetOfPosition(UITextPosition position, UITextRange range) => SourceInputProjection is null ?
            base.GetCharacterOffsetOfPosition(position, range) : GetOffsetFromPosition(range.Start, position);
        /// <inheritdoc />
        public override NSComparisonResult ComparePosition(UITextPosition first, UITextPosition second) => SourceInputProjection is null ?
            base.ComparePosition(first, second) : (NSComparisonResult)Math.Sign((long)GetOffsetFromPosition(second, first));
        /// <inheritdoc />
        public override UITextPosition GetPosition(UITextPosition from, UITextLayoutDirection direction, nint offset)
        {
            if (SourceInputProjection is not { } map || offset == 0) return base.GetPosition(from, direction, offset);
            var current = from;
            for (long step = 0; step < Math.Abs((long)offset); step++)
            {
                var source = map.ToSource((int)GetDisplayOffset(BeginningOfDocument, current));
                UITextPosition? next;
                do
                {
                    next = GetDisplayPosition(current, direction, Math.Sign((long)offset));
                    if (next is null || GetDisplayOffset(current, next) == 0)
                    {
                        if (!ReferenceEquals(current, from)) current.Dispose();
                        if (next is not null && !ReferenceEquals(next, current) && !ReferenceEquals(next, from)) next.Dispose();
                        return null!;
                    }
                    if (!ReferenceEquals(current, from)) current.Dispose();
                    current = next;
                } while (map.ToSource((int)GetDisplayOffset(BeginningOfDocument, current)) == source ||
                    direction is UITextLayoutDirection.Up or UITextLayoutDirection.Down && ProjectionHandler!.IsDisplayReservationLine((int)GetDisplayOffset(BeginningOfDocument, current)));
            }
            return current;
        }

        private UITextPosition NormalizeSourcePosition(UITextPosition position)
        {
            if (SourceInputProjection is not { } map) return position;
            var offset = (int)GetDisplayOffset(BeginningOfDocument, position);
            var target = map.ToDisplayCaret(map.ToSource(offset));
            return offset == target ? position : GetDisplayPosition(BeginningOfDocument, target)!;
        }

        /// <inheritdoc />
        public override UITextPosition GetClosestPositionToPoint(CGPoint point) => NormalizeSourcePosition(GetDisplayClosestPosition(point)!);
        /// <inheritdoc />
        public override UITextPosition GetClosestPositionToPoint(CGPoint point, UITextRange range)
        {
            UITextPosition position;
            using (new DisplayInputScope(this)) position = base.GetClosestPositionToPoint(point, range);
            return NormalizeSourcePosition(position);
        }
        /// <inheritdoc />
        public override UITextPosition GetPositionWithinRange(UITextRange range, UITextLayoutDirection direction)
        {
            UITextPosition position;
            using (new DisplayInputScope(this)) position = base.GetPositionWithinRange(range, direction);
            return NormalizeSourcePosition(position);
        }
        /// <inheritdoc />
        public override UITextRange GetCharacterRange(UITextPosition position, UITextLayoutDirection direction)
        {
            if (SourceInputProjection is null) return base.GetCharacterRange(position, direction);
            using var next = GetPosition(position, direction, 1);
            return next is null ? null! : GetDisplayOffset(position, next) >= 0 ? GetDisplayRange(position, next)! : GetDisplayRange(next, position)!;
        }
        /// <inheritdoc />
        public override UITextRange GetCharacterRangeAtPoint(CGPoint point)
        {
            if (SourceInputProjection is not { } map) return base.GetCharacterRangeAtPoint(point);
            using var range = GetDisplayCharacterRange(point);
            if (range is null) return null!;
            var start = map.ToSource((int)GetDisplayOffset(BeginningOfDocument, range.Start));
            var end = map.ToSource((int)GetDisplayOffset(BeginningOfDocument, range.End));
            var display = map.ToDisplay(new RichTextRange(start, Math.Max(0, end - start)));
            using var first = GetDisplayPosition(BeginningOfDocument, display.Start);
            using var last = GetDisplayPosition(BeginningOfDocument, display.End);
            return GetDisplayRange(first!, last!)!;
        }
    }
}
#endif
