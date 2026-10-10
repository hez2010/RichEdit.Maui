namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial (TextSelectionState Selection, double? PreferredX)? NavigateDisplaySelectionCore(
        TextSelectionState selection, EditorKey direction, bool extend, double? preferredX)
    {
        if (PlatformView.Layout is not { } layout || PlatformView.EditableText is not { } text)
            return null;
        var oldStart = PlatformView.SelectionStart;
        var oldEnd = PlatformView.SelectionEnd;
        var applying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            global::Android.Text.Selection.SetSelection(text, selection.Anchor, selection.Active);
            var vertical = direction is EditorKey.Up or EditorKey.Down;
            var x = vertical ? preferredX ?? GetTextCaretBoundsCore(selection.Active, RichTextCaretAffinity.Downstream)?.X : null;
            if (direction == EditorKey.Left)
            {
                if (extend) global::Android.Text.Selection.ExtendLeft(text, layout);
                else global::Android.Text.Selection.MoveLeft(text, layout);
            }
            else if (direction == EditorKey.Right)
            {
                if (extend) global::Android.Text.Selection.ExtendRight(text, layout);
                else global::Android.Text.Selection.MoveRight(text, layout);
            }
            else if (vertical)
            {
                var line = layout.GetLineForOffset(selection.Active);
                var next = Math.Clamp(line + (direction == EditorKey.Up ? -1 : 1), 0, layout.LineCount - 1);
                var offset = next == line ? direction == EditorKey.Up ? 0 : DisplaySourceSnapshot.Length :
                    layout.GetOffsetForHorizontal(next, (float)((x ?? 0) * LayoutDensity - TextLayoutOrigin().X));
                global::Android.Text.Selection.SetSelection(text, extend ? selection.Anchor : offset, offset);
            }
            else
            {
                var line = layout.GetLineForOffset(selection.Active);
                var offset = direction == EditorKey.Home ? layout.GetLineStart(line) : layout.GetLineEnd(line);
                if (direction == EditorKey.End && offset > layout.GetLineStart(line) && DisplaySourceSnapshot.Text[offset - 1] is '\n' or '\u2028')
                    offset--;
                global::Android.Text.Selection.SetSelection(text, extend ? selection.Anchor : offset, offset);
            }
            return (new(PlatformView.SelectionStart, PlatformView.SelectionEnd), x);
        }
        finally
        {
            global::Android.Text.Selection.SetSelection(text, oldStart, oldEnd);
            _applyingDocument = applying;
        }
    }
}
