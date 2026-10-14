using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RichEdit.Maui;

/// <summary>
/// Exposes history, selection, and clipboard commands for a <see cref="NativeEditorView"/>.
/// </summary>
public sealed class RichEditorCommands
{
    private readonly NativeEditorView _editor;
    private readonly Command[] _commands;
    private bool _clipboardSubscribed;

    internal RichEditorCommands(NativeEditorView editor)
    {
        _editor = editor;
        Undo = Create(editor.Undo, () => CanMutateSelection && editor.CanUndo);
        Redo = Create(editor.Redo, () => CanMutateSelection && editor.CanRedo);
        Cut = Create(async () => await editor.CutAsync(), () => CanMutateSelection && editor.HasClipboardSelection);
        Copy = Create(async () => await editor.CopyAsync(), () => editor.HasClipboardSelection);
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

    /// <summary>Reports a native command failure so the application can offer recovery.</summary>
    /// <remarks>Direct editing API calls still propagate exceptions. Cancellation is not reported as a failure.</remarks>
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

    private bool CanMutateSelection => !_editor.IsReadOnly && !_editor.Document.HasActiveComposition;

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

    private Command Create(Action execute, Func<bool> canExecute) =>
        new(() => TryExecute(execute), () => Execute(canExecute, false));

    private Command Create(Func<Task> execute, Func<bool> canExecute) =>
        new(async () => await ExecuteClipboardAsync(execute), () => Execute(canExecute, false));

    internal bool TryExecute(Action execute) => Execute(() => { execute(); return true; }, false);

    internal T Execute<T>(Func<T> execute, T failureResult)
    {
        try { return execute(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ReportFailure(exception); }
        return failureResult;
    }

    internal Task ClipboardCompletion { get; private set; } = Task.CompletedTask;

    internal Task ExecuteClipboardAsync(Func<Task> execute) => ClipboardCompletion = ExecuteClipboardCoreAsync(execute);

    private async Task ExecuteClipboardCoreAsync(Func<Task> execute)
    {
        try
        {
            await ExecuteAsync(execute);
        }
        catch (OperationCanceledException)
        {
            // Cancellation of a native clipboard request has no pending caller to notify.
        }
        catch (Exception exception)
        {
            // Native callbacks and ICommand.Execute are async void: their callers
            // cannot observe exceptions. Report every failure at this boundary.
            ReportFailure(exception);
        }
    }

    private void ReportFailure(Exception exception)
    {
        if (_editor.StructuredInputCoordinator is { } coordinator &&
            ReferenceEquals(_editor.StructuredSession, coordinator.StructuredSession))
        {
            coordinator.Commands.ReportFailure(exception);
            return;
        }
        System.Diagnostics.Trace.TraceError("Editor command failed: {0}", exception);
        Failed?.Invoke(this, new(exception));
    }

    private static bool IsClipboardFailure(Exception exception) =>
        exception is COMException or IOException or UnauthorizedAccessException
#if ANDROID
        or global::Java.Lang.SecurityException or global::Java.IO.IOException
#endif
        ;

    internal static Task ExecuteAsync(Func<Task> execute) => execute();
}
