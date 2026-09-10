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
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.TextChanged += (_, _) => UpdateStatus();
        Editor.Folding.Changed += (_, _) => UpdateStatus();
        var find = new Command(OpenSearch);
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
        var resources = Application.Current!.Resources;
        _foldIndicators ??= new CodeEditorFoldIndicators(Editor, (DataTemplate)resources["FoldIndicatorTemplate"]);
        _colorizer ??= new CodeColorizer(Editor);
        _features ??= new CodeEditorFeatures(Editor, EditorHost);
        PopupLayer.BindingContext = _features;
        _features.SetPresentation(CompletionPopup, InfoPopup, CompletionChoices,
            (DataTemplate)resources["InlineHintTemplate"], (DataTemplate)resources["CodeActionTemplate"]);
        _colorizer.Failed += OnColoringFailed;
        Application.Current.RequestedThemeChanged += OnThemeChanged;
        ApplyPalette();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Application.Current!.RequestedThemeChanged -= OnThemeChanged;
        _foldIndicators?.Dispose();
        _foldIndicators = null;
        _colorizer?.Dispose();
        _colorizer = null;
        _features?.Dispose();
        _features = null;
        PopupLayer.BindingContext = null;
    }

    private async void OnCompleteClicked(object? sender, EventArgs args)
    {
        if (_features is not null)
        {
            await _features.ShowCompletionsAsync();
            Editor.Focus();
        }
    }
    private void OnSnippetClicked(object? sender, EventArgs args) => _features?.InsertSnippet();
    private void OnWordInfoClicked(object? sender, EventArgs args) => _features?.ShowWordInfo();
    private void OnNextPlaceholderClicked(object? sender, EventArgs args) => _features?.NextPlaceholder();

    private void OnColoringFailed(object? sender, Exception exception)
    {
        SearchPanel.IsVisible = true;
        SearchStatusLabel.Text = exception.Message;
    }

    private void OnToggleTheme(object? sender, EventArgs args) => StudioTheme.Toggle();
    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs args) => ApplyPalette();
    private void OnToggleTools(object? sender, EventArgs args) => ToolsPanel.IsVisible = !ToolsPanel.IsVisible;
    private void OnToggleSearch(object? sender, EventArgs args)
    {
        if (SearchPanel.IsVisible) { SearchPanel.IsVisible = false; Editor.Focus(); }
        else OpenSearch();
    }
    private void OpenSearch() { SearchPanel.IsVisible = true; QueryEntry.Focus(); }

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

    private void ApplyPalette()
    {
        if (_features is not null) _features.PlaceholderColor = StudioTheme.Get("Selection");
        if (_colorizer is not null) _colorizer.Palette = token => token.Kind switch
        {
            CodeTokenKind.Keyword => StudioTheme.Get("Keyword"),
            CodeTokenKind.String => StudioTheme.Get("String"),
            CodeTokenKind.Comment => StudioTheme.Get("Comment"),
            CodeTokenKind.Number => StudioTheme.Get("Number"),
            CodeTokenKind.Preprocessor => StudioTheme.Get("Preprocessor"),
            _ => null,
        };
    }

    private void UpdateStatus()
    {
        StatusLabel.Text = $"Ln {Editor.CaretPosition.Line}, Col {Editor.CaretPosition.Column}  ·  {Editor.LineCount} lines";
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

        // Welcome to the playground.
        // Complete a word, or insert a snippet.
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
                // Select these lines and try folding.
                var message = $"Count: {_value}";
                return message;
            }
        }
        """;
}
