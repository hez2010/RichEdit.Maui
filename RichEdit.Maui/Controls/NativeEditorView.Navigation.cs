using TextEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    /// <summary>Moves every selection using native caret navigation. Supports Left, Right, Up, Down, Home, and End.</summary>
    /// <returns>False while composing or when native layout is unavailable.</returns>
    public bool MoveSelections(EditorKey direction, bool extend = false)
    {
        VerifyAccess();
        if (direction is not (EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down or EditorKey.Home or EditorKey.End))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (Composition.IsActive || Handler is not RichEditorHandler handler || !handler.HasTextLayout)
            return false;
        var moved = new List<TextSelection>();
        foreach (var selection in Selections)
        {
            if (handler.NavigateSelection(selection, direction, extend) is not { } result)
                return false;
            moved.Add(result);
        }
        Selections = new(moved, Selections.PrimaryIndex);
        ScrollIntoView(new(Selections.Primary.Active, 0));
        return true;
    }

    /// <summary>Adds a caret on the preceding native visual line, preserving the preferred horizontal coordinate.</summary>
    public bool AddCaretAbove() => AddAdjacentCaret(EditorKey.Up);

    /// <summary>Adds a caret on the following native visual line, preserving the preferred horizontal coordinate.</summary>
    public bool AddCaretBelow() => AddAdjacentCaret(EditorKey.Down);

    private bool AddAdjacentCaret(EditorKey direction)
    {
        VerifyAccess();
        if (Composition.IsActive || Handler is not RichEditorHandler handler || !handler.HasTextLayout)
            return false;
        var primary = Selections.Primary;
        if (handler.NavigateSelection(primary with { Anchor = primary.Active }, direction, false) is not { } next || next.Active == primary.Active)
            return false;
        var selections = Selections.ToList();
        var index = selections.FindIndex(item => item.Range.IsEmpty && item.Active == next.Active);
        if (index < 0)
        {
            index = selections.Count;
            selections.Add(next with { Id = Guid.NewGuid() });
        }
        else
            selections[index] = next with { Id = selections[index].Id };
        Selections = new(selections, index);
        ScrollIntoView(new(Selections.Primary.Active, 0));
        return true;
    }

    private bool HandleSelectionKey(EditorKey key, EditorKeyModifiers modifiers)
    {
        if (modifiers == EditorKeyModifiers.None && key is EditorKey.Backspace or EditorKey.Delete && DeleteStructuredCells()) return true;
        if (modifiers == (EditorKeyModifiers.Control | EditorKeyModifiers.Alt) && key is EditorKey.Up or EditorKey.Down)
            return key == EditorKey.Up ? AddCaretAbove() : AddCaretBelow();
        if (Selections.Count < 2)
            return false;
        if (key == EditorKey.Escape && modifiers == EditorKeyModifiers.None)
        {
            ClearSecondarySelections();
            return true;
        }
        return (modifiers & ~EditorKeyModifiers.Shift) == 0 &&
            key is EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down or EditorKey.Home or EditorKey.End &&
            MoveSelections(key, (modifiers & EditorKeyModifiers.Shift) != 0);
    }
}
