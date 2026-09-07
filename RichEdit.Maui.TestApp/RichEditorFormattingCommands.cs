using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace RichEdit.Maui.TestApp;

/// <summary>
/// Supplies a list definition and level to an MVVM list command.
/// </summary>
internal sealed record ListCommandRequest
{
    /// <summary>
    /// Initializes a list command request.
    /// </summary>
    /// <param name="definition">The caller-defined list.</param>
    /// <param name="level">The zero-based nesting level.</param>
    public ListCommandRequest(RichTextListDefinition definition, int level = 0)
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
internal sealed record LinkRequest
{
    /// <summary>
    /// Initializes a hyperlink request.
    /// </summary>
    /// <param name="target">The application-defined target.</param>
    /// <param name="toolTip">An optional RTF tooltip.</param>
    public LinkRequest(string target, string? toolTip = null)
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
internal sealed record FieldRequest
{
    /// <summary>
    /// Initializes a field request.
    /// </summary>
    /// <param name="instruction">The RTF field instruction.</param>
    /// <param name="result">The visible field result.</param>
    public FieldRequest(string instruction, string result)
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

// Application toolbar bindings wrap the library's selection operations.
internal sealed class RichEditorFormattingCommands : IDisposable
{
    private readonly RichEditor _editor;
    private readonly Command[] _commands;

    public RichEditorFormattingCommands(RichEditor editor)
    {
        _editor = editor;
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

        ToggleList = Create<ListCommandRequest>(
            request => editor.Selection.ToggleList(request.Definition, request.Level),
            _ => CanMutateSelection);
        SetList = Create<ListCommandRequest>(
            request => editor.Selection.SetList(request.Definition, request.Level),
            _ => CanMutateSelection);
        ClearList = Create(editor.Selection.ClearList, () => CanMutateSelection);
        IndentList = Create(() => editor.Selection.ChangeListLevel(1), () => CanMutateSelection);
        OutdentList = Create(() => editor.Selection.ChangeListLevel(-1), () => CanMutateSelection);
        RestartList = Create<int>(
            editor.Selection.RestartList,
            startAt => CanMutateSelection && startAt > 0);

        SetLink = Create<LinkRequest>(
            request => editor.Selection.SetLink(request.Target, request.ToolTip),
            _ => CanMutateSelection && !editor.SelectedRange.IsEmpty);
        RemoveLinks = Create(editor.Selection.RemoveLinks, () => CanMutateSelection);
        InsertImage = Create<RichTextImage>(editor.Selection.InsertImage, _ => CanMutateSelection);
        InsertField = Create<FieldRequest>(
            request => editor.Selection.InsertField(request.Instruction, request.Result),
            _ => CanMutateSelection);

        _commands =
        [
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
        editor.PropertyChanged += OnEditorPropertyChanged;
    }

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

    public void Dispose() => _editor.PropertyChanged -= OnEditorPropertyChanged;

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RichEditor.IsReadOnly) or nameof(RichEditor.SelectedRange))
            foreach (var command in _commands) command.ChangeCanExecute();
    }

    private bool CanMutateSelection => !_editor.IsReadOnly;

    private static Command Create(Action execute, Func<bool> canExecute) => new(execute, canExecute);

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
