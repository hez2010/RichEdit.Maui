using System.ComponentModel;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A native source-code editor with syntax colors, line numbers, and atomic editing commands.</summary>
/// <remarks>
/// Uses an internal <see cref="RichEditor"/> through its public APIs. Syntax colors are
/// derived document formatting; they preserve undo/redo. Persist source using Document.Text.
/// </remarks>
public sealed partial class CodeEditor : ContentView
{
    /// <summary>Identifies <see cref="Document"/>.</summary>
    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document), typeof(RichTextDocument), typeof(CodeEditor),
        defaultValueCreator: static view => ((CodeEditor)view).TextView.Document,
        validateValue: static (_, value) => value is RichTextDocument,
        coerceValue: static (view, value) =>
        {
            var editor = (CodeEditor)view;
            editor.TextView.Document = (RichTextDocument)value;
            return editor.TextView.Document;
        },
        propertyChanged: static (view, _, value) => ((CodeEditor)view).SetDocument((RichTextDocument)value));
    /// <summary>Identifies <see cref="SelectedRange"/>.</summary>
    public static readonly BindableProperty SelectedRangeProperty = BindableProperty.Create(
        nameof(SelectedRange), typeof(RichTextRange), typeof(CodeEditor), RichTextRange.Empty, BindingMode.TwoWay,
        coerceValue: static (view, value) =>
        {
            var range = (RichTextRange)value;
            var length = ((CodeEditor)view).Document.Length;
            var start = Math.Min(range.Start, length);
            return new RichTextRange(start, Math.Min(range.Length, length - start));
        },
        propertyChanged: static (view, _, value) => ((CodeEditor)view).TextView.SelectedRange = (RichTextRange)value);
    /// <summary>Identifies <see cref="Highlighter"/>.</summary>
    public static readonly BindableProperty HighlighterProperty = BindableProperty.Create(
        nameof(Highlighter), typeof(ICodeSyntaxHighlighter), typeof(CodeEditor), new CSharpSyntaxHighlighter(),
        propertyChanged: static (view, _, _) => ((CodeEditor)view).ScheduleHighlighting());
    /// <summary>Identifies <see cref="Theme"/>.</summary>
    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme), typeof(CodeEditorTheme), typeof(CodeEditor), CodeEditorTheme.Light,
        validateValue: static (_, value) => value is CodeEditorTheme,
        propertyChanged: static (view, _, _) => ((CodeEditor)view).UpdateAppearance());
    /// <summary>Identifies <see cref="FontFamily"/>.</summary>
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily), typeof(string), typeof(CodeEditor), DefaultFontFamily,
        validateValue: static (_, value) => value is string family && !string.IsNullOrWhiteSpace(family),
        propertyChanged: static (view, _, _) => ((CodeEditor)view).UpdateAppearance());
    /// <summary>Identifies <see cref="FontSize"/>.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(CodeEditor), 14d,
        validateValue: static (_, value) => value is double size && double.IsFinite(size) && size > 0 && size <= float.MaxValue,
        propertyChanged: static (view, _, _) => ((CodeEditor)view).UpdateAppearance());
    /// <summary>Identifies <see cref="IsReadOnly"/>.</summary>
    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly), typeof(bool), typeof(CodeEditor), false,
        propertyChanged: static (view, _, value) =>
        {
            var editor = (CodeEditor)view;
            editor.TextView.IsReadOnly = (bool)value;
            editor.NativeAdapter?.UpdateConfiguration();
            editor.Commands.Refresh();
        });
    /// <summary>Identifies <see cref="Placeholder"/>.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(CodeEditor), string.Empty,
        coerceValue: static (_, value) => value ?? string.Empty,
        propertyChanged: static (view, _, value) => ((CodeEditor)view).TextView.Placeholder = (string)value);
    /// <summary>Identifies <see cref="MaxLength"/>.</summary>
    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength), typeof(int), typeof(CodeEditor), -1,
        validateValue: static (_, value) => value is int length && length >= -1,
        propertyChanged: static (view, _, value) =>
        {
            var editor = (CodeEditor)view;
            editor.TextView.MaxLength = (int)value;
            editor.NativeAdapter?.UpdateConfiguration();
        });
    /// <summary>Identifies <see cref="ShowLineNumbers"/>.</summary>
    public static readonly BindableProperty ShowLineNumbersProperty = BindableProperty.Create(
        nameof(ShowLineNumbers), typeof(bool), typeof(CodeEditor), true,
        propertyChanged: static (view, _, _) => ((CodeEditor)view).UpdateGutter());
    /// <summary>Identifies <see cref="WordWrap"/>.</summary>
    public static readonly BindableProperty WordWrapProperty = BindableProperty.Create(
        nameof(WordWrap), typeof(bool), typeof(CodeEditor), false,
        propertyChanged: static (view, _, _) => ((CodeEditor)view).NativeAdapter?.UpdateConfiguration());
    /// <summary>Identifies <see cref="IndentSize"/>.</summary>
    public static readonly BindableProperty IndentSizeProperty = BindableProperty.Create(
        nameof(IndentSize), typeof(int), typeof(CodeEditor), 4,
        validateValue: static (_, value) => value is int size && size is >= 1 and <= 16);
    /// <summary>Identifies <see cref="UseTabs"/>.</summary>
    public static readonly BindableProperty UseTabsProperty = BindableProperty.Create(
        nameof(UseTabs), typeof(bool), typeof(CodeEditor), false);
    /// <summary>Identifies <see cref="AutoIndent"/>.</summary>
    public static readonly BindableProperty AutoIndentProperty = BindableProperty.Create(
        nameof(AutoIndent), typeof(bool), typeof(CodeEditor), true);
    /// <summary>Identifies <see cref="LineCommentPrefix"/>.</summary>
    public static readonly BindableProperty LineCommentPrefixProperty = BindableProperty.Create(
        nameof(LineCommentPrefix), typeof(string), typeof(CodeEditor), "//",
        validateValue: static (_, value) => value is string prefix && prefix.Length > 0 && !prefix.Any(char.IsWhiteSpace));

