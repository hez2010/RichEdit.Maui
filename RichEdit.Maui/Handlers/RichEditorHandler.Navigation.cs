using TextEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    internal TextSelection? NavigateSelection(TextSelection selection, EditorKey direction, bool extend)
    {
        var display = NativeProjection.ToDisplay(new RichTextSelectionState(selection.Anchor, selection.Active));
        var preferred = direction is EditorKey.Up or EditorKey.Down ? selection.PreferredVisualX : null;
        // A native step can land in a private reservation. Continue through it without
        // exposing that display position as a document caret.
        for (var attempt = 0; attempt <= NativeProjection.Reservations.Length + 1; attempt++)
        {
            if (NavigateDisplaySelectionCore(display, direction, extend, preferred) is not { } result)
                return null;
            var source = NativeProjection.ToSource(result.Selection);
            if (source.Active != selection.Active || source.Anchor != selection.Anchor || result.Selection == display)
            {
                var position = source.Active;
                if (VirtualView.Folding.EffectiveRanges.FirstOrDefault(range => position >= range.Start && position < range.End) is { IsEmpty: false } hidden)
                    position = direction is EditorKey.Left or EditorKey.Up or EditorKey.Home ? hidden.Start : hidden.End;
                return selection with { Anchor = extend ? selection.Anchor : position, Active = position, PreferredVisualX = result.PreferredX };
            }
            display = result.Selection;
        }
        return selection;
    }

    private partial (RichTextSelectionState Selection, double? PreferredX)? NavigateDisplaySelectionCore(
        RichTextSelectionState selection, EditorKey direction, bool extend, double? preferredX);
}
