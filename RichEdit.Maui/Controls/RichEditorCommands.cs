using System.Runtime.InteropServices;
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
            CanPaste);
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

    /// <summary>Reports an operational clipboard failure from a native event or command.</summary>
    /// <remarks>Direct calls to the editor's task-based clipboard methods still propagate exceptions.</remarks>
    public event EventHandler<RichTextCommandFailedEventArgs>? Failed;

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

    private bool CanPaste()
    {
        if (!CanMutateSelection)
            return false;

        try
        {
            return RichTextClipboard.HasContent;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            return false;
        }
    }

    private static Command Create(Action execute, Func<bool> canExecute) =>
        new(execute, canExecute);

    private Command Create(Func<Task> execute, Func<bool> canExecute) =>
        new(async () => await ExecuteClipboardAsync(execute), canExecute);

    internal async Task ExecuteClipboardAsync(Func<Task> execute)
    {
        try
        {
            await ExecuteAsync(execute);
        }
        catch (OperationCanceledException)
        {
            // Cancellation of a native clipboard request has no pending caller to notify.
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            System.Diagnostics.Trace.TraceError("Clipboard command failed: {0}", exception);
            Failed?.Invoke(this, new(exception));
        }
    }

    private static bool IsClipboardFailure(Exception exception) =>
        exception is COMException or IOException or UnauthorizedAccessException
#if ANDROID
        or global::Java.Lang.SecurityException or global::Java.IO.IOException
#endif
        ;

    internal static Task ExecuteAsync(Func<Task> execute) => execute();
}