#if WINDOWS
    private const string DefaultFontFamily = "Consolas";
#elif ANDROID
    private const string DefaultFontFamily = "monospace";
#else
    private const string DefaultFontFamily = "Menlo";
#endif

    private readonly GraphicsView _gutter;
    private CancellationTokenSource? _highlightCancellation;
    private readonly object _highlightTag = new();
    private Microsoft.Maui.Dispatching.IDispatcherTimer? _compositionTimer;
    private IReadOnlyList<CodeToken> _tokens = Array.Empty<CodeToken>();
    internal RichEditor TextView { get; }
    internal CodeEditorNativeAdapter? NativeAdapter { get; private set; }
    internal CodeLineMap Lines { get; private set; } = new(string.Empty);

    /// <summary>Creates an editor configured for source code.</summary>
    public CodeEditor()
    {
        TextView = new RichEditor
        {
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            Keyboard = Keyboard.Create(KeyboardFlags.None),
        };
        Commands = new CodeEditorCommands(this);
        _gutter = new GraphicsView { Drawable = new LineNumberDrawable(this), InputTransparent = true };
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 0 };
        grid.Add(_gutter);
        grid.Add(TextView, 1);
        Content = grid;
        TextView.ContentChanged += OnContentChanged;
        TextView.SelectionChanged += (_, args) =>
        {
            UpdateLines();
            SetValue(SelectedRangeProperty, TextView.SelectedRange);
            OnPropertyChanged(nameof(CaretPosition));
            Commands.Refresh();
            InvalidateGutter();
            SelectionChanged?.Invoke(this, args);
        };
        TextView.PropertyChanged += OnTextViewPropertyChanged;
        TextView.Loaded += (_, _) =>
        {
            if (NativeAdapter is { } adapter)
                adapter.Post(() => { if (ReferenceEquals(NativeAdapter, adapter)) adapter.UpdateConfiguration(); });
        };
        TextView.HandlerChanging += (_, _) =>
        {
            CancelHighlighting();
            _compositionTimer?.Stop();
            NativeAdapter?.Dispose();
            NativeAdapter = null;
        };
        TextView.HandlerChanged += (_, _) =>
        {
            if (TextView.Handler is RichEditorHandler handler)
            {
                NativeAdapter = new CodeEditorNativeAdapter(this, handler);
                ScheduleHighlighting();
            }
        };
        TextView.Pasting += (_, args) =>
        {
            Pasting?.Invoke(this, args);
            args.Fragment = RichTextDocumentFragment.FromPlainText(args.Fragment.Text);
        };
        SetValue(DocumentProperty, TextView.Document);
        UpdateLines();
        UpdateAppearance();
    }

    /// <summary>Gets or sets the stable live source document. Persist code through <see cref="RichTextDocument.Text"/>.</summary>
    public RichTextDocument Document
    {
        get => TextView.Document;
        set { ArgumentNullException.ThrowIfNull(value); SetValue(DocumentProperty, value); }
    }
    /// <summary>Gets or sets the selected UTF-16 range, clamped to the source length.</summary>
    public RichTextRange SelectedRange { get => (RichTextRange)GetValue(SelectedRangeProperty); set => SetValue(SelectedRangeProperty, value); }
    /// <summary>Gets the underlying live selection facade.</summary>
    public RichTextSelection Selection => TextView.Selection;
    /// <summary>Gets or sets the highlighter, or null to disable syntax coloring.</summary>
    public ICodeSyntaxHighlighter? Highlighter { get => (ICodeSyntaxHighlighter?)GetValue(HighlighterProperty); set => SetValue(HighlighterProperty, value); }
    /// <summary>Gets or sets the editor palette.</summary>
    public CodeEditorTheme Theme { get => (CodeEditorTheme)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }
    /// <summary>Gets or sets the monospaced font family.</summary>
    public string FontFamily { get => (string)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    /// <summary>Gets or sets the font size in device-independent units.</summary>
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    /// <summary>Gets or sets whether user edits and code-editing commands are disabled.</summary>
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    /// <summary>Gets or sets the empty-source placeholder.</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    /// <summary>Gets or sets the maximum UTF-16 source length, or -1 for unlimited.</summary>
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    /// <summary>Gets or sets whether logical line numbers are visible.</summary>
    public bool ShowLineNumbers { get => (bool)GetValue(ShowLineNumbersProperty); set => SetValue(ShowLineNumbersProperty, value); }
    /// <summary>Gets or sets whether long lines wrap. The default is horizontal scrolling.</summary>
    public bool WordWrap { get => (bool)GetValue(WordWrapProperty); set => SetValue(WordWrapProperty, value); }
    /// <summary>Gets or sets the indentation width in spaces, from 1 through 16. This is not a native tab-stop setting.</summary>
    public int IndentSize { get => (int)GetValue(IndentSizeProperty); set => SetValue(IndentSizeProperty, value); }
    /// <summary>Gets or sets whether indentation inserts tabs instead of spaces.</summary>
    public bool UseTabs { get => (bool)GetValue(UseTabsProperty); set => SetValue(UseTabsProperty, value); }
    /// <summary>Gets or sets whether Enter copies the current line's leading whitespace.</summary>
    public bool AutoIndent { get => (bool)GetValue(AutoIndentProperty); set => SetValue(AutoIndentProperty, value); }
    /// <summary>Gets or sets the prefix used by <see cref="ToggleLineComment"/>.</summary>
    public string LineCommentPrefix { get => (string)GetValue(LineCommentPrefixProperty); set => SetValue(LineCommentPrefixProperty, value); }
    /// <summary>Gets the logical line count, including a trailing empty line.</summary>
    public int LineCount => Lines.Count;
    /// <summary>Gets the one-based position of the selection start.</summary>
    public CodePosition CaretPosition => Lines.GetPosition(Math.Min(SelectedRange.Start, Lines.Text.Length));
    /// <summary>Gets the last successfully classified tokens.</summary>
    public IReadOnlyList<CodeToken> Tokens => _tokens;
    /// <summary>Gets whether an undo unit is available.</summary>
    public bool CanUndo => TextView.CanUndo;
    /// <summary>Gets whether a redo unit is available.</summary>
    public bool CanRedo => TextView.CanRedo;
    /// <summary>Gets the stable MVVM commands.</summary>
    public CodeEditorCommands Commands { get; }

    /// <summary>Occurs after an atomic source-document change.</summary>
    public event EventHandler<RichTextContentChangedEventArgs>? ContentChanged;
    /// <summary>Occurs after logical source text changes.</summary>
    public event EventHandler<RichTextTextChangedEventArgs>? TextChanged;
    /// <summary>Occurs after the caret or selection changes.</summary>
    public event EventHandler<RichTextSelectionChangedEventArgs>? SelectionChanged;
    /// <summary>Occurs before paste. The accepted fragment is converted to plain text.</summary>
    public event EventHandler<RichTextPastingEventArgs>? Pasting;
    /// <summary>Occurs when automatic highlighting fails. Explicit refresh calls propagate failures to their caller.</summary>
    public event EventHandler<CodeHighlightingFailedEventArgs>? HighlightingFailed;

    /// <summary>Focuses the native text surface.</summary>
    /// <returns>Whether the focus request succeeded.</returns>
    public new bool Focus() => TextView.Focus();
    /// <summary>Undoes the most recent source edit.</summary>
    public void Undo() => TextView.Undo();
    /// <summary>Reapplies the most recently undone source edit.</summary>
    public void Redo() => TextView.Redo();
    /// <summary>Clears undo and redo history.</summary>
    public void ClearUndoHistory() => TextView.ClearUndoHistory();
    /// <summary>Selects all source text.</summary>
    public void SelectAll() => TextView.SelectAll();
    /// <summary>Copies the selection through the native clipboard.</summary>
    /// <returns>The clipboard operation.</returns>
    public Task CopyAsync() => TextView.CopyAsync();
    /// <summary>Cuts the selection through the native clipboard.</summary>
    /// <returns>The clipboard operation.</returns>
    public Task CutAsync() => TextView.CutAsync();
    /// <summary>Pastes clipboard content as plain source text.</summary>
    /// <returns>The clipboard operation.</returns>
    public Task PasteAsync() => TextView.PasteAsync();

    /// <summary>Reclassifies the current source immediately and updates native syntax colors.</summary>
    /// <param name="cancellationToken">Cancellation for this refresh.</param>
    /// <returns>A task completing after classification and presentation. Call on the UI thread.</returns>
    public Task RefreshHighlightingAsync(CancellationToken cancellationToken = default) => HighlightAsync(TimeSpan.Zero, cancellationToken);

    private async Task HighlightAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        CancelHighlighting();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _highlightCancellation = cancellation;
        var token = cancellation.Token;
        var document = Document;
        var version = document.Version;
        var text = document.Text;
        var highlighter = Highlighter;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            var tokens = await Task.Run(() =>
            {
                var result = highlighter is null ? Array.Empty<CodeToken>() :
                    highlighter.Highlight(text, token) ?? throw new InvalidOperationException("A highlighter must return a token collection.");
                var copy = new CodeToken[result.Count];
                var end = 0;
                for (var i = 0; i < copy.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = result[i];
                    if (item.Range.IsEmpty || item.Range.Start < end || item.Range.End > text.Length || !Enum.IsDefined(item.Kind))
                        throw new InvalidOperationException("Syntax tokens must be ordered, nonempty, non-overlapping UTF-16 ranges within the source.");
                    copy[i] = item;
                    end = item.Range.End;
                }
                return Array.AsReadOnly(copy);
            }, token);
            // Always leave a possible document notification before applying formatting.
            await Task.Yield();
            if (token.IsCancellationRequested || !ReferenceEquals(Document, document) || document.Version != version) return;
            if (NativeAdapter?.IsComposing == true)
            {
                WaitForComposition();
                return;
            }
            var formatting = GetForegroundChanges(document.CurrentSnapshot, tokens);
            document.Edit(edit => edit.SetCharacterFormats(formatting),
                new RichTextEditOptions(RichTextUndoBehavior.PreserveHistory, tag: _highlightTag));
            if (token.IsCancellationRequested || !ReferenceEquals(Document, document)) return;
            _tokens = tokens;
            OnPropertyChanged(nameof(Tokens));
            InvalidateGutter();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_highlightCancellation, cancellation)) _highlightCancellation = null;
        }
    }

    internal void CancelHighlighting() => _highlightCancellation?.Cancel();

    private List<RichTextRun> GetForegroundChanges(RichTextDocumentSnapshot snapshot, IReadOnlyList<CodeToken> tokens)
    {
        var changes = new List<RichTextRun>();
        var tokenIndex = 0;
        foreach (var run in snapshot.Runs)
        {
            for (var position = run.Range.Start; position < run.Range.End;)
            {
                while (tokenIndex < tokens.Count && tokens[tokenIndex].Range.End <= position) tokenIndex++;
                var end = run.Range.End;
                Color? color = null;
                if (tokenIndex < tokens.Count)
                {
                    var token = tokens[tokenIndex];
                    if (token.Range.Start <= position)
                    {
                        end = Math.Min(end, token.Range.End);
                        color = Theme.GetColor(token.Kind);
                    }
                    else end = Math.Min(end, token.Range.Start);
                }
                if (!Equals(run.Format.ForegroundColor, color))
                {
                    var format = run.Format with { ForegroundColor = color };
                    if (changes.Count > 0 && changes[^1].Range.End == position && changes[^1].Format == format)
                    {
                        var previous = changes[^1].Range;
                        changes[^1] = new RichTextRun(new RichTextRange(previous.Start, end - previous.Start), format);
                    }
                    else changes.Add(new RichTextRun(new RichTextRange(position, end - position), format));
                }
                position = end;
            }
        }
        return changes;
    }

    internal async void ScheduleHighlighting()
    {
        CancelHighlighting();
        _compositionTimer?.Stop();
        _tokens = Array.Empty<CodeToken>();
        OnPropertyChanged(nameof(Tokens));
        if (NativeAdapter is null) return;
        if (NativeAdapter.IsComposing) { WaitForComposition(); return; }
        try { await HighlightAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None); }
        catch (Exception exception) { HighlightingFailed?.Invoke(this, new CodeHighlightingFailedEventArgs(exception)); }
    }

    private void WaitForComposition()
    {
        if (_compositionTimer is null)
        {
            _compositionTimer = Dispatcher.CreateTimer();
            _compositionTimer.Interval = TimeSpan.FromMilliseconds(100);
            _compositionTimer.Tick += (_, _) =>
            {
                if (NativeAdapter?.IsComposing != true)
                {
                    _compositionTimer.Stop();
                    ScheduleHighlighting();
                }
            };
        }
        _compositionTimer.Start();
    }

    private void SetDocument(RichTextDocument document)
    {
        TextView.Document = document;
        NativeAdapter?.UpdateConfiguration();
        SetValue(SelectedRangeProperty, TextView.SelectedRange);
        UpdateLines();
        ScheduleHighlighting();
    }

    private void OnContentChanged(object? sender, RichTextContentChangedEventArgs args)
    {
        NativeAdapter?.UpdateConfiguration();
        UpdateLines();
        if (!ReferenceEquals(args.ChangeSet.Tag, _highlightTag)) ScheduleHighlighting();
        if (args.ChangeSet.Origin == RichTextChangeOrigin.User) QueueNativeAutoIndent(args.ChangeSet);
        ContentChanged?.Invoke(this, args);
        if (args.ChangeSet.IsTextChanged) TextChanged?.Invoke(this, new RichTextTextChangedEventArgs(args.ChangeSet));
    }

    private void OnTextViewPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CanUndo) or nameof(CanRedo))
        {
            OnPropertyChanged(args.PropertyName);
            Commands.Refresh();
        }
    }

    private void UpdateLines()
    {
        if (ReferenceEquals(Lines.Text, Document.Text)) return;
        Lines = new CodeLineMap(Document.Text);
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(CaretPosition));
        UpdateGutter();
    }

    private void UpdateAppearance()
    {
        TextView.FontFamily = FontFamily;
        TextView.FontSize = FontSize;
        TextView.TextColor = Theme.TextColor;
        TextView.BackgroundColor = Theme.BackgroundColor;
        TextView.PlaceholderColor = Theme.LineNumberColor;
        NativeAdapter?.UpdateConfiguration();
        UpdateGutter();
        ScheduleHighlighting();
    }

    private void UpdateGutter()
    {
        _gutter.IsVisible = ShowLineNumbers;
        _gutter.WidthRequest = Math.Max(3, LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) * FontSize * 0.7 + 16;
        InvalidateGutter();
    }

    internal void InvalidateGutter() => _gutter.Invalidate();

    private sealed class LineNumberDrawable(CodeEditor owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = owner.Theme.GutterBackgroundColor;
            canvas.FillRectangle(dirtyRect);
            canvas.Font = new Microsoft.Maui.Graphics.Font(owner.FontFamily);
            canvas.FontSize = (float)owner.FontSize;
            if (owner.NativeAdapter is not { } handler) return;
            foreach (var line in handler.GetVisibleLines())
            {
                canvas.FontColor = line.Number == owner.CaretPosition.Line ? owner.Theme.CurrentLineNumberColor : owner.Theme.LineNumberColor;
                canvas.DrawString(line.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, line.Top,
                    (float)owner._gutter.Width - 8, line.Height, HorizontalAlignment.Right, VerticalAlignment.Center);
            }
        }
    }
}

internal readonly record struct VisibleCodeLine(int Number, float Top, float Height);
