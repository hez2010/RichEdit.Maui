using System.Windows.Input;

namespace RichEdit.Maui;

/// <summary>
/// Supplies a list definition and level to an MVVM list command.
/// </summary>
public sealed record RichTextListCommandRequest
{
    /// <summary>
    /// Initializes a list command request.
    /// </summary>
    /// <param name="definition">The caller-defined list.</param>
    /// <param name="level">The zero-based nesting level.</param>
    public RichTextListCommandRequest(RichTextListDefinition definition, int level = 0)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if ((uint)level >= (uint)definition.Levels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        Level = level;
    }

    /// <summary>Gets the caller-defined list.</summary>
    public RichTextListDefinition Definition { get; }

    /// <summary>Gets the zero-based nesting level.</summary>
    public int Level { get; }
}

/// <summary>
/// Supplies hyperlink data to an MVVM command.
/// </summary>
public sealed record RichTextLinkRequest
{
    /// <summary>
    /// Initializes a hyperlink request.
    /// </summary>
    /// <param name="target">The application-defined target.</param>
    /// <param name="toolTip">An optional RTF tooltip.</param>
    public RichTextLinkRequest(string target, string? toolTip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Target = target;
        ToolTip = toolTip;
    }

    /// <summary>Gets the application-defined target.</summary>
    public string Target { get; }

    /// <summary>Gets the optional RTF tooltip.</summary>
    public string? ToolTip { get; }
}

/// <summary>
/// Supplies field data to an MVVM command.
/// </summary>
public sealed record RichTextFieldRequest
{
    /// <summary>
    /// Initializes a field request.
    /// </summary>
    /// <param name="instruction">The RTF field instruction.</param>
    /// <param name="result">The visible field result.</param>
    public RichTextFieldRequest(string instruction, string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        Instruction = instruction;
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>Gets the RTF field instruction.</summary>
    public string Instruction { get; }

    /// <summary>Gets the visible field result.</summary>
    public string Result { get; }
}

/// <summary>
/// Exposes MVVM commands for a <see cref="RichEditor"/>.
/// </summary>
public sealed class RichEditorCommands
{
    private readonly RichEditor _editor;
    private readonly Command[] _commands;
    private bool _clipboardSubscribed;

    internal RichEditorCommands(RichEditor editor)
    {
        _editor = editor;
        Undo = Create(editor.Undo, () => editor.CanUndo);
        Redo = Create(editor.Redo, () => editor.CanRedo);
        Cut = Create(async () => await editor.CutAsync(), () => CanMutateSelection && !editor.SelectedRange.IsEmpty);
        Copy = Create(async () => await editor.CopyAsync(), () => !editor.SelectedRange.IsEmpty);
        Paste = Create(
            async () => await editor.PasteAsync(),
            () => CanMutateSelection && RichTextClipboard.HasContent);
        SelectAll = Create(editor.SelectAll, () => editor.Document.Length != 0);

        ToggleBold = Create(editor.Selection.ToggleBold, () => CanMutateSelection);
        ToggleItalic = Create(editor.Selection.ToggleItalic, () => CanMutateSelection);
        ToggleUnderline = Create<RichTextUnderlineStyle>(
            editor.Selection.ToggleUnderline,
            style => CanMutateSelection && style != RichTextUnderlineStyle.None);
        ToggleStrikethrough = Create<RichTextStrikethroughStyle>(
            editor.Selection.ToggleStrikethrough,
            style => CanMutateSelection && style != RichTextStrikethroughStyle.None);
        ToggleScript = Create<RichTextScript>(
            editor.Selection.ToggleScript,
            script => CanMutateSelection && script != RichTextScript.Normal);

        ToggleList = Create<RichTextListCommandRequest>(
            request => editor.Selection.ToggleList(request.Definition, request.Level),
            _ => CanMutateSelection);
        SetList = Create<RichTextListCommandRequest>(
            request => editor.Selection.SetList(request.Definition, request.Level),
            _ => CanMutateSelection);
        ClearList = Create(editor.Selection.ClearList, () => CanMutateSelection);
        IndentList = Create(() => editor.Selection.ChangeListLevel(1), () => CanMutateSelection);
        OutdentList = Create(() => editor.Selection.ChangeListLevel(-1), () => CanMutateSelection);
        RestartList = Create<int>(
            editor.Selection.RestartList,
            startAt => CanMutateSelection && startAt > 0);

        SetLink = Create<RichTextLinkRequest>(
            request => editor.Selection.SetLink(request.Target, request.ToolTip),
            _ => CanMutateSelection && !editor.SelectedRange.IsEmpty);
        RemoveLinks = Create(editor.Selection.RemoveLinks, () => CanMutateSelection);
        InsertImage = Create<RichTextImage>(editor.Selection.InsertImage, _ => CanMutateSelection);
        InsertField = Create<RichTextFieldRequest>(
            request => editor.Selection.InsertField(request.Instruction, request.Result),
            _ => CanMutateSelection);

        _commands =
        [
            (Command)Undo,
            (Command)Redo,
            (Command)Cut,
            (Command)Copy,
            (Command)Paste,
            (Command)SelectAll,
            (Command)ToggleBold,
            (Command)ToggleItalic,
            (Command)ToggleUnderline,
            (Command)ToggleStrikethrough,
            (Command)ToggleScript,
            (Command)ToggleList,
            (Command)SetList,
            (Command)ClearList,
            (Command)IndentList,
            (Command)OutdentList,
            (Command)RestartList,
            (Command)SetLink,
            (Command)RemoveLinks,
            (Command)InsertImage,
            (Command)InsertField,
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

    /// <summary>Gets the bold toggle command.</summary>
    public ICommand ToggleBold { get; }

    /// <summary>Gets the italic toggle command.</summary>
    public ICommand ToggleItalic { get; }

    /// <summary>Gets the parameterized underline toggle command.</summary>
    public ICommand ToggleUnderline { get; }

    /// <summary>Gets the parameterized strikethrough toggle command.</summary>
    public ICommand ToggleStrikethrough { get; }

    /// <summary>Gets the parameterized script toggle command.</summary>
    public ICommand ToggleScript { get; }

    /// <summary>Gets the caller-defined list toggle command.</summary>
    public ICommand ToggleList { get; }

    /// <summary>Gets the caller-defined list apply command.</summary>
    public ICommand SetList { get; }

    /// <summary>Gets the clear-list command.</summary>
    public ICommand ClearList { get; }

    /// <summary>Gets the list-indent command.</summary>
    public ICommand IndentList { get; }

    /// <summary>Gets the list-outdent command.</summary>
    public ICommand OutdentList { get; }

    /// <summary>Gets the parameterized list-restart command.</summary>
    public ICommand RestartList { get; }

    /// <summary>Gets the parameterized hyperlink command.</summary>
    public ICommand SetLink { get; }

    /// <summary>Gets the remove-hyperlinks command.</summary>
    public ICommand RemoveLinks { get; }

    /// <summary>Gets the parameterized image-insertion command.</summary>
    public ICommand InsertImage { get; }

    /// <summary>Gets the parameterized field-insertion command.</summary>
    public ICommand InsertField { get; }

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
        new(async () => await execute(), canExecute);

    private static Command Create<T>(Action<T> execute, Func<T, bool> canExecute) =>
        new(
            parameter =>
            {
                if (parameter is T value)
                {
                    execute(value);
                }
            },
            parameter => parameter is T value && canExecute(value));
}
