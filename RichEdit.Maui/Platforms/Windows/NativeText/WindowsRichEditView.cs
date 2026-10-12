using ScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode;
using ScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using Border = Microsoft.UI.Xaml.Controls.Border;
using ControlTemplate = Microsoft.UI.Xaml.Controls.ControlTemplate;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

// The canvas is only a presentation surface for the system RichEdit engine.
// RichEdit owns shaping, line breaking, selection, hit testing and input. Its
// client rectangle stays viewport-sized regardless of the document's extent.
internal partial class WindowsRichEditView : UserControl, IWindowsTextSite, IDisposable
{
    private readonly Grid _root = new();
    private readonly FrameworkElement _extent = new Border();
    internal ScrollViewer Scroller { get; } = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollMode = ScrollMode.Enabled,
        ZoomMode = ZoomMode.Disabled,
        IsTabStop = false,
    };
    private readonly CanvasControl _canvas = new() { IsHitTestVisible = false };
    private readonly TextBlock _placeholder = new() { IsHitTestVisible = false };
    private readonly WindowsTextServices _engine;
    internal WindowsTextDocument Document { get; }
    private bool _disposed, _synchronizingScroll, _queuedScroll, _queuedNotifications;
    private bool _textChanged, _formatChanged, _selectionChanged, _raisingNotifications;
    private bool _compositionStarted, _compositionEnded, _compositionActive;
    private int _horizontalMaximum, _verticalMaximum, _horizontalPosition, _verticalPosition;
    private bool _readOnly, _spellCheck = true, _prediction = true, _acceptsReturn = true;
    private int _maximumLength;
    private TextWrapping _wrapping = TextWrapping.Wrap;
    private InputScope _inputScope = new();

    internal WindowsRichEditView()
    {
        IsTabStop = true;
        Scroller.Content = _extent;
        _root.Children.Add(Scroller);
        _root.Children.Add(_canvas);
        _root.Children.Add(_placeholder);
        Content = _root;
        _engine = new(this);
        Document = new(_engine);
        _engine.OperationCompleted += FlushNotifications;
        _engine.Send(EM_SETEVENTMASK, 0, (nint)(ENM_CHANGE | ENM_SELCHANGE | ENM_SCROLL | ENM_REQUESTRESIZE));
        _canvas.Draw += Draw;
        Scroller.ViewChanged += ScrollChanged;
        SizeChanged += (_, _) => ResizeNativeClient();
        Loaded += (_, _) => { ResizeNativeClient(); AttachInputWindow(); };
        Unloaded += (_, _) => DetachInputWindow();
        ActualThemeChanged += (_, _) => UpdateDefaultFormat();
        RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => UpdateDefaultFormat());
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => UpdateDefaultFormat());
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => UpdateDefaultFormat());
        RegisterPropertyChangedCallback(PaddingProperty, (_, _) => ResizeNativeClient());
        UpdateDefaultFormat();
    }

    internal Func<bool>? IsProjectionUpdate { get; set; }
    internal event EventHandler<NativeTextChangingEventArgs>? TextChanging;
    internal event EventHandler? SelectionChanged;
    internal event EventHandler? TextCompositionStarted;
    internal event EventHandler? TextCompositionEnded;

    internal bool AcceptsReturn { get => _acceptsReturn; set => _acceptsReturn = value; }
    internal bool IsReadOnly
    {
        get => _readOnly;
        set { if (_readOnly == value) return; _readOnly = value; _engine.PropertyChanged(TXTBIT_READONLY, value ? TXTBIT_READONLY : 0); }
    }
    internal bool IsSpellCheckEnabled { get => _spellCheck; set { _spellCheck = value; UpdateLanguageServices(); } }
    internal bool IsTextPredictionEnabled { get => _prediction; set { _prediction = value; UpdateLanguageServices(); } }
    internal int MaxLength
    {
        get => _maximumLength;
        set { _maximumLength = value; _engine.PropertyChanged(TXTBIT_MAXLENGTHCHANGE, TXTBIT_MAXLENGTHCHANGE); }
    }
    internal TextWrapping TextWrapping
    {
        get => _wrapping;
        set
        {
            if (_wrapping == value) return;
            _wrapping = value;
            Scroller.HorizontalScrollBarVisibility = value == TextWrapping.NoWrap ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            _engine.PropertyChanged(TXTBIT_WORDWRAP, value == TextWrapping.NoWrap ? 0 : TXTBIT_WORDWRAP);
            ResizeNativeClient();
        }
    }
    internal InputScope InputScope { get => _inputScope; set { _inputScope = value; UpdateLanguageServices(); } }
    internal string PlaceholderText { get => _placeholder.Text; set { _placeholder.Text = value; UpdatePlaceholder(); } }
    internal SolidColorBrush SelectionHighlightColor { get; } = new(Microsoft.UI.Colors.DodgerBlue);

    private void UpdateLanguageServices()
    {
        // RichEdit's TSF store supplies proofing, prediction and composition.
        const uint proofing = 0x00800000;
        _engine.Send(EM_SETEDITSTYLE, _spellCheck ? proofing : 0, (nint)proofing);
        const uint spelling = 0x0800, prediction = 0x1000;
        _engine.Send(EM_SETLANGOPTIONS, 0, (nint)((uint)_engine.Send(EM_GETLANGOPTIONS) & ~(spelling | prediction) | (_spellCheck ? spelling : 0) | (_prediction ? prediction : 0)));
        if (FocusState != FocusState.Unfocused && NativeWindow != 0)
            Marshal.ThrowExceptionForHR(SetInputScope(NativeWindow, (int)(_inputScope.Names.FirstOrDefault()?.NameValue ?? InputScopeNameValue.Default)));
    }

    [WinRT.DynamicWindowsRuntimeCast(typeof(SolidColorBrush))]
    private void UpdateDefaultFormat()
    {
        if (_disposed) return;
        var color = (Foreground as SolidColorBrush)?.Color ?? (ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
        var format = new global::Windows.Win32.UI.Controls.RichEdit.CHARFORMATW
        {
            dwMask = global::Windows.Win32.UI.Controls.RichEdit.CFM_MASK.CFM_FACE |
                global::Windows.Win32.UI.Controls.RichEdit.CFM_MASK.CFM_SIZE | global::Windows.Win32.UI.Controls.RichEdit.CFM_MASK.CFM_COLOR,
            yHeight = (int)Math.Round(FontSize * 15), crTextColor = new((uint)(color.R | color.G << 8 | color.B << 16)),
        };
        (FontFamily?.Source ?? "Segoe UI").AsSpan().Slice(0, Math.Min(31, (FontFamily?.Source ?? "Segoe UI").Length)).CopyTo(format.szFaceName.AsSpan());
        _engine.SetDefaultCharacterFormat(format);
        _placeholder.Foreground = Foreground;
        _canvas.Invalidate();
    }

    private void UpdatePlaceholder() => _placeholder.Visibility = Document.GetRange(0, 0).StoryLength <= 1 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private void ResizeNativeClient()
    {
        if (_disposed) return;
        _placeholder.Margin = Padding;
        _engine.PropertyChanged(TXTBIT_CLIENTRECTCHANGE | TXTBIT_VIEWINSETCHANGE, TXTBIT_CLIENTRECTCHANGE | TXTBIT_VIEWINSETCHANGE);
        _canvas.Invalidate();
    }

    private unsafe void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_disposed || ActualWidth <= 0 || ActualHeight <= 0) return;
        using var wrapper = WindowsCanvasResource.Acquire(args.DrawingSession);
        _engine.Draw(wrapper.Target);
        DrawCaret(args.DrawingSession);
    }

    private void QueueScrollUpdate()
    {
        if (_queuedScroll || _disposed) return;
        _queuedScroll = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _queuedScroll = false;
            if (_disposed) return;
            _synchronizingScroll = true;
            try
            {
                var horizontal = _engine.GetScroll(false);
                var vertical = _engine.GetScroll(true);
                _horizontalMaximum = horizontal.Maximum;
                _verticalMaximum = vertical.Maximum;
                _horizontalPosition = horizontal.Position;
                _verticalPosition = vertical.Position;
                _extent.Width = Math.Max(ActualWidth, horizontal.Maximum + 1 + ActualWidth - horizontal.Page);
                _extent.Height = Math.Max(ActualHeight, vertical.Maximum + 1 + ActualHeight - vertical.Page);
                Scroller.UpdateLayout();
                Scroller.ChangeView(_horizontalPosition, _verticalPosition, null, true);
            }
            finally { _synchronizingScroll = false; }
        });
    }

    private unsafe void ScrollChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_disposed || _synchronizingScroll) return;
        var point = new POINT { x = (int)Math.Round(Scroller.HorizontalOffset), y = (int)Math.Round(Scroller.VerticalOffset) };
        if (point.x == _horizontalPosition && point.y == _verticalPosition) return;
        _engine.Send(EM_SETSCROLLPOS, 0, (nint)(&point));
        _canvas.Invalidate();
    }

    internal unsafe void ScrollBy(double horizontalDelta, double verticalDelta)
    {
        var horizontal = _engine.GetScroll(false);
        var vertical = _engine.GetScroll(true);
        // Native scroll positions are integral client units. Quantize the
        // requested delta, so a half-pixel alignment does not oscillate between
        // two positions while a caller waits for stable geometry.
        var point = new POINT
        {
            x = (int)Math.Clamp(horizontal.Position + Math.Round(horizontalDelta), 0, Math.Max(0, horizontal.Maximum - horizontal.Page + 1)),
            y = (int)Math.Clamp(vertical.Position + Math.Round(verticalDelta), 0, Math.Max(0, vertical.Maximum - vertical.Page + 1)),
        };
        _engine.Send(EM_SETSCROLLPOS, 0, (nint)(&point));
        QueueScrollUpdate();
    }

    private void FlushNotifications()
    {
        if (_disposed || _raisingNotifications) return;
        _raisingNotifications = true;
        try
        {
            var text = _textChanged;
            var format = _formatChanged;
            var selection = _selectionChanged;
            var started = _compositionStarted;
            var ended = _compositionEnded;
            _textChanged = _formatChanged = _selectionChanged = false;
            _compositionStarted = _compositionEnded = false;
            if (started && !_compositionActive) { _compositionActive = true; TextCompositionStarted?.Invoke(this, EventArgs.Empty); }
            if (text || format) { UpdatePlaceholder(); TextChanging?.Invoke(this, new(text)); }
            if (selection) SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (ended && _compositionActive) { _compositionActive = false; TextCompositionEnded?.Invoke(this, EventArgs.Empty); }
        }
        finally { _raisingNotifications = false; }
    }

    nint IWindowsTextSite.Window => NativeWindow;
    DispatcherQueue IWindowsTextSite.Dispatcher => DispatcherQueue;
    RECT IWindowsTextSite.Client => new() { right = Math.Max(1, (int)ActualWidth), bottom = Math.Max(1, (int)ActualHeight) };
    RECT IWindowsTextSite.Inset => new() { left = (int)Math.Round(Padding.Left * 2540 / 96), top = (int)Math.Round(Padding.Top * 2540 / 96), right = (int)Math.Round(Padding.Right * 2540 / 96), bottom = (int)Math.Round(Padding.Bottom * 2540 / 96) };
    uint IWindowsTextSite.PropertyBits => TXTBIT_RICHTEXT | TXTBIT_MULTILINE | TXTBIT_D2DDWRITE | TXTBIT_SAVESELECTION |
        (_wrapping == TextWrapping.NoWrap ? 0 : TXTBIT_WORDWRAP) | (_readOnly ? TXTBIT_READONLY : 0);
    uint IWindowsTextSite.ScrollBars => 0x300000;
    uint IWindowsTextSite.MaximumLength => _maximumLength <= 0 ? int.MaxValue : (uint)_maximumLength;
    uint IWindowsTextSite.EditStyle => 0;
    uint IWindowsTextSite.WindowStyle => 0x50000000 | 0x300000 | 4 | 0x40 | 0x80;
    int IWindowsTextSite.HorizontalExtent => (int)ActualWidth;
    void IWindowsTextSite.Invalidate() { if (!_disposed) { _canvas.Invalidate(); QueueScrollUpdate(); } }
    void IWindowsTextSite.SetScrollRange(int bar, int minimum, int maximum)
    {
        if (bar == 0) _horizontalMaximum = maximum; else if (bar == 1) _verticalMaximum = maximum;
        QueueScrollUpdate();
    }
    void IWindowsTextSite.SetScrollPosition(int bar, int position)
    {
        if (bar == 0) _horizontalPosition = position; else if (bar == 1) _verticalPosition = position;
        QueueScrollUpdate();
    }
    void IWindowsTextSite.ShowScrollBar(int bar, bool show) { }
    void IWindowsTextSite.EnableScrollBar(int bar, int arrows) { }
    void IWindowsTextSite.Focus() => Focus(FocusState.Programmatic);
    unsafe int IWindowsTextSite.Notify(uint code, nint data)
    {
        if (IsProjectionUpdate?.Invoke() != true)
        {
            if (code == EN_CHANGE)
            {
                var change = data == 0 ? global::Windows.Win32.UI.Controls.RichEdit.CHANGETYPE.CN_TEXTCHANGED : ((global::Windows.Win32.UI.Controls.RichEdit.CHANGENOTIFY*)data)->dwChangeType;
                _textChanged |= (change & global::Windows.Win32.UI.Controls.RichEdit.CHANGETYPE.CN_TEXTCHANGED) != 0;
                _formatChanged |= (change & global::Windows.Win32.UI.Controls.RichEdit.CHANGETYPE.CN_TEXTCHANGED) == 0;
            }
            if (code == EN_SELCHANGE) _selectionChanged = true;
        }
        if (code == EN_STARTCOMPOSITION) _compositionStarted = true;
        if (code == EN_ENDCOMPOSITION) _compositionEnded = true;
        if (!_queuedNotifications && (_textChanged || _formatChanged || _selectionChanged || _compositionStarted || _compositionEnded))
        {
            _queuedNotifications = true;
            DispatcherQueue.TryEnqueue(() => { _queuedNotifications = false; FlushNotifications(); });
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DetachInputWindow();
        _engine.OperationCompleted -= FlushNotifications;
        Document.Dispose();
        _engine.Dispose();
        _disposed = true;
        IsTabStop = false;
        _canvas.RemoveFromVisualTree();
    }

    [LibraryImport("msctf.dll")] private static partial int SetInputScope(nint window, int scope);
}

internal sealed class NativeTextChangingEventArgs(bool isContentChanging) : EventArgs
{
    internal bool IsContentChanging { get; } = isContentChanging;
}
