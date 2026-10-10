using TextEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    internal TextSelection? NavigateSelection(TextSelection selection, EditorKey direction, bool extend, bool byWord = false)
    {
        var text = VirtualView.Document.CurrentSnapshot.TextBuffer;
        if (VirtualView.SourceSession is not null && !byWord && direction is EditorKey.Left or EditorKey.Right &&
            (selection.ActiveVirtualSpaces != 0 || VirtualView.SourceVirtualSpaceEnabled &&
                text.GetLine(text.GetPosition(selection.Active).Line).Span.End == selection.Active))
        {
            var forward = (direction == EditorKey.Right) == (VirtualSpaceAdvance(selection.Active) > 0);
            if (forward || selection.ActiveVirtualSpaces != 0)
            {
                var spaces = Math.Max(0, checked(selection.ActiveVirtualSpaces + (forward ? 1 : -1)));
                if (!extend && !selection.IsEmpty) spaces = forward ? selection.EndVirtualSpaces : selection.StartVirtualSpaces;
                return selection with { Anchor = extend ? selection.Anchor : selection.Active, AnchorVirtualSpaces = extend ? selection.AnchorVirtualSpaces : spaces,
                    ActiveVirtualSpaces = spaces, PreferredVisualX = null };
            }
        }
        var display = NativeProjection.ToDisplay(new TextSelectionState(selection.Anchor, selection.Active));
        var preferred = direction is EditorKey.Up or EditorKey.Down ? selection.PreferredVisualX : null;
        if (direction is EditorKey.Up or EditorKey.Down && preferred is null && selection.ActiveVirtualSpaces != 0 &&
            GetTextCaretBounds(selection.Active, RichTextCaretAffinity.Upstream) is { } caret)
            preferred = caret.X + selection.ActiveVirtualSpaces * VirtualSpaceAdvance(selection.Active);
        // A native step can land in a private reservation. Continue through it without
        // exposing that display position as a document caret.
        for (var attempt = 0; attempt <= NativeProjection.Reservations.Length + 1; attempt++)
        {
            if (NavigateDisplaySelectionCore(display, direction, extend, preferred, byWord) is not { } result)
                return null;
            var source = NativeProjection.ToSource(result.Selection);
            if (source.Active != selection.Active || source.Anchor != selection.Anchor || result.Selection == display)
            {
                var position = source.Active;
                if (VirtualView.Folding.EffectiveRanges.FirstOrDefault(range => position >= range.Start && position < range.End) is { IsEmpty: false } hidden)
                    position = direction is EditorKey.Left or EditorKey.Up or EditorKey.Home ? hidden.Start : hidden.End;
                var virtualSpaces = 0;
                if (VirtualView.SourceSession is not null && VirtualView.SourceVirtualSpaceEnabled && direction is EditorKey.Up or EditorKey.Down &&
                    text.GetLine(text.GetPosition(position).Line).Span.End == position && result.PreferredX is { } x &&
                    GetTextCaretBounds(position, RichTextCaretAffinity.Upstream) is { } end)
                    virtualSpaces = Math.Max(0, checked((int)Math.Round((x - end.X) / VirtualSpaceAdvance(position))));
                return selection with { Anchor = extend ? selection.Anchor : position, Active = position, PreferredVisualX = result.PreferredX,
                    AnchorVirtualSpaces = extend ? selection.AnchorVirtualSpaces : virtualSpaces, ActiveVirtualSpaces = virtualSpaces };
            }
            display = result.Selection;
        }
        return selection;
    }

    private partial (TextSelectionState Selection, double? PreferredX)? NavigateDisplaySelectionCore(
        TextSelectionState selection, EditorKey direction, bool extend, double? preferredX, bool byWord);
}
