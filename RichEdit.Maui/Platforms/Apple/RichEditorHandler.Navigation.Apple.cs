#if IOS || MACCATALYST
using CoreGraphics;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial (TextSelectionState Selection, double? PreferredX)? NavigateDisplaySelectionCore(
        TextSelectionState selection, EditorKey direction, bool extend, double? preferredX, bool byWord)
    {
        var vertical = direction is EditorKey.Up or EditorKey.Down;
        var position = selection.Active;
        if (!extend && !selection.IsEmpty && direction is EditorKey.Left or EditorKey.Right)
        {
            using var start = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, selection.Range.Start);
            using var end = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, selection.Range.End);
            using var range = PlatformView.GetDisplayRange(start!, end!);
            using var edge = PlatformView.GetDisplayFarthestPosition(range!, direction == EditorKey.Left ? UITextLayoutDirection.Left : UITextLayoutDirection.Right);
            if (edge is not null)
                position = checked((int)PlatformView.GetDisplayOffset(PlatformView.BeginningOfDocument, edge));
            var first = PlatformView.GetDisplayCaret(start!);
            var last = PlatformView.GetDisplayCaret(end!);
            if (Math.Abs(first.Y - last.Y) < 0.5)
                position = (direction == EditorKey.Left ? first.X <= last.X : first.X >= last.X) ? selection.Range.Start : selection.Range.End;
        }
        else
        {
            using var current = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, position);
            if (current is null)
                return null;
            if (direction is EditorKey.Home or EditorKey.End)
            {
                var text = DisplaySourceSnapshot.TextBuffer;
                if (text.Length != 0 && !(position == text.Length && text[^1] == '\n'))
                {
                    var manager = PlatformView.LayoutManager;
                    var glyph = manager.GetGlyphIndex((nuint)Math.Min(position, text.Length - 1));
                    manager.GetLineFragmentUsedRect(glyph, out var glyphs, true);
                    var characters = manager.GetCharacterRange(glyphs);
                    var start = (int)characters.Location;
                    var end = Math.Min(text.Length, (int)(characters.Location + characters.Length));
                    if (end > start && text[end - 1] is '\n' or '\u2028')
                        end--;
                    position = direction == EditorKey.Home ? start : end;
                }
            }
            else
            {
                var nativeDirection = direction switch
                {
                    EditorKey.Left => UITextLayoutDirection.Left,
                    EditorKey.Right => UITextLayoutDirection.Right,
                    EditorKey.Up => UITextLayoutDirection.Up,
                    _ => UITextLayoutDirection.Down,
                };
                using var next = byWord
                    ? PlatformView.GetDisplayWordBoundary(current, nativeDirection)
                    : PlatformView.GetDisplayPosition(current, nativeDirection, 1);
                if (next is not null)
                    position = checked((int)PlatformView.GetDisplayOffset(PlatformView.BeginningOfDocument, next));
            }
        }
        var x = vertical ? preferredX ?? GetTextCaretBoundsCore(selection.Active, RichTextCaretAffinity.Downstream)?.X : null;
        if (vertical && x is { } targetX && position != selection.Active &&
            GetTextCaretBoundsCore(position, RichTextCaretAffinity.Downstream) is { } target)
        {
            using var hit = PlatformView.GetDisplayClosestPosition(new CGPoint(targetX + PlatformView.ContentOffset.X,
                target.Y + target.Height / 2 + PlatformView.ContentOffset.Y));
            if (hit is not null)
                position = checked((int)PlatformView.GetDisplayOffset(PlatformView.BeginningOfDocument, hit));
        }
        return (new(extend ? selection.Anchor : position, position), x);
    }
}
#endif
