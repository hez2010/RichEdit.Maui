#if IOS || MACCATALYST
using System.Collections.Immutable;
using System.Globalization;
using CoreGraphics;
using Foundation;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial RichTextNativeLayout? CaptureTextLayoutCore()
    {
        if (PlatformView.Window is null) return null;
        var viewport = GetAdornmentViewport();
        var inset = PlatformView.TextContainerInset;
        var padding = PlatformView.TextContainer.LineFragmentPadding;
        var offset = PlatformView.ContentOffset;
        var textViewport = new Rect(inset.Left + padding, inset.Top, Math.Max(0, viewport.Width - inset.Left - inset.Right - 2 * padding),
            Math.Max(0, viewport.Height - inset.Top - inset.Bottom));
        var manager = PlatformView.LayoutManager;
        NativeGeometryQueryCount++;
        var visible = manager.GetGlyphRangeForBoundingRectWithoutAdditionalLayout(
            new CGRect(offset.X - inset.Left, offset.Y - inset.Top, viewport.Width, viewport.Height), PlatformView.TextContainer);
        var lines = new List<RichTextVisualLine>();
        var limit = checked((nuint)(visible.Location + visible.Length));
        for (var glyph = checked((nuint)visible.Location); glyph < limit;)
        {
            NativeGeometryQueryCount += 2;
            var rect = manager.GetLineFragmentUsedRect(glyph, out var glyphs, true);
            var chars = manager.GetCharacterRange(glyphs);
            var ranges = VisibleSourceRanges(checked((int)chars.Location), checked((int)(chars.Location + chars.Length)));
            var bounds = new Rect(rect.X + inset.Left - offset.X, rect.Y + inset.Top - offset.Y, Math.Max(1, rect.Width), rect.Height);
            if (!ranges.IsEmpty && bounds.Bottom > textViewport.Top && bounds.Top < textViewport.Bottom)
            {
                NativeGeometryQueryCount++;
                lines.Add(new(ranges, bounds, rect.Y + manager.GetLocationForGlyph(glyph).Y + inset.Top - offset.Y));
            }
            var next = checked((nuint)(glyphs.Location + glyphs.Length));
            if (next <= glyph) break;
            glyph = next;
        }
        if (GetTextCaretBoundsCore(DisplaySourceSnapshot.Length, RichTextCaretAffinity.Downstream) is { } end &&
            end.IntersectsWith(textViewport) && (lines.Count == 0 || Math.Abs(lines[^1].Bounds.Y - end.Y) > 0.5))
        {
            var storage = PlatformView.TextStorage;
            var attributes = storage.Length == 0 || PlatformView.SelectedRange.Location == storage.Length && PlatformView.SelectedRange.Length == 0
                ? PlatformView.TypingAttributes2 : storage.GetAttributes(storage.Length - 1, out _);
            var font = new UIStringAttributes(attributes).Font ?? PlatformView.Font ?? UIFont.SystemFontOfSize(UIFont.SystemFontSize);
            lines.Add(new(ImmutableArray.Create(new RichTextRange(VirtualView.Document.Length, 0)), end, end.Y + font.Ascender));
        }
        return new(viewport, textViewport, lines.ToImmutableArray());
    }

    private partial Rect? GetTextCaretBoundsCore(int position, RichTextCaretAffinity affinity)
    {
        using var native = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, position);
        if (native is null) return null;
        NativeGeometryQueryCount++;
        var rect = PlatformView.GetDisplayCaret(native);
        var text = DisplaySourceSnapshot.Text;
        if (position == text.Length && (position == 0 || text[^1] == '\n'))
        {
            // Catalyst can return its empty-view caret for an unselected trailing
            // paragraph. TextKit's extra fragment retains that paragraph's layout.
            NativeGeometryQueryCount++;
            var extra = PlatformView.LayoutManager.ExtraLineFragmentUsedRect;
            if (extra.Height > 0)
                rect = new(extra.X + PlatformView.TextContainerInset.Left + PlatformView.TextContainer.LineFragmentPadding,
                    extra.Y + PlatformView.TextContainerInset.Top, Math.Max(1, rect.Width), extra.Height);
        }
        if (affinity == RichTextCaretAffinity.Upstream && position > 0)
        {
            var start = position - 1;
            if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
            using var previous = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, start);
            using var range = PlatformView.GetDisplayRange(previous!, native);
            NativeGeometryQueryCount++;
            var rectangles = PlatformView.GetDisplaySelectionRects(range!);
            try
            {
                var last = rectangles.LastOrDefault(static item => item.ContainsEnd) ?? rectangles.LastOrDefault();
                if (last is not null && text[position - 1] is not ('\n' or '\u2028'))
                    rect = new(last.WritingDirection == NSWritingDirection.RightToLeft ? last.Rect.Left : last.Rect.Right, last.Rect.Y, rect.Width, last.Rect.Height);
            }
            finally { foreach (var item in rectangles) item.Dispose(); }
        }
        return new(rect.X - PlatformView.ContentOffset.X, rect.Y - PlatformView.ContentOffset.Y, Math.Max(1, rect.Width), rect.Height);
    }

    private partial IReadOnlyList<Rect> GetTextRangeBoundsCore(RichTextRange range)
    {
        using var start = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, range.Start);
        using var end = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, range.End);
        if (start is null || end is null) return Array.Empty<Rect>();
        using var nativeRange = PlatformView.GetDisplayRange(start, end);
        NativeGeometryQueryCount++;
        var rectangles = PlatformView.GetDisplaySelectionRects(nativeRange!);
        var result = new List<Rect>();
        try
        {
            foreach (var item in rectangles) AddFragment(result, new(item.Rect.X - PlatformView.ContentOffset.X, item.Rect.Y - PlatformView.ContentOffset.Y, item.Rect.Width, item.Rect.Height));
        }
        finally { foreach (var item in rectangles) item.Dispose(); }
        return result;
    }

    private partial RichTextHit? HitTestTextCore(Point point)
    {
        var local = new CGPoint(point.X + PlatformView.ContentOffset.X, point.Y + PlatformView.ContentOffset.Y);
        NativeGeometryQueryCount += 2;
        using var position = PlatformView.GetDisplayClosestPosition(local);
        if (position is null) return null;
        var offset = checked((int)PlatformView.GetDisplayOffset(PlatformView.BeginningOfDocument, position));
        using var character = PlatformView.GetDisplayCharacterRange(local);
        var isText = false;
        if (character is not null)
        {
            NativeGeometryQueryCount++;
            foreach (var item in PlatformView.GetDisplaySelectionRects(character))
                using (item) isText |= item.Rect.Contains(local);
        }
        return SourceHit(offset, RichTextCaretAffinity.Downstream, isText);
    }
}
#endif
