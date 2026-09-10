using System.Text.RegularExpressions;
using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

// These are application features. The component deliberately depends only on public editor APIs.
internal sealed partial class CodeEditorFeatures : IDisposable
{
    private readonly CodeEditor _editor;
    private readonly Grid _host;
    private readonly AbsoluteLayout _popups = new() { InputTransparent = true, CascadeInputTransparent = false };
    private readonly Border _completion = new() { BackgroundColor = Colors.White, Stroke = Colors.SlateGray, Padding = 6, IsVisible = false };
    private readonly Border _hover = new() { BackgroundColor = Colors.LightYellow, Stroke = Colors.SlateGray, Padding = 8, InputTransparent = true, IsVisible = false };
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

    internal CodeEditorFeatures(CodeEditor editor, Grid host)
    {
        _editor = editor;
        _host = host;
        _layout = editor.TextLayout.CreateRelativeTo(host);
        _placeholders = editor.Decorations.CreateLayer();
        _popups.Add(_completion);
        _popups.Add(_hover);
        host.Add(_popups);
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
        var choices = new VerticalStackLayout { Spacing = 2 };
        for (var index = 0; index < _suggestions.Length; index++)
        {
            var candidate = index;
            var button = new Button { Text = _suggestions[index], FontSize = 13, Padding = new Thickness(10, 4),
                BackgroundColor = index == _suggestion ? Colors.LightSkyBlue : Colors.White, TextColor = Colors.Black, HorizontalOptions = LayoutOptions.Fill };
            button.Clicked += (_, _) => AcceptCompletion(candidate);
            choices.Add(button);
        }
        _completion.Content = choices;
        _completion.IsVisible = true;
        _hover.IsVisible = false;
        PositionPopups();
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
            .Select(static range => new RichTextDecoration(range, new() { BackgroundColor = Colors.LightCyan, Underline = RichTextUnderlineStyle.Single })));
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
            args.Handled = _completion.IsVisible || _hover.IsVisible || _snippet.Count > 0;
            DismissCompletion();
            DismissHover();
            EndSnippet();
        }
        else if (_completion.IsVisible && args.Modifiers == EditorKeyModifiers.None)
        {
            if (args.Key is EditorKey.Up or EditorKey.Down)
            {
                _suggestion = (_suggestion + (args.Key == EditorKey.Up ? -1 : 1) + _suggestions.Length) % _suggestions.Length;
                RenderSuggestions();
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
        if (range == _hoverRange && _hover.IsVisible) return;
        DismissHover();
        using var request = new CancellationTokenSource();
        _hoverRequest = request;
        try
        {
            await Task.Delay(300, request.Token);
            if (_disposed || snapshot.Revision != _editor.Document.Revision || _editor.Composition.IsActive || _completion.IsVisible) return;
            _hoverRange = range;
            _hover.Content = new Label { Text = $"{snapshot.Text.Substring(range.Start, range.Length)}\nSource line {snapshot.GetPosition(range.Start).Line}", TextColor = Colors.Black, FontSize = 13 };
            _hover.IsVisible = true;
            PositionPopups();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_hoverRequest, request)) _hoverRequest = null; }
    }

    private void PositionPopups()
    {
        var layout = _layout.Capture();
        if (layout is null) { _completion.IsVisible = _hover.IsVisible = false; return; }
        if (_completion.IsVisible) Place(_completion, _layout.GetCaretBounds(layout, _editor.SelectionState.Active));
        if (_hover.IsVisible) Place(_hover, _layout.GetRangeBounds(layout, _hoverRange).FirstOrDefault());
        void Place(Border popup, Rect? anchor)
        {
            if (!popup.IsVisible) return;
            if (anchor is not { Height: > 0 } rect) { popup.IsVisible = false; return; }
            var width = Math.Min(290, _host.Width);
            var size = ((IView)popup).Measure(width, _host.Height);
            var top = rect.Bottom + 3;
            if (top + size.Height > _host.Height) top = Math.Max(0, rect.Top - size.Height - 3);
            AbsoluteLayout.SetLayoutBounds(popup, new(Math.Clamp(rect.Left, 0, Math.Max(0, _host.Width - width)), top, width, size.Height));
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
            foreach (Match match in Regex.Matches(snapshot.Text, @"CodeDocument\.FromPlainText\(").Take(6))
                _widgets.Add(_editor.Adornments.Add(match.Index + match.Length,
                    new Label { Text = "text: ", FontSize = 12, TextColor = Colors.SlateGray, Padding = new Thickness(3, 0) }, new() { Placement = RichTextAdornmentPlacement.Inline }));
            var declaration = snapshot.Text.IndexOf("public sealed class Counter", StringComparison.Ordinal);
            if (declaration >= 0)
            {
                var button = new Button { Text = "Add Value property", FontSize = 12, Padding = new Thickness(8, 3), HorizontalOptions = LayoutOptions.Start };
                var actions = new HorizontalStackLayout { Children = { button }, Padding = new Thickness(0, 3) };
                var item = _editor.Adornments.Add(declaration, actions, new() { Placement = RichTextAdornmentPlacement.AboveLine });
                _widgets.Add(item);
                button.Clicked += (_, _) =>
                {
                    var current = _editor.Document.CurrentSnapshot;
                    var brace = current.Text.IndexOf('{', item.Position);
                    if (brace >= 0) _editor.TryApplyEdits(current.Revision, (RichTextEdit[])[new(new(brace + 1, 0), "\n    public int Value => _value;\n")]);
                };
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_widgetRequest, request)) _widgetRequest = null; }
    }
    private void ClearWidgets() { foreach (var item in _widgets) item.Dispose(); _widgets.Clear(); }
    private void DismissCompletion() { _completionRequest?.Cancel(); _completion.IsVisible = false; }
    private void DismissHover() { _hoverRequest?.Cancel(); _hover.IsVisible = false; }
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
        _host.Remove(_popups);
    }
}
