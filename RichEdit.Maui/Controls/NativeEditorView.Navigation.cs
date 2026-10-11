using TextEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    /// <summary>Moves every selection using native caret navigation. Supports Left, Right, Up, Down, Home, and End.</summary>
    /// <returns>False while composing or when native layout is unavailable.</returns>
    public bool MoveSelections(EditorKey direction, bool extend = false, bool byWord = false)
    {
        VerifyAccess();
        if (direction is not (EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down or EditorKey.Home or EditorKey.End))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (byWord && direction is not (EditorKey.Left or EditorKey.Right))
            throw new ArgumentException("Word navigation requires Left or Right.", nameof(direction));
        if (StructuredSession is not null && _structuredSurfaces.Count != 0) return MoveStructuredSelections(direction, extend, byWord);
        if (Composition.IsActive || Handler is not RichEditorHandler handler || !handler.HasTextLayout)
            return false;
        var moved = new List<TextSelection>();
        foreach (var selection in Selections)
        {
            if (handler.NavigateSelection(selection, direction, extend, byWord) is not { } result)
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
        if (handler.NavigateSelection(primary with { Anchor = primary.Active, AnchorVirtualSpaces = primary.ActiveVirtualSpaces }, direction, false) is not { } next ||
            next.Active == primary.Active && next.ActiveVirtualSpaces == primary.ActiveVirtualSpaces)
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
        if (key == EditorKey.Tab && modifiers is EditorKeyModifiers.None or EditorKeyModifiers.Shift &&
            MoveToAdjacentCell(modifiers == EditorKeyModifiers.None, appendRow: true)) return true;
        if (key == EditorKey.Enter && modifiers == EditorKeyModifiers.Shift && StructuredSession is { } rich &&
            ApplyStructuredNativeInput(rich.Selection, "\u2028")) return true;
        if (modifiers == EditorKeyModifiers.None && key is EditorKey.Enter or EditorKey.Backspace && TryHandleStructuredListKey(key)) return true;
        if (SourceSession is not null && modifiers == EditorKeyModifiers.None && key is EditorKey.Backspace or EditorKey.Delete &&
            Selections.Any(item => item.AnchorVirtualSpaces != 0 || item.ActiveVirtualSpaces != 0))
            return DeleteSourceVirtualSelections(key == EditorKey.Backspace);
        if (modifiers == EditorKeyModifiers.None && key is EditorKey.Backspace or EditorKey.Delete && DeleteStructuredCells()) return true;
        if (modifiers == EditorKeyModifiers.None && key is EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down or EditorKey.Home or EditorKey.End &&
            StructuredSession?.Selection is RichEdit.Core.RichCellSelection or RichEdit.Core.RichBlockSelection)
        {
            var ranges = StructuredSession.Document.CurrentSnapshot.GetSelectedText(StructuredSession.Selection);
            var backward = key is EditorKey.Left or EditorKey.Up or EditorKey.Home;
            var selected = backward ? ranges[0] : ranges[^1];
            var point = selected.Start with { Offset = selected.Start.Offset + (backward ? 0 : selected.Length) };
            StructuredSession.Selection = new RichEdit.Core.RichTextSelection(point, point);
            FocusStructuredSelection();
            return true;
        }
        if (modifiers == (EditorKeyModifiers.Control | EditorKeyModifiers.Alt) && key is EditorKey.Up or EditorKey.Down)
            return key == EditorKey.Up ? AddCaretAbove() : AddCaretBelow();
        if (Selections.Count < 2 && !SourceVirtualSpaceEnabled && Selections.Primary.ActiveVirtualSpaces == 0 &&
            (StructuredSession is null || _structuredSurfaces.Count == 0))
            return false;
        var wordModifier = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? EditorKeyModifiers.Alt : EditorKeyModifiers.Control;
        if ((modifiers & ~EditorKeyModifiers.Shift) == wordModifier && key is EditorKey.Left or EditorKey.Right)
            return MoveSelections(key, (modifiers & EditorKeyModifiers.Shift) != 0, byWord: true);
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
