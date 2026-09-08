using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

public sealed partial class CodeEditorPage : ContentPage
{
    private readonly CodeEditorActions _actions;

    public CodeEditorPage()
    {
        InitializeComponent();
        _actions = new CodeEditorActions(Editor);
        Editor.Document = CodeDocument.FromPlainText(Sample);
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.TextChanged += (_, _) => UpdateStatus();
        Editor.HighlightingFailed += (_, args) => SearchStatusLabel.Text = args.Exception.Message;
        var find = new Command(() => QueryEntry.Focus());
        var comment = new Command(_actions.ToggleLineComment, () => !Editor.IsReadOnly);
        var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? EditorKeyModifiers.Meta : EditorKeyModifiers.Control;
        Editor.KeyBindings.Add(new(EditorKey.F, primary, find));
        Editor.ContextMenuOpening += (_, args) =>
        {
            args.Items.Add(new MenuFlyoutSeparator());
            args.Items.Add(new MenuFlyoutItem { Text = "Find…", Command = find });
            args.Items.Add(new MenuFlyoutItem { Text = "Toggle line comment", Command = comment });
        };

        UpdateStatus();
    }

    private void OnIndentClicked(object? sender, EventArgs e) => _actions.Indent();

    private void OnOutdentClicked(object? sender, EventArgs e) => _actions.Outdent();

    private void OnToggleLineCommentClicked(object? sender, EventArgs e) => _actions.ToggleLineComment();

    private void OnFindPrevious(object? sender, EventArgs e) => ShowMatch(_actions.FindPrevious(QueryEntry.Text ?? string.Empty));

    private void OnFindNext(object? sender, EventArgs e) => ShowMatch(_actions.FindNext(QueryEntry.Text ?? string.Empty));

    private void OnReplaceAllClicked(object? sender, EventArgs e)
    {
        var count = _actions.ReplaceAll(QueryEntry.Text ?? string.Empty, ReplacementEntry.Text ?? string.Empty);
        SearchStatusLabel.Text = $"{count} replaced";
    }

    private void OnDarkThemeChanged(object? sender, CheckedChangedEventArgs e) => Editor.Theme = e.Value ? CodeEditorTheme.Dark : CodeEditorTheme.Light;

    private void UpdateStatus() => StatusLabel.Text = $"Ln {Editor.CaretPosition.Line}, Col {Editor.CaretPosition.Column}    ·    {Editor.LineCount} lines    ·    C#";

    private void ShowMatch(RichTextRange? match)
    {
        SearchStatusLabel.Text = match is null ? "No match" : $"{_actions.FindAll(QueryEntry.Text ?? string.Empty).Count} matches";
        if (match is not null) Editor.Focus();
    }

    private const string Sample = """
        using CodeEdit.Maui;
        using RichEdit.Maui;

        // Native editing; derived syntax formatting does not create undo units.
        var editor = new CodeEditor
        {
            Document = CodeDocument.FromPlainText("Hello, code!"),
            Theme = CodeEditorTheme.Light,
            IndentSize = 4,
            ShowLineNumbers = true,
        };

        editor.Document.Edit(edit =>
        {
            edit.InsertText(editor.Document.Length, "\nAnother line");
        });

        public sealed class Counter
        {
            private int _value = 42;

            public string Describe()
            {
                var message = $"Count: {_value}";
                return message;
            }
        }
        """;
}
