using System.Text.RegularExpressions;
using System.Windows.Input;
using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

// These are application features. The component deliberately depends only on public editor APIs.
internal sealed partial class CodeEditorFeatures : BindableObject, IDisposable
{
    private readonly CodeEditor _editor;
    private readonly Grid _host;
    private Border? _completion;
    private Border? _hover;
    private ScrollView? _completionChoices;
    private DataTemplate? _hintTemplate;
    private DataTemplate? _actionTemplate;
    private readonly RichTextLayout _layout;
    private readonly RichTextDecorationLayer _placeholders;
    private readonly List<RichTextTrackedRange> _snippet = [];
    private readonly List<RichTextAdornment> _widgets = [];
    private CancellationTokenSource? _completionRequest;
    private CancellationTokenSource? _hoverRequest;
    private CancellationTokenSource? _widgetRequest;
    private RichTextRevision _completionRevision;
    private RichTextRange _completionRange;
    private RichTextRange _hoverRange;
    private string[] _suggestions = [];
    private int _suggestion;
    private int _placeholder;
    private bool _accepting;
    private bool _disposed;

    public IReadOnlyList<CompletionSuggestion> Suggestions { get; private set; } = [];
    public bool IsCompletionVisible { get; private set; }
    public bool IsInfoVisible { get; private set; }
    public string InfoWord { get; private set; } = "";
    public string InfoKind { get; private set; } = "";
    public string InfoDescription { get; private set; } = "";
    public string InfoLocation { get; private set; } = "";
    private Color _placeholderColor = Colors.LightCyan;
    internal Color PlaceholderColor
    {
        get => _placeholderColor;
        set { _placeholderColor = value; PaintPlaceholders(); }
    }

    internal CodeEditorFeatures(CodeEditor editor, Grid host)
    {
        _editor = editor;
        _host = host;
        _layout = editor.TextLayout.CreateRelativeTo(host);
        _placeholders = editor.Decorations.CreateLayer();
        editor.KeyDown += OnKeyDown;
        editor.TextChanged += OnTextChanged;
        editor.SelectionChanged += OnSelectionChanged;
        editor.DocumentChanged += OnDocumentChanged;
        editor.CompositionChanged += OnCompositionChanged;
        editor.PointerMoved += OnPointerMoved;
        editor.PointerPressed += OnPointerPressed;
        editor.PointerExited += OnPointerExited;
        editor.Unloaded += OnUnloaded;
        editor.Loaded += OnLoaded;
        _layout.Changed += OnLayoutChanged;
        ScheduleWidgets();
    }

    internal void SetPresentation(Border completion, Border hover, ScrollView choices, DataTemplate hintTemplate, DataTemplate actionTemplate)
    {
        _completion = completion;
        _hover = hover;
        _completionChoices = choices;
        _hintTemplate = hintTemplate;
        _actionTemplate = actionTemplate;
        ScheduleWidgets();
    }

