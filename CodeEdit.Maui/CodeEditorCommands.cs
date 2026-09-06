using System.Windows.Input;

namespace CodeEdit.Maui;

/// <summary>Provides editor-aware MVVM commands for source editing and the native clipboard.</summary>
public sealed class CodeEditorCommands
{
    private readonly Command[] _editingCommands;

    internal CodeEditorCommands(CodeEditor editor)
    {
        var indent = new Command(editor.Indent, () => !editor.IsReadOnly);
        var outdent = new Command(editor.Outdent, () => !editor.IsReadOnly);
        var newLine = new Command(editor.InsertNewLine, () => !editor.IsReadOnly);
        var comment = new Command(editor.ToggleLineComment, () => !editor.IsReadOnly);
        _editingCommands = [indent, outdent, newLine, comment];
        Indent = indent;
        Outdent = outdent;
        InsertNewLine = newLine;
        ToggleLineComment = comment;
        Undo = editor.TextView.Commands.Undo;
        Redo = editor.TextView.Commands.Redo;
        SelectAll = editor.TextView.Commands.SelectAll;
        Copy = editor.TextView.Commands.Copy;
        Cut = editor.TextView.Commands.Cut;
        Paste = editor.TextView.Commands.Paste;
    }

    /// <summary>Gets the indentation command.</summary>
    public ICommand Indent { get; }
    /// <summary>Gets the outdent command.</summary>
    public ICommand Outdent { get; }
    /// <summary>Gets the newline and auto-indent command.</summary>
    public ICommand InsertNewLine { get; }
    /// <summary>Gets the line-comment toggle command.</summary>
    public ICommand ToggleLineComment { get; }
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

    internal void Refresh()
    {
        foreach (var command in _editingCommands) command.ChangeCanExecute();
    }
}
