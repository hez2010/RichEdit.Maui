using System.Windows.Input;

namespace CodeEdit.Maui;

/// <summary>Provides editor-aware MVVM commands for history, selection, and the native clipboard.</summary>
public sealed class CodeEditorCommands
{
    internal CodeEditorCommands(CodeEditor editor)
    {
        Undo = editor.TextView.Commands.Undo;
        Redo = editor.TextView.Commands.Redo;
        SelectAll = editor.TextView.Commands.SelectAll;
        Copy = editor.TextView.Commands.Copy;
        Cut = editor.TextView.Commands.Cut;
        Paste = editor.TextView.Commands.Paste;
    }

    /// <summary>Gets the undo command.</summary>
    public ICommand Undo { get; }
    /// <summary>Gets the redo command.</summary>
    public ICommand Redo { get; }
    /// <summary>Gets the select-all command.</summary>
    public ICommand SelectAll { get; }
    /// <summary>Gets the copy command.</summary>
    public ICommand Copy { get; }
    /// <summary>Gets the cut command.</summary>
    public ICommand Cut { get; }
    /// <summary>Gets the plain-text paste command.</summary>
    public ICommand Paste { get; }
}
