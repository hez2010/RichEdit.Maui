using RichEdit.Maui;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    /// <summary>Moves every selection with native caret navigation. Supports Left, Right, Up, Down, Home, and End.</summary>
    public bool MoveSelections(EditorKey direction, bool extend = false) => TextView.MoveSelections(direction, extend);

    /// <summary>Adds a caret on the preceding native visual line.</summary>
    public bool AddCaretAbove() => TextView.AddCaretAbove();

    /// <summary>Adds a caret on the following native visual line.</summary>
    public bool AddCaretBelow() => TextView.AddCaretBelow();
}
