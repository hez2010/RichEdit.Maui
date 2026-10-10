using Microsoft.UI.Text;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial (RichTextSelectionState Selection, double? PreferredX)? NavigateDisplaySelectionCore(
        RichTextSelectionState selection, EditorKey direction, bool extend, double? preferredX)
    {
        var native = PlatformView.Document.Selection;
        var start = native.StartPosition;
        var end = native.EndPosition;
        var options = native.Options;
        var applying = _applyingDocument;
        _applyingDocument = true;
        PlatformView.Document.BatchDisplayUpdates();
        try
        {
            SetNativeSelectionCore(selection);
            var vertical = direction is EditorKey.Up or EditorKey.Down;
            var x = vertical ? preferredX ?? GetTextCaretBoundsCore(selection.Active, RichTextCaretAffinity.Downstream)?.X : null;
            switch (direction)
            {
                case EditorKey.Left: native.MoveLeft(TextRangeUnit.Character, 1, extend); break;
                case EditorKey.Right: native.MoveRight(TextRangeUnit.Character, 1, extend); break;
                case EditorKey.Up: native.MoveUp(TextRangeUnit.Line, 1, extend); break;
                case EditorKey.Down: native.MoveDown(TextRangeUnit.Line, 1, extend); break;
                case EditorKey.Home: native.HomeKey(TextRangeUnit.Line, extend); break;
                case EditorKey.End: native.EndKey(TextRangeUnit.Line, extend); break;
            }
            var active = DisplayPositionFromNative((native.Options & SelectionOptions.StartActive) != 0 ? native.StartPosition : native.EndPosition);
            if (vertical && x is { } targetX && active != selection.Active &&
                GetTextCaretBoundsCore(active, RichTextCaretAffinity.Downstream) is { } target)
            {
                var origin = TextLayoutOrigin();
                var targetLine = PlatformView.Document.GetRange(NativePositionFromDisplay(active), NativePositionFromDisplay(active));
                targetLine.Expand(TextRangeUnit.Line);
                var lineStart = DisplayPositionFromNative(targetLine.StartPosition);
                var lineEnd = DisplayPositionFromNative(targetLine.EndPosition);
                var text = DisplaySourceSnapshot.Text;
                if (lineEnd > lineStart && text[lineEnd - 1] is '\n' or '\u2028')
                    lineEnd--;
                var hit = PlatformView.Document.GetRangeFromPoint(new(targetX - origin.X, target.Y + target.Height / 2 - origin.Y), PointOptions.ClientCoordinates);
                active = Math.Clamp(DisplayPositionFromNative(hit.StartPosition), lineStart, lineEnd);
            }
            return (new(extend ? selection.Anchor : active, active), x);
        }
        finally
        {
            native.SetRange(start, end);
            native.Options = options;
            PlatformView.Document.ApplyDisplayUpdates();
            _applyingDocument = applying;
        }
    }
}
