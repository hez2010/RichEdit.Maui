using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodeEdit.Lsp;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A native source-code editor with syntax colors, line numbers, and atomic editing commands.</summary>
/// <remarks>
/// Uses an internal <see cref="RichEditor"/> through its public APIs. Syntax colors are
/// view-owned decorations; they do not change source, saved state, or history. Persist source using Document.Text.
/// </remarks>
public sealed partial class CodeEditor : ContentView
{
    /// <summary>Identifies <see cref="Document"/>.</summary>
    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document), typeof(CodeDocument), typeof(CodeEditor),
        defaultValueCreator: static view => ((CodeEditor)view)._document,
        validateValue: static (_, value) => value is CodeDocument,
        coerceValue: static (view, value) =>
        {
            var editor = (CodeEditor)view;
            editor.VerifyAccess();
            var previous = editor._document;
            var requested = (CodeDocument)value;
            editor._document = requested;
            try
            {
                editor.TextView.Document = requested.Source;
                if (ReferenceEquals(editor.TextView.Document, requested.Source)) return requested;
                editor._document = previous;
                return previous;
            }
            catch { editor._document = previous; throw; }
        },
        propertyChanged: static (view, _, value) => ((CodeEditor)view).SetDocument((CodeDocument)value));
    /// <summary>Identifies the directional <see cref="SelectionState"/>.</summary>
    public static readonly BindableProperty SelectionStateProperty = BindableProperty.Create(
        nameof(SelectionState), typeof(RichTextSelectionState), typeof(CodeEditor), default(RichTextSelectionState), BindingMode.TwoWay,
        coerceValue: static (view, value) =>
        {
            var editor = (CodeEditor)view;
            editor.VerifyAccess();
            var selection = (RichTextSelectionState)value;
            return new RichTextSelectionState(Math.Min(selection.Anchor, editor.Document.Length), Math.Min(selection.Active, editor.Document.Length));
        },
        propertyChanged: static (view, _, value) =>
        {
            var editor = (CodeEditor)view;
            if (!editor._synchronizingSelection) editor.TextView.SelectionState = (RichTextSelectionState)value;
        });

    /// <summary>Identifies <see cref="SelectedRange"/>.</summary>
    public static readonly BindableProperty SelectedRangeProperty = BindableProperty.Create(
        nameof(SelectedRange), typeof(RichTextRange), typeof(CodeEditor), RichTextRange.Empty, BindingMode.TwoWay,
        coerceValue: static (view, value) =>
        {
            ((CodeEditor)view).VerifyAccess();
            var range = (RichTextRange)value;
            var length = ((CodeEditor)view).Document.Length;
            var start = Math.Min(range.Start, length);
            return new RichTextRange(start, Math.Min(range.Length, length - start));
        },
        propertyChanged: static (view, _, value) =>
        {
            var editor = (CodeEditor)view;
            if (!editor._synchronizingSelection) editor.TextView.SelectedRange = (RichTextRange)value;
        });
    /// <summary>Identifies <see cref="Theme"/>.</summary>
    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme), typeof(CodeEditorTheme), typeof(CodeEditor), null,
        coerceValue: static (view, value) => { ((CodeEditor)view).VerifyAccess(); return value; },
        propertyChanged: static (view, _, _) => ((CodeEditor)view).RefreshTheme());
    /// <summary>Identifies <see cref="TextColor"/>.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(CodeEditor), null,
        propertyChanged: static (view, _, value) =>
        {
            var editor = (CodeEditor)view;
            editor.TextView.TextColor = (Color?)value;
            editor.InvalidateGutter();
        });
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
    private bool _themeRefreshQueued;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _synchronizingSelection;
    private CodeDocument _document = new();
    private readonly RichTextDecorationLayer _syntaxLayer;
    private Microsoft.Maui.Dispatching.IDispatcherTimer? _compositionTimer;
    private IReadOnlyList<SemanticToken> _tokens = Array.Empty<SemanticToken>();
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
            Document = _document.Source,
        };
        Selection = new CodeTextSelection(TextView.Selection);
        _syntaxLayer = TextView.Decorations.CreateLayer();
        TextView.Folding.Changed += (_, _) => InvalidateGutter();
        Commands = new CodeEditorCommands(this);
        _gutter = new GraphicsView { Drawable = new LineNumberDrawable(this), InputTransparent = true };
        TextView.BackgroundColor = BackgroundColor;
        _gutter.BackgroundColor = BackgroundColor;
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 0 };
        grid.Add(_gutter);
        grid.Add(TextView, 1);
        Content = grid;
        TextView.ContentChanged += OnContentChanged;
        TextView.KeyDown += (_, args) => KeyDown?.Invoke(this, args);
        InitializeKeyBindings();
        TextView.ContextMenuOpening += (_, args) =>
        {
            if (FlyoutBase.GetContextFlyout(this) is MenuFlyout menu)
            {
                args.Items.Clear();
                foreach (var item in menu.Cast<IMenuElement>()) args.Items.Add(item);
                args.IncludeDefaultItems = false;
            }
            ContextMenuOpening?.Invoke(this, args);
        };
        TextView.SelectionChanged += (_, args) =>
        {
            UpdateLines();
            SynchronizeSelectionProperties();
            OnPropertyChanged(nameof(CaretPosition));
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
            ResetLanguageDocument();
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
        SetValue(DocumentProperty, _document);
        UpdateLines();
        UpdateAppearance();
    }

    /// <summary>Gets or sets the stable live source document. Persist code through <see cref="CodeDocument.Text"/>.</summary>
    public CodeDocument Document
    {
        get => _document;
        set { ArgumentNullException.ThrowIfNull(value); SetValue(DocumentProperty, value); }
    }
    /// <summary>Gets or sets the selected UTF-16 range, clamped to the source length.</summary>
    public RichTextRange SelectedRange { get => TextView.SelectedRange; set => TextView.SelectedRange = value; }
    /// <summary>Gets or sets the anchor and active offsets.</summary>
    public RichTextSelectionState SelectionState { get => TextView.SelectionState; set => TextView.SelectionState = value; }
    /// <summary>Gets the source-only live selection facade.</summary>
    public CodeTextSelection Selection { get; }
    /// <summary>Gets presentation layers. Application layers compose above syntax coloring.</summary>
    public RichTextDecorations Decorations => TextView.Decorations;

    /// <summary>Gets explicit source-range folding. The application owns discovery, UI, and expansion policy.</summary>
    public RichTextFolding Folding => TextView.Folding;
    /// <summary>Gets or sets the token-to-color callback, or null to leave tokens uncolored.</summary>
    public CodeEditorTheme? Theme { get => (CodeEditorTheme?)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }
    /// <summary>Gets or sets the normal text and line-number color, or null for the native default.</summary>
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
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
    /// <summary>Gets or sets the prefix used by the native line-comment shortcut.</summary>
    public string LineCommentPrefix { get => (string)GetValue(LineCommentPrefixProperty); set => SetValue(LineCommentPrefixProperty, value); }
    /// <summary>Gets the logical line count, including a trailing empty line.</summary>
    public int LineCount => Lines.Count;
    /// <summary>Gets the one-based position of the active selection endpoint.</summary>
    public CodePosition CaretPosition => Lines.GetPosition(Math.Min(SelectionState.Active, Lines.Text.Length));
    /// <summary>Gets the last successfully rendered LSP semantic tokens.</summary>
    public IReadOnlyList<SemanticToken> Tokens => _tokens;
    /// <summary>Gets whether an undo unit is available.</summary>
    public bool CanUndo => TextView.CanUndo;
    /// <summary>Gets whether a redo unit is available.</summary>
    public bool CanRedo => TextView.CanRedo;
    /// <summary>Gets the stable MVVM commands.</summary>
    public CodeEditorCommands Commands { get; }

    /// <summary>Gets the text surface's key bindings, including the editable code defaults.</summary>
    public EditorKeyBindingCollection KeyBindings => TextView.KeyBindings;

    /// <summary>Occurs before built-in hardware-key handling, except while native text composition is active.</summary>
    public event EventHandler<EditorKeyEventArgs>? KeyDown;
    /// <summary>Customizes the text surface's native context or selection menu. Apple platforms require version 16 or later.</summary>
    public event EventHandler<EditorContextMenuEventArgs>? ContextMenuOpening;

    /// <summary>Occurs after an atomic source-document change.</summary>
    public event EventHandler<RichTextContentChangedEventArgs>? ContentChanged;
    /// <summary>Occurs after logical source text changes.</summary>
    public event EventHandler<RichTextTextChangedEventArgs>? TextChanged;
    /// <summary>Occurs after the caret or selection changes.</summary>
    public event EventHandler<RichTextSelectionChangedEventArgs>? SelectionChanged;
    /// <summary>Occurs before paste. The accepted fragment is converted to plain text.</summary>
    public event EventHandler<RichTextPastingEventArgs>? Pasting;

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
    /// <summary>Reveals source without changing the selection.</summary>
    /// <param name="range">The source range to reveal.</param>
    public void ScrollIntoView(RichTextRange range) => TextView.ScrollIntoView(range);
    /// <summary>Copies the selection through the native clipboard.</summary>
    /// <returns>The clipboard operation.</returns>
    public Task CopyAsync() => TextView.CopyAsync();
    /// <summary>Cuts the selection through the native clipboard.</summary>
    /// <returns>The clipboard operation.</returns>
    public Task CutAsync() => TextView.CutAsync();
    /// <summary>Pastes clipboard content as plain source text.</summary>
    /// <returns>The clipboard operation.</returns>
    public Task PasteAsync() => TextView.PasteAsync();

    /// <summary>Requests LSP semantic tokens for the current source and updates native syntax colors.</summary>
    /// <param name="cancellationToken">Cancellation for this refresh.</param>
    /// <returns>A task completing after classification and presentation. Call on the UI thread.</returns>
    public Task RefreshHighlightingAsync(CancellationToken cancellationToken = default) => HighlightAsync(TimeSpan.Zero, cancellationToken);

    private async Task HighlightAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        VerifyAccess();
        CancelHighlighting();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _highlightCancellation = cancellation;
        var token = cancellation.Token;
        var document = Document;
        var version = document.Version;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            var languageDocument = await GetLanguageDocumentAsync(token);
            var tokens = languageDocument is null ? Array.Empty<SemanticToken>() : await languageDocument.GetSemanticTokensAsync(token);
            // Always leave a possible document notification before applying formatting.
            await Task.Yield();
            if (token.IsCancellationRequested || !ReferenceEquals(Document, document) || document.Version != version) return;
            if (!ApplyTheme(tokens, document, version, token)) return;
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

    private void RefreshTheme()
    {
        if (NativeAdapter is null || _themeRefreshQueued) return;
        _themeRefreshQueued = true;
        if (!Dispatcher.Dispatch(() =>
        {
            _themeRefreshQueued = false;
            try { ApplyTheme(_tokens, Document, Document.Version); }
            catch (Exception exception) { LanguageServerFailed?.Invoke(this, new(exception)); }
        })) _themeRefreshQueued = false;
    }

    private bool ApplyTheme(IReadOnlyList<SemanticToken> tokens, CodeDocument document, long version, CancellationToken cancellationToken = default)
    {
        if (NativeAdapter?.IsComposing == true) { WaitForComposition(); return false; }
        var theme = Theme;
        var decorations = new List<RichTextDecoration>();
        if (theme is not null)
        {
            foreach (var token in tokens)
            {
                var color = theme(token);
                if (cancellationToken.IsCancellationRequested || !ReferenceEquals(Document, document) || document.Version != version || !ReferenceEquals(Theme, theme)) return false;
                if (color is not null) decorations.Add(new(new(token.Start, token.Length), new() { ForegroundColor = color }));
            }
        }
        _syntaxLayer.Set(decorations);
        return true;
    }

    internal async void ScheduleHighlighting()
    {
        CancelHighlighting();
        _compositionTimer?.Stop();
        _tokens = Array.Empty<SemanticToken>();
        OnPropertyChanged(nameof(Tokens));
        if (NativeAdapter is null) return;
        if (NativeAdapter.IsComposing) { WaitForComposition(); return; }
        try { await HighlightAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None); }
        catch (Exception exception) { LanguageServerFailed?.Invoke(this, new CodeLanguageServerFailedEventArgs(exception)); }
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

    private void SetDocument(CodeDocument document)
    {
        ResetLanguageDocument();
        _document = document;
        TextView.Document = document.Source;
        NativeAdapter?.UpdateConfiguration();
        SynchronizeSelectionProperties();
        UpdateLines();
        ScheduleHighlighting();
    }

    private void SynchronizeSelectionProperties()
    {
        _synchronizingSelection = true;
        try
        {
            SetValue(SelectedRangeProperty, TextView.SelectedRange);
            SetValue(SelectionStateProperty, TextView.SelectionState);
        }
        finally { _synchronizingSelection = false; }
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("CodeEditor must be mutated on its owning UI thread.");
    }

    private void OnContentChanged(object? sender, RichTextContentChangedEventArgs args)
    {
        NativeAdapter?.UpdateConfiguration();
        UpdateLines();
        SynchronizeLanguageDocument();
        ScheduleHighlighting();
        if (args.ChangeSet.Origin == RichTextChangeOrigin.User) QueueNativeAutoIndent(args.ChangeSet);
        ContentChanged?.Invoke(this, args);
        if (args.ChangeSet.IsTextChanged) TextChanged?.Invoke(this, new RichTextTextChangedEventArgs(args.ChangeSet));
    }

    private void OnTextViewPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CanUndo) or nameof(CanRedo))
        {
            OnPropertyChanged(args.PropertyName);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (propertyName == nameof(BackgroundColor))
        {
            if (TextView is { } textView) textView.BackgroundColor = BackgroundColor;
            if (_gutter is { } gutter) gutter.BackgroundColor = BackgroundColor;
        }
        base.OnPropertyChanged(propertyName);
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
        NativeAdapter?.UpdateConfiguration();
        UpdateGutter();
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
            canvas.FontSize = (float)owner.FontSize;
            if (owner.NativeAdapter is not { } handler) return;
            if ((owner.TextColor ?? owner.TextView.TextColor ?? handler.GetTextColor()) is { } color) canvas.FontColor = color;
            foreach (var line in handler.GetVisibleLines())
            {
                canvas.Font = new Microsoft.Maui.Graphics.Font(owner.FontFamily, line.Number == owner.CaretPosition.Line ? 700 : 400);
                canvas.DrawString(line.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, line.Top,
                    (float)owner._gutter.Width - 8, line.Height, HorizontalAlignment.Right, VerticalAlignment.Center);
            }
        }
    }
}

internal readonly record struct VisibleCodeLine(int Number, float Top, float Height);