    internal async Task ShowCompletionsAsync()
    {
        if (_disposed || _editor.IsReadOnly || _editor.Composition.IsActive) return;
        _completionRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _completionRequest = request;
        var snapshot = _editor.Document.CurrentSnapshot;
        var selection = _editor.SelectionState;
        var range = WordAt(snapshot.Text, selection.Active, prefixOnly: true);
        var prefix = snapshot.Text.Substring(range.Start, range.Length);
        try
        {
            var suggestions = await Task.Run(() => Regex.Matches(snapshot.Text, @"\b[A-Za-z_]\w*\b").Select(static match => match.Value)
                .Concat((string[])["Console", "WriteLine", "string", "return", "public", "private"])
                .Distinct(StringComparer.Ordinal).Where(word => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && word != prefix)
                .Order(StringComparer.Ordinal).Take(6).Append("var name = value;").ToArray(), request.Token);
            if (_disposed || request.IsCancellationRequested || snapshot.Revision != _editor.Document.Revision || selection != _editor.SelectionState || _editor.Composition.IsActive) return;
            _completionRevision = snapshot.Revision;
            _completionRange = range;
            _suggestions = suggestions;
            _suggestion = 0;
            RenderSuggestions();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_completionRequest, request)) _completionRequest = null; }
    }

    private void RenderSuggestions()
    {
        Suggestions = _suggestions.Select((text, index) => new CompletionSuggestion(text,
            text == "var name = value;" ? "Snippet" : IsKeyword(text) ? "Keyword" : "Symbol",
            new Command(() => AcceptCompletion(index))) { IsSelected = index == _suggestion }).ToArray();
        OnPropertyChanged(nameof(Suggestions));
        IsCompletionVisible = true;
        IsInfoVisible = false;
        NotifyPopupVisibility();
        PositionPopups();
    }

    private void SelectSuggestion(int index)
    {
        _suggestion = index;
        for (var item = 0; item < Suggestions.Count; item++) Suggestions[item].IsSelected = item == index;
        if (_completionChoices?.Content is Layout rows && index < rows.Children.Count && rows.Children[index] is Element row)
        {
            _ = _completionChoices.ScrollToAsync(row, ScrollToPosition.MakeVisible, false);
        }
    }

    internal bool AcceptCompletion(int index)
    {
        if (_disposed || (uint)index >= (uint)_suggestions.Length) return false;
        var text = _suggestions[index];
        var start = _completionRange.Start;
        _accepting = true;
        try
        {
            if (!_editor.TryApplyEdits(_completionRevision, (RichTextEdit[])[new(_completionRange, text)], new(start + text.Length, start + text.Length)))
            {
                DismissCompletion();
                return false;
            }
            EndSnippet();
            if (text == "var name = value;")
            {
                foreach (var word in new[] { "name", "value" })
                    _snippet.Add(_editor.Document.Tracking.TrackRange(new(start + text.IndexOf(word, StringComparison.Ordinal), word.Length),
                        RichTextTrackingAffinity.BeforeInsertion, RichTextTrackingAffinity.AfterInsertion, RichTextTrackingDeletionBehavior.Preserve));
                _placeholder = 0;
                SelectPlaceholder();
            }
            DismissCompletion();
            _editor.Focus();
            return true;
        }
        finally { _accepting = false; }
    }

    internal void InsertSnippet()
    {
        if (_disposed) return;
        _completionRevision = _editor.Document.Revision;
        _completionRange = _editor.SelectedRange;
        _suggestions = ["var name = value;"];
        AcceptCompletion(0);
    }

    internal void ShowWordInfo()
    {
        if (_disposed || _editor.Composition.IsActive) return;
        DismissCompletion();
        DismissHover();
        var snapshot = _editor.Document.CurrentSnapshot;
        var range = WordAt(snapshot.Text, _editor.SelectionState.Active, prefixOnly: false);
        if (!range.IsEmpty) ShowWordInfo(snapshot, range);
    }

    private void ShowWordInfo(CodeDocumentSnapshot snapshot, RichTextRange range)
    {
        _hoverRange = range;
        InfoWord = snapshot.Text.Substring(range.Start, range.Length);
        InfoKind = IsKeyword(InfoWord) ? "C# KEYWORD" : "DOCUMENT SYMBOL";
        InfoDescription = InfoWord switch
        {
            "using" => "Imports a namespace or declares a disposable resource.",
            "var" => "Infers a local variable's type from its initial value.",
            "new" => "Creates an instance of a type.",
            "return" => "Returns control and an optional value to the caller.",
            "public" => "Makes a type or member accessible to other code.",
            "private" => "Limits access to the containing type.",
            "class" => "Declares a reference type.",
            "sealed" => "Prevents a class from being inherited.",
            "string" => "Represents a sequence of UTF-16 code units.",
            "int" => "Represents a signed 32-bit integer.",
            _ => "An identifier in the current document.",
        };
        var position = snapshot.GetPosition(range.Start);
        InfoLocation = $"Line {position.Line}  ·  Column {position.Column}";
        OnPropertyChanged(nameof(InfoWord));
        OnPropertyChanged(nameof(InfoKind));
        OnPropertyChanged(nameof(InfoDescription));
        OnPropertyChanged(nameof(InfoLocation));
        IsInfoVisible = true;
        NotifyPopupVisibility();
        PositionPopups();
    }

    internal bool NextPlaceholder(bool previous = false)
    {
        if (_snippet.Count == 0) return false;
        _placeholder += previous ? -1 : 1;
        if (_placeholder < 0) _placeholder = 0;
        if (_placeholder >= _snippet.Count) { EndSnippet(); return true; }
        SelectPlaceholder();
        return true;
    }

    private void SelectPlaceholder()
    {
        if (_snippet[_placeholder].Range is not { } range) { EndSnippet(); return; }
        _editor.SelectedRange = range;
        PaintPlaceholders();
    }
    private void PaintPlaceholders()
    {
        if (_disposed || _snippet.Count == 0) return;
        if (_snippet.Any(static item => item.Range is null)) { EndSnippet(); return; }
        var ranges = _snippet.Select(static item => item.Range!.Value)
            .Where(static range => !range.IsEmpty).OrderBy(static range => range.Start).ToArray();
        // Edits and undo can map distinct placeholders onto overlapping source.
        for (var index = 1; index < ranges.Length; index++)
            if (ranges[index].Start < ranges[index - 1].End) { EndSnippet(); return; }
        _placeholders.TrySet(_editor.Document.Revision, ranges
            .Select(range => new RichTextDecoration(range, new() { BackgroundColor = PlaceholderColor, Underline = RichTextUnderlineStyle.Single })));
    }
    private void EndSnippet()
    {
        foreach (var item in _snippet) item.Dispose();
        _snippet.Clear();
        _placeholders.Clear();
    }

    private async void OnKeyDown(object? sender, EditorKeyEventArgs args)
    {
        var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? EditorKeyModifiers.Meta : EditorKeyModifiers.Control;
        if (args.Key == EditorKey.Space && args.Modifiers == primary)
        {
            args.Handled = true;
            await ShowCompletionsAsync();
        }
        else if (args.Key == EditorKey.Escape)
        {
            args.Handled = IsCompletionVisible || IsInfoVisible || _snippet.Count > 0;
            DismissCompletion();
            DismissHover();
            EndSnippet();
        }
        else if (IsCompletionVisible && args.Modifiers == EditorKeyModifiers.None)
        {
            if (args.Key is EditorKey.Up or EditorKey.Down)
            {
                SelectSuggestion((_suggestion + (args.Key == EditorKey.Up ? -1 : 1) + _suggestions.Length) % _suggestions.Length);
                args.Handled = true;
            }
            else if (args.Key is EditorKey.Enter or EditorKey.Tab) { args.Handled = true; AcceptCompletion(_suggestion); }
        }
        else if (args.Key == EditorKey.Tab && args.Modifiers is EditorKeyModifiers.None or EditorKeyModifiers.Shift)
            args.Handled = NextPlaceholder(args.Modifiers == EditorKeyModifiers.Shift);
    }

    private async void OnPointerMoved(object? sender, RichTextPointerEventArgs args)
    {
        if (_disposed || _editor.Composition.IsActive || args.Buttons != RichTextPointerButtons.None) return;
        var layout = _layout.Capture();
        var point = new Point(args.Point.X + _editor.X, args.Point.Y + _editor.Y);
        if (layout is null || _layout.HitTest(layout, point) is not { Kind: RichTextHitKind.Text } hit) { DismissHover(); return; }
        var snapshot = _editor.Document.CurrentSnapshot;
        var range = WordAt(snapshot.Text, hit.Position, prefixOnly: false);
        if (range.IsEmpty) { DismissHover(); return; }
        if (range == _hoverRange && IsInfoVisible) return;
        DismissHover();
        using var request = new CancellationTokenSource();
        _hoverRequest = request;
        try
        {
            await Task.Delay(300, request.Token);
            if (_disposed || snapshot.Revision != _editor.Document.Revision || _editor.Composition.IsActive || IsCompletionVisible) return;
            ShowWordInfo(snapshot, range);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_hoverRequest, request)) _hoverRequest = null; }
    }

    private void PositionPopups()
    {
        if (_completion is null || _hover is null) return;
        var layout = _layout.Capture();
        if (layout is null) { IsCompletionVisible = IsInfoVisible = false; NotifyPopupVisibility(); return; }
        if (IsCompletionVisible && !Place(_completion, _layout.GetCaretBounds(layout, _editor.SelectionState.Active))) DismissCompletion();
        if (IsInfoVisible && !Place(_hover, _layout.GetRangeBounds(layout, _hoverRange).FirstOrDefault())) DismissHover();
        bool Place(Border popup, Rect? anchor)
        {
            if (anchor is not { Height: > 0 } rect || _host.Width <= 16) return false;
            var width = Math.Min(popup.WidthRequest, _host.Width - 16);
            var desired = ((IView)popup).Measure(width, double.PositiveInfinity);
            var below = Math.Max(0, _host.Height - rect.Bottom - 8);
            var above = Math.Max(0, rect.Top - 8);
            var useBelow = below >= desired.Height || below >= above;
            var height = Math.Min(desired.Height, useBelow ? below : above);
            if (height <= 0) return false;
            var top = useBelow ? rect.Bottom + 6 : rect.Top - height - 6;
            AbsoluteLayout.SetLayoutBounds(popup, new(Math.Clamp(rect.Left, 8, Math.Max(8, _host.Width - width - 8)), top, width, height));
            return true;
        }
    }

    private async void ScheduleWidgets()
    {
        _widgetRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _widgetRequest = request;
        var snapshot = _editor.Document.CurrentSnapshot;
        try
        {
            await Task.Delay(180, request.Token);
            if (_disposed || snapshot.Revision != _editor.Document.Revision || _editor.Composition.IsActive) return;
            ClearWidgets();
            if (_hintTemplate is not null)
                foreach (Match match in Regex.Matches(snapshot.Text, @"CodeDocument\.FromPlainText\(").Take(6))
                {
                    var hint = (View)_hintTemplate.CreateContent();
                    hint.BindingContext = "text:";
                    _widgets.Add(_editor.Adornments.Add(match.Index + match.Length, hint, new() { Placement = RichTextAdornmentPlacement.Inline }));
                }
            var declaration = snapshot.Text.IndexOf("public sealed class Counter", StringComparison.Ordinal);
            if (declaration >= 0 && _actionTemplate is not null)
            {
                var actions = (View)_actionTemplate.CreateContent();
                var item = _editor.Adornments.Add(declaration, actions, new() { Placement = RichTextAdornmentPlacement.AboveLine });
                _widgets.Add(item);
                actions.BindingContext = new CodeLineAction("Add Value property", new Command(() =>
                {
                    var current = _editor.Document.CurrentSnapshot;
                    var brace = current.Text.IndexOf('{', item.Position);
                    if (brace >= 0) _editor.TryApplyEdits(current.Revision, (RichTextEdit[])[new(new(brace + 1, 0), "\n    public int Value => _value;\n")]);
                }));
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_widgetRequest, request)) _widgetRequest = null; }
    }
    private void ClearWidgets() { foreach (var item in _widgets) item.Dispose(); _widgets.Clear(); }
    private void DismissCompletion() { _completionRequest?.Cancel(); IsCompletionVisible = false; OnPropertyChanged(nameof(IsCompletionVisible)); }
    private void DismissHover() { _hoverRequest?.Cancel(); IsInfoVisible = false; OnPropertyChanged(nameof(IsInfoVisible)); }
    private void NotifyPopupVisibility() { OnPropertyChanged(nameof(IsCompletionVisible)); OnPropertyChanged(nameof(IsInfoVisible)); }
    private void OnTextChanged(object? sender, RichTextTextChangedEventArgs args)
    {
        DismissCompletion();
        DismissHover();
        _editor.Dispatcher.Dispatch(() => { if (!_disposed) PaintPlaceholders(); });
        ScheduleWidgets();
    }
    private void OnSelectionChanged(object? sender, RichTextSelectionChangedEventArgs args) { if (!_accepting) DismissCompletion(); PositionPopups(); }
    private void OnDocumentChanged(object? sender, CodeDocumentReplacedEventArgs args) { DismissCompletion(); DismissHover(); EndSnippet(); ClearWidgets(); ScheduleWidgets(); }
    private void OnCompositionChanged(object? sender, RichTextCompositionChangedEventArgs args)
    {
        if (args.Current.IsActive) { DismissCompletion(); DismissHover(); _widgetRequest?.Cancel(); }
        else ScheduleWidgets();
    }
    private void OnPointerPressed(object? sender, RichTextPointerEventArgs args) { DismissCompletion(); DismissHover(); }
    private void OnPointerExited(object? sender, EventArgs args) => DismissHover();
    private void OnLayoutChanged(object? sender, EventArgs args) => PositionPopups();
    private void OnLoaded(object? sender, EventArgs args) => ScheduleWidgets();
    private void OnUnloaded(object? sender, EventArgs args) { DismissCompletion(); DismissHover(); _widgetRequest?.Cancel(); EndSnippet(); ClearWidgets(); }
    private static RichTextRange WordAt(string text, int position, bool prefixOnly)
    {
        var start = position;
        var end = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        if (!prefixOnly) while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return new(start, end - start);
    }

    private static bool IsKeyword(string text) => text is "using" or "var" or "new" or "return" or "public" or "private" or "sealed" or "class" or "string" or "int" or "void" or "static" or "true" or "false";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DismissCompletion(); DismissHover(); _widgetRequest?.Cancel();
        EndSnippet(); ClearWidgets();
        _placeholders.Dispose();
        _editor.KeyDown -= OnKeyDown;
        _editor.TextChanged -= OnTextChanged;
        _editor.SelectionChanged -= OnSelectionChanged;
        _editor.DocumentChanged -= OnDocumentChanged;
        _editor.CompositionChanged -= OnCompositionChanged;
        _editor.PointerMoved -= OnPointerMoved;
        _editor.PointerPressed -= OnPointerPressed;
        _editor.PointerExited -= OnPointerExited;
        _editor.Unloaded -= OnUnloaded;
        _editor.Loaded -= OnLoaded;
        _layout.Changed -= OnLayoutChanged;
        _completion = _hover = null;
        _completionChoices = null;
    }
}

internal sealed partial class CompletionSuggestion(string text, string kind, ICommand insert) : BindableObject
{
    private bool _isSelected;
    public string Text { get; } = text;
    public string Kind { get; } = kind;
    public string Glyph => Kind == "Snippet" ? "{}" : Kind == "Keyword" ? "K" : "S";
    public ICommand Insert { get; } = insert;
    public bool IsSelected
    {
        get => _isSelected;
        internal set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }
}

internal sealed record CodeLineAction(string Title, ICommand Apply);
