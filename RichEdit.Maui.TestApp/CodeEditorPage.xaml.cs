using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

public sealed partial class CodeEditorPage : ContentPage
{
    private readonly CodeEditorActions _actions;
    private CodeColorizer? _colorizer;
    private CodeEditorFoldIndicators? _foldIndicators;
    private CodeEditorFeatures? _features;

    public CodeEditorPage()
    {
        InitializeComponent();
        _actions = new CodeEditorActions(Editor);
        Editor.Document = CodeDocument.FromPlainText(Sample);
        ApplyTheme(false);
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.TextChanged += (_, _) => UpdateStatus();
        Editor.Folding.Changed += (_, _) => UpdateStatus();
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
        _colorizer ??= new CodeColorizer(Editor);
        _features ??= new CodeEditorFeatures(Editor, EditorHost);
        _colorizer.Failed += OnColoringFailed;
        ApplyTheme(DarkThemeCheckBox.IsChecked);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _foldIndicators?.Dispose();
        _foldIndicators = null;
        _colorizer?.Dispose();
        _colorizer = null;
        _features?.Dispose();
        _features = null;
    }

    private async void OnCompleteClicked(object? sender, EventArgs args)
    {
        if (_features is not null) await _features.ShowCompletionsAsync();
    }
    private void OnSnippetClicked(object? sender, EventArgs args) => _features?.InsertSnippet();
    private void OnNextPlaceholderClicked(object? sender, EventArgs args) => _features?.NextPlaceholder();

    private void OnColoringFailed(object? sender, Exception exception) => SearchStatusLabel.Text = exception.Message;

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
        if (_colorizer is not null) _colorizer.Palette = token => token.Kind switch
        {
            CodeTokenKind.Keyword => dark ? Colors.LightSkyBlue : Colors.Blue,
            CodeTokenKind.String => dark ? Colors.LightSalmon : Colors.Maroon,
            CodeTokenKind.Comment => dark ? Colors.LightGreen : Colors.Green,
            CodeTokenKind.Number => dark ? Colors.PaleGreen : Colors.DarkCyan,
            CodeTokenKind.Preprocessor => dark ? Colors.Orchid : Colors.Purple,
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
        using RichEdit.Maui;

        // Try Complete, Insert snippet, or hover over a word.
        // Application-owned colors and widgets leave source/history intact.
        var editor = new CodeEditor
        {
            Document = CodeDocument.FromPlainText("Hello, code!"),
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
