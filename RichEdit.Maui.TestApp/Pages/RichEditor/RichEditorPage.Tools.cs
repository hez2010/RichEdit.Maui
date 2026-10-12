using System.Globalization;
using RichEdit.Core;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage
{
    private bool _updatingTools;
    private Picker? _fontPicker, _sizePicker;
    private readonly Dictionary<string, Button> _formatButtons = [];
    private Entry? _linkEntry, _replacementEntry;
    private RichEditorSession? _typingSession;

    private void InitializeTools()
    {
        DemoPicker.ItemsSource = new[] { "Formatting", "Paragraphs", "Lists & styles", "Insert", "Tables", "Annotations & review",
            "Selections", "Checked edits", "Tracking", "Widgets", "Layout & input", "Document options" };
        DemoPicker.SelectedIndex = 0;
        ObserveTypingFormat();
        Editor.SelectionChanged += (_, _) => { UpdateFormattingTools(); UpdateGeometry(); };
        Editor.ContentChanged += OnDemoContentChanged;
        Editor.DocumentChanged += (_, _) => { ObserveTypingFormat(); ResetDemos(); UpdateFormattingTools(); };
        Editor.TextLayout.Changed += (_, _) => UpdateGeometry();
        Editor.CompositionChanged += (_, _) => UpdateInputReadout();
        Editor.PointerMoved += OnDemoPointer;
        Editor.PointerPressed += OnDemoPointer;
        var insertDate = new Command(InsertDate, () => !Editor.IsReadOnly);
        var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? EditorKeyModifiers.Meta : EditorKeyModifiers.Control;
        Editor.KeyBindings.Add(new(EditorKey.D, primary | EditorKeyModifiers.Shift, insertDate));
        Editor.ContextMenuOpening += (_, args) =>
        {
            args.Items.Add(new MenuFlyoutSeparator());
            args.Items.Add(new MenuFlyoutItem { Text = "Insert date", Command = insertDate });
        };
        Editor.LinkInvoked += (_, args) => { args.Handled = true; ResultLabel.Text = $"Link: {args.Link.Target}"; };
        Editor.ObjectInvoked += (_, args) => { args.Handled = true; ResultLabel.Text = $"Selected object: {args.Object.Id}"; };
    }

    private void ObserveTypingFormat()
    {
        if (_typingSession is not null) _typingSession.TypingFormatChanged -= OnTypingFormatChanged;
        _typingSession = Editor.Session;
        _typingSession.TypingFormatChanged += OnTypingFormatChanged;
    }
    private void OnTypingFormatChanged(object? sender, EventArgs args) => UpdateFormattingTools();

    private void OnDemoChanged(object? sender, EventArgs args)
    {
        if (ToolPanel is null || Editor is null) return;
        ToolPanel.Children.Clear();
        _formatButtons.Clear();
        _fontPicker = _sizePicker = null;
        switch (DemoPicker.SelectedItem as string)
        {
            case "Formatting":
                _fontPicker = Choose("Font family", new[] { "Default", "Arial", "Times New Roman", "Courier New", "Georgia", "Verdana", "Consolas", "Menlo" },
                    value => Format(new() { FontFamily = value == "Default" ? StyleValue<string?>.Initial : value }));
                _sizePicker = Choose("Font size", new[] { "10", "12", "14", "17", "20", "24", "30", "36" },
                    value => Format(new() { FontSize = RichFontSize.FromPoints(double.Parse(value, CultureInfo.InvariantCulture)) }));
                Row(_fontPicker, _sizePicker, FormatButton("Bold", () => RunRich(Editor.ToggleBold)),
                    FormatButton("Italic", () => RunRich(Editor.ToggleItalic)), FormatButton("Underline", () => RunRich(Editor.ToggleUnderline)),
                    FormatButton("Strike", () => Format(new() { Strike = Formats().All(x => x.Strike != RichStrike.None) ? RichStrike.None : RichStrike.Single })),
                    FormatButton("Superscript", () => Script(RichVerticalPosition.Superscript)),
                    FormatButton("Subscript", () => Script(RichVerticalPosition.Subscript)), ActionButton("Clear formatting", ClearCharacterFormatting));
                Row(Choose("Text color", ColorNames, value => Format(new() { Foreground = ColorValue(value) })),
                    Choose("Highlight", ColorNames, value => Format(new() { Background = ColorValue(value) })),
                    Choose("Underline style", Enum.GetNames<RichUnderline>(), value => Format(new() { Underline = Enum.Parse<RichUnderline>(value) })),
                    ActionButton("More character options", ShowCharacterOptions));
                break;
            case "Paragraphs":
                Row(Enum.GetValues<RichAlignment>().Select(alignment => ActionButton(alignment.ToString(), () => Paragraph(new() { Alignment = alignment }))).ToArray());
                Row(ActionButton("Indent", () => ChangeIndent(18)), ActionButton("Outdent", () => ChangeIndent(-18)),
                    ActionButton("First-line indent", () => Paragraph(new() { FirstLineIndent = RichLength.FromPoints(18) })),
                    ActionButton("Hanging indent", () => Paragraph(new() { FirstLineIndent = RichLength.FromPoints(-18) })),
                    Choose("Line spacing", new[] { "Single", "1.5 lines", "Double", "Exactly 24 pt", "At least 24 pt" }, value => Paragraph(new()
                    {
                        LineSpacing = value switch { "1.5 lines" => RichLineSpacing.Multiple(1.5), "Double" => RichLineSpacing.Multiple(2),
                            "Exactly 24 pt" => RichLineSpacing.Exactly(RichLength.FromPoints(24)), "At least 24 pt" => RichLineSpacing.AtLeast(RichLength.FromPoints(24)), _ => RichLineSpacing.Automatic },
                    })), ActionButton("More paragraph options", ShowParagraphOptions));
                break;
            case "Lists & styles":
                Row(ActionButton("Styles & lists", () => OnStyles(this, EventArgs.Empty)),
                    ActionButton("Indent", () => ChangeIndent(18)), ActionButton("Outdent", () => ChangeIndent(-18)),
                    ActionButton("Restart at 1", () => ChangeNumbering(null)),
                    Choose("Number style", Enum.GetNames<RichListNumberingStyle>(), value => ChangeNumbering(Enum.Parse<RichListNumberingStyle>(value))),
                    ActionButton("Nested list", AddNestedList));
                break;
            case "Insert":
                _linkEntry = new Entry { Placeholder = "https://example.com", Text = "https://example.com", WidthRequest = 220, AutomationId = "LinkTarget" };
                Row(_linkEntry, ActionButton("Link", () => SetHyperlink(_linkEntry.Text)), ActionButton("Unlink", () => SetHyperlink(null)),
                    ActionButton("Image", InsertSampleImage), ActionButton("Date field", InsertDate), ActionButton("Custom field", InsertCustomField),
                    ActionButton("Soft break", () => RunRich(() => Editor.ReplaceSelection("\u2028"))));
                break;
            case "Tables":
                Row(ActionButton("Insert table", () => RunRich(() => Editor.ReplaceSelection(SampleTable()))), ActionButton("Table operations", () => OnTableTools(this, EventArgs.Empty)), ActionButton("Cell appearance", ShowCellAppearance),
                    ActionButton("Select table", SelectTable), ActionButton("Delete table", DeleteTable),
                    ActionButton("Previous cell", () => Editor.MoveToAdjacentCell(false)), ActionButton("Next cell", () => Editor.MoveToAdjacentCell()));
                break;
            case "Annotations & review":
                Row(ActionButton("Comments & bookmarks", () => OnAnnotations(this, EventArgs.Empty)), ActionButton("Review changes", () => OnReview(this, EventArgs.Empty)));
                break;
            case "Selections":
                Row(ActionButton("Select all", Editor.SelectAll), ActionButton("Caret above", () => Editor.AddCaretAbove()),
                    ActionButton("Caret below", () => Editor.AddCaretBelow()), ActionButton("Single caret", KeepPrimarySelection),
                    ActionButton("Select paragraph", SelectParagraph), ActionButton("Cut", () => Editor.Commands.Cut.Execute(null)),
                    ActionButton("Copy", () => Editor.Commands.Copy.Execute(null)), ActionButton("Paste", () => Editor.Commands.Paste.Execute(null)),
                    ActionButton("Paste plain text", PastePlainText));
                break;
            case "Checked edits":
                _replacementEntry = new Entry { Text = "updated", Placeholder = "Replacement text", WidthRequest = 180, AutomationId = "PrimitiveReplacement" };
                Row(_replacementEntry, ActionButton("Capture", CaptureEdit), ActionButton("Apply edit", ApplyCapturedEdit),
                    ActionButton("Highlight", HighlightCapture), ActionButton("Clear highlight", () => _capturedHighlight?.Clear()));
                break;
            case "Tracking":
                Row(ActionButton("Track selection", TrackSelection), ActionButton("Select tracked range", SelectTracked),
                    ActionButton("Bookmark caret", BookmarkCaret), ActionButton("Go to bookmark", GoToBookmark), ActionButton("Clear tracking", ClearTracking));
                break;
            case "Widgets":
                Row(Enum.GetValues<RichTextAdornmentPlacement>().Select(placement => ActionButton(placement.ToString(), () => AddWidget(placement))).ToArray());
                Row(ActionButton("Clear widgets", ClearWidgets), ActionButton("Highlight selection", HighlightSelection));
                break;
            case "Layout & input":
                Row(ActionButton("Capture layout", CaptureLayout), ActionButton("Inspect capture", InspectLayout), ActionButton("Inspect selection", InspectSelection),
                    ActionButton("Show / hide bounds", () => { _showGeometry = !_showGeometry; UpdateGeometry(); }),
                    ActionButton("Complete composition", () => ResultLabel.Text = Editor.TryCompleteComposition() ? "Composition is complete." : "Input is still composing."));
                break;
            case "Document options":
                Row(Toggle("Read only", Editor.IsReadOnly, value => Editor.IsReadOnly = value),
                    Toggle("Spell check", Editor.IsSpellCheckEnabled ?? true, value => Editor.IsSpellCheckEnabled = value),
                    Toggle("Prediction", Editor.IsTextPredictionEnabled ?? true, value => Editor.IsTextPredictionEnabled = value),
                    Toggle("Accept Tab", Editor.AcceptsTab, value => Editor.AcceptsTab = value),
                    ActionButton("Save RTF", () => OnSaveRtf(this, EventArgs.Empty)),
                    ActionButton("RTF support", ShowCapabilities));
                break;
        }
        UpdateFormattingTools();
    }

    private void Row(params View[] views)
    {
        var row = new HorizontalStackLayout { Spacing = 5 };
        foreach (var view in views) row.Add(view);
        ToolPanel.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Default });
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, AutomationId = "Rich" + text.Replace(" ", "", StringComparison.Ordinal) };
        button.Clicked += (_, _) => action();
        return button;
    }
    private Button FormatButton(string name, Action action) { var button = ActionButton(name, action); _formatButtons.Add(name, button); return button; }
    private Picker Choose(string title, string[] values, Action<string> action)
    {
        var picker = new Picker { Title = title, ItemsSource = values, WidthRequest = title == "Font family" ? 155 : 135, AutomationId = "Rich" + title.Replace(" ", "", StringComparison.Ordinal) };
        SemanticProperties.SetDescription(picker, title);
        picker.SelectedIndexChanged += (_, _) => { if (!_updatingTools && picker.SelectedItem is string value) action(value); };
        return picker;
    }
    private static View Toggle(string text, bool initial, Action<bool> action)
    {
        var toggle = new Switch { IsToggled = initial };
        toggle.Toggled += (_, args) => action(args.Value);
        return new HorizontalStackLayout { Spacing = 5, Children = { toggle, new Label { Text = text, VerticalOptions = LayoutOptions.Center } } };
    }
    private IReadOnlyList<ResolvedCharacterStyle> Formats()
    {
        var formats = Editor.Document.CurrentSnapshot.GetSelectedCharacterFormats(Editor.Selection);
        return formats.IsEmpty ? [Editor.Session.ResolveTypingCharacterFormat()] : formats;
    }
    private void UpdateFormattingTools()
    {
        if (Editor is null || ToolPanel is null) return;
        var formats = Formats();
        _updatingTools = true;
        try
        {
            if (_fontPicker is not null) _fontPicker.SelectedItem = formats.Select(x => x.FontFamily ?? "Default").Distinct().Count() == 1 ? formats[0].FontFamily ?? "Default" : null;
            if (_sizePicker is not null) _sizePicker.SelectedItem = formats.Select(x => x.FontSize).Distinct().Count() == 1 ? (formats[0].FontSize?.Points ?? Editor.FontSize ?? 17).ToString(CultureInfo.InvariantCulture) : null;
            foreach (var (name, button) in _formatButtons)
            {
                var active = formats.All(x => name switch { "Bold" => x.Bold, "Italic" => x.Italic, "Underline" => x.Underline != RichUnderline.None,
                    "Strike" => x.Strike != RichStrike.None, "Superscript" => x.VerticalPosition.Kind == RichVerticalPositionKind.Superscript,
                    "Subscript" => x.VerticalPosition.Kind == RichVerticalPositionKind.Subscript, _ => false });
                button.Text = active ? "✓ " + name : name;
            }
        }
        finally { _updatingTools = false; }
    }
}
