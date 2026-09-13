using System.Windows.Input;

namespace RichEdit.Maui;

/// <summary>
/// Exposes history, selection, and clipboard commands for a <see cref="RichEditor"/>.
/// </summary>
public sealed class RichEditorCommands
{
    private readonly RichEditor _editor;
    private readonly Command[] _commands;
    private bool _clipboardSubscribed;

    internal RichEditorCommands(RichEditor editor)
    {
        _editor = editor;
        Undo = Create(editor.Undo, () => CanMutateSelection && editor.CanUndo);
        Redo = Create(editor.Redo, () => CanMutateSelection && editor.CanRedo);
        Cut = Create(async () => await editor.CutAsync(), () => CanMutateSelection && !editor.SelectedRange.IsEmpty);
        Copy = Create(async () => await editor.CopyAsync(), () => !editor.SelectedRange.IsEmpty);
        Paste = Create(
            async () => await editor.PasteAsync(),
            () => CanMutateSelection && RichTextClipboard.HasContent);
        SelectAll = Create(editor.SelectAll, () => editor.Document.Length != 0);

        _commands =
        [
            (Command)Undo,
            (Command)Redo,
            (Command)Cut,
            (Command)Copy,
            (Command)Paste,
            (Command)SelectAll,
        ];
    }

    /// <summary>Gets the undo command.</summary>
    public ICommand Undo { get; }

    /// <summary>Gets the redo command.</summary>
    public ICommand Redo { get; }

    /// <summary>Gets the cut command.</summary>
    public ICommand Cut { get; }

    /// <summary>Gets the copy command.</summary>
    public ICommand Copy { get; }

    /// <summary>Gets the paste command.</summary>
    public ICommand Paste { get; }

    /// <summary>Gets the select-all command.</summary>
    public ICommand SelectAll { get; }

    internal void Refresh()
    {
        if (_commands is null)
        {
            return;
        }

        foreach (var command in _commands)
        {
            command.ChangeCanExecute();
        }
    }

    internal void Connect()
    {
        if (_clipboardSubscribed)
        {
            return;
        }

        Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default
            .ClipboardContentChanged += OnClipboardContentChanged;
        _clipboardSubscribed = true;
        Refresh();
    }

    internal void Disconnect()
    {
        if (!_clipboardSubscribed)
        {
            return;
        }

        Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default
            .ClipboardContentChanged -= OnClipboardContentChanged;
        _clipboardSubscribed = false;
    }

    private void OnClipboardContentChanged(object? sender, EventArgs eventArgs) => Refresh();

    private bool CanMutateSelection => !_editor.IsReadOnly;

    private static Command Create(Action execute, Func<bool> canExecute) =>
        new(execute, canExecute);

    private static Command Create(Func<Task> execute, Func<bool> canExecute) =>
        new(async () => await ExecuteAsync(execute), canExecute);

    internal static Task ExecuteAsync(Func<Task> execute) => execute();
}
