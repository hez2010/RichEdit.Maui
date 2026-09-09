using CodeEdit.Maui;
using CodeEdit.Lsp;

namespace RichEdit.Maui.TestApp;

public sealed partial class CodeEditorPage : ContentPage
{
    private readonly CodeEditorActions _actions;
    private readonly bool _useSampleLanguageServer;
    private Tests.TestLanguageServer? _sampleLanguageServer;
    private CodeEditorFoldIndicators? _foldIndicators;

    public CodeEditorPage() : this(null) => _useSampleLanguageServer = true;

    public CodeEditorPage(LspClient? languageServer)
    {
        InitializeComponent();
        _actions = new CodeEditorActions(Editor);
        Editor.Document = CodeDocument.FromPlainText(Sample, languageId: "csharp");
        Editor.LanguageServer = languageServer;
        ApplyTheme(false);
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.TextChanged += (_, _) => UpdateStatus();
        Editor.Folding.Changed += (_, _) => UpdateStatus();
        Editor.LanguageServerFailed += (_, args) => SearchStatusLabel.Text = args.Exception.Message;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _foldIndicators ??= new CodeEditorFoldIndicators(Editor);
        if (!_useSampleLanguageServer || _sampleLanguageServer is not null) return;
        _sampleLanguageServer = new();
        Editor.LanguageServer = _sampleLanguageServer.Client;
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _foldIndicators?.Dispose();
        _foldIndicators = null;
        if (_sampleLanguageServer is not { } server) return;
        _sampleLanguageServer = null;
        Editor.LanguageServer = null;
        try { await server.Client.DisposeAsync(); }
        catch (Exception exception) { SearchStatusLabel.Text = exception.Message; }
    }

    private void OnIndentClicked(object? sender, EventArgs e) => _actions.Indent();

    private void OnOutdentClicked(object? sender, EventArgs e) => _actions.Outdent();

    private void OnToggleLineCommentClicked(object? sender, EventArgs e) => _actions.ToggleLineComment();

    private void OnCollapseSelectionClicked(object? sender, EventArgs e) => _actions.CollapseSelection();

    private void OnExpandAtCaretClicked(object? sender, EventArgs e) => _actions.ExpandAtCaret();

    private void OnExpandAllClicked(object? sender, EventArgs e) => Editor.Folding.ExpandAll();

    private void OnFindPrevious(object? sender, EventArgs e) => ShowMatch(_actions.FindPrevious(QueryEntry.Text ?? string.Empty));

    private void OnFindNext(object? sender, EventArgs e) => ShowMatch(_actions.FindNext(QueryEntry.Text ?? string.Empty));

    private void OnReplaceAllClicked(object? sender, EventArgs e)
    {
        var count = _actions.ReplaceAll(QueryEntry.Text ?? string.Empty, ReplacementEntry.Text ?? string.Empty);
        SearchStatusLabel.Text = $"{count} replaced";
    }

    private void OnDarkThemeChanged(object? sender, CheckedChangedEventArgs e) => ApplyTheme(e.Value);

    private void ApplyTheme(bool dark)
    {
        Editor.BackgroundColor = dark ? Color.FromArgb("#1E1E1E") : Colors.White;
        Editor.TextColor = dark ? Colors.LightGray : Colors.Black;
        Editor.Theme = token => token.Type switch
        {
            "keyword" or "modifier" => dark ? Colors.LightSkyBlue : Colors.Blue,
            "string" or "regexp" => dark ? Colors.LightSalmon : Colors.Maroon,
            "comment" => dark ? Colors.LightGreen : Colors.Green,
            "number" => dark ? Colors.PaleGreen : Colors.DarkCyan,
            "macro" => dark ? Colors.Orchid : Colors.Purple,
            "type" or "class" or "interface" or "struct" => dark ? Colors.Turquoise : Colors.Teal,
            "variable" when token.Modifiers.Contains("readonly") => dark ? Colors.LightCyan : Colors.DarkCyan,
            _ => null,
        };
    }

    private void UpdateStatus()
    {
        StatusLabel.Text = $"Ln {Editor.CaretPosition.Line}, Col {Editor.CaretPosition.Column}    ·    {Editor.LineCount} lines    ·    {Editor.Folding.CollapsedRanges.Count} folded    ·    C#";
        CollapseSelectionButton.IsEnabled = _actions.GetCollapseRange() is not null;
        ExpandAtCaretButton.IsEnabled = _actions.GetFoldAtCaret() is not null;
        ExpandAllButton.IsEnabled = Editor.Folding.CollapsedRanges.Count > 0;
    }

    private void ShowMatch(RichTextRange? match)
    {
        SearchStatusLabel.Text = match is null ? "No match" : $"{_actions.FindAll(QueryEntry.Text ?? string.Empty).Count} matches";
        if (match is not null) Editor.Focus();
    }

    private const string Sample = """
        using CodeEdit.Maui;
        using CodeEdit.Lsp;
        using RichEdit.Maui;

        // Native editing; derived syntax formatting does not create undo units.
        var editor = new CodeEditor
        {
            Document = CodeDocument.FromPlainText("Hello, code!"),
            Theme = token => token.Type == "keyword" ? Colors.Blue : null,
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
                // Select these lines, then choose Collapse selection above.
                var message = $"Count: {_value}";
                return message;
            }
        }
        """;
}
