using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

public sealed class CodeEditorPage : ContentPage
{
    private readonly CodeEditor _editor = new() { FontSize = 14, Placeholder = "Write some code…" };
    private readonly CodeEditorActions _actions;
    private readonly Entry _query = new() { Placeholder = "Find text", MinimumWidthRequest = 160 };
    private readonly Entry _replacement = new() { Placeholder = "Replace with", MinimumWidthRequest = 160 };
    private readonly Label _status = new() { FontSize = 12 };
    private readonly Label _searchStatus = new() { FontSize = 12, VerticalOptions = LayoutOptions.Center };

    public CodeEditorPage()
    {
        Title = "Code editor";
        _actions = new CodeEditorActions(_editor);
        _editor.Document = CodeDocument.FromPlainText(Sample);
        _editor.SelectionChanged += (_, _) => UpdateStatus();
        _editor.TextChanged += (_, _) => UpdateStatus();
        _editor.HighlightingFailed += (_, args) => _searchStatus.Text = args.Exception.Message;

        var toolbar = new HorizontalStackLayout { Spacing = 6 };
        toolbar.Add(new Button { Text = "Undo", Command = _editor.Commands.Undo });
        toolbar.Add(new Button { Text = "Redo", Command = _editor.Commands.Redo });
        toolbar.Add(EditButton("Indent", _actions.Indent));
        toolbar.Add(EditButton("Outdent", _actions.Outdent));
        toolbar.Add(EditButton("//", _actions.ToggleLineComment));
        toolbar.Add(new Button { Text = "Copy", Command = _editor.Commands.Copy });
        toolbar.Add(new Button { Text = "Paste", Command = _editor.Commands.Paste });
        toolbar.Add(Toggle("Wrap", false, value => _editor.WordWrap = value));
        toolbar.Add(Toggle("Line numbers", true, value => _editor.ShowLineNumbers = value));
        toolbar.Add(Toggle("Read only", false, value => _editor.IsReadOnly = value));
        toolbar.Add(Toggle("Dark", false, value => _editor.Theme = value ? CodeEditorTheme.Dark : CodeEditorTheme.Light));

        var previous = new Button { Text = "Previous" };
        previous.Clicked += (_, _) => ShowMatch(_actions.FindPrevious(_query.Text ?? string.Empty));
        var next = new Button { Text = "Next" };
        next.Clicked += (_, _) => ShowMatch(_actions.FindNext(_query.Text ?? string.Empty));
        _query.Completed += (_, _) => ShowMatch(_actions.FindNext(_query.Text ?? string.Empty));
        var replace = new Button { Text = "Replace all" };
        replace.Clicked += (_, _) =>
        {
            var count = _actions.ReplaceAll(_query.Text ?? string.Empty, _replacement.Text ?? string.Empty);
            _searchStatus.Text = $"{count} replaced";
        };
        replace.SetBinding(IsEnabledProperty, new Binding(nameof(CodeEditor.IsReadOnly), source: _editor,
            converter: new InverseBooleanConverter()));

        var search = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8,
            RowSpacing = 4,
        };
        search.Add(_query);
        search.Add(previous, 1);
        search.Add(next, 2);
        search.Add(_replacement, 0, 1);
        search.Add(replace, 1, 1);
        search.Add(_searchStatus, 2, 1);

        var layout = new Grid
        {
            Padding = new Thickness(16),
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            RowSpacing = 10,
        };
        layout.Add(new Label { Text = "Code editor", FontSize = 24, FontAttributes = FontAttributes.Bold });
        layout.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = toolbar }, 0, 1);
        layout.Add(search, 0, 2);
        layout.Add(_editor, 0, 3);
        layout.Add(_status, 0, 4);
        Content = layout;
        UpdateStatus();
    }

    private Button EditButton(string text, Action action)
    {
        var button = new Button { Text = text, Command = new Command(action) };
        button.SetBinding(IsEnabledProperty, new Binding(nameof(CodeEditor.IsReadOnly), source: _editor,
            converter: new InverseBooleanConverter()));
        return button;
    }

    private void UpdateStatus() => _status.Text = $"Ln {_editor.CaretPosition.Line}, Col {_editor.CaretPosition.Column}    ·    {_editor.LineCount} lines    ·    C#";

    private void ShowMatch(RichTextRange? match)
    {
        _searchStatus.Text = match is null ? "No match" : $"{_actions.FindAll(_query.Text ?? string.Empty).Count} matches";
        if (match is not null) _editor.Focus();
    }

    private static HorizontalStackLayout Toggle(string text, bool initialValue, Action<bool> changed)
    {
        var checkBox = new CheckBox { IsChecked = initialValue };
        checkBox.CheckedChanged += (_, args) => changed(args.Value);
        return new HorizontalStackLayout
        {
            Spacing = 0,
            Children = { checkBox, new Label { Text = text, VerticalOptions = LayoutOptions.Center } },
        };
    }

    private sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is false;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is false;
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
