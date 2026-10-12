using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Win32.Foundation;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal partial class WindowsRichEditView
{
    private nint _inputWindow;
    private GCHandle _inputHandle;
    private Microsoft.UI.Xaml.Input.Pointer? _pointer;
    private readonly SubclassProcedure _windowProcedure = WindowProcedure;
    private DispatcherQueueTimer? _caretTimer;
    private bool _caretVisible, _caretPhase;
    [ThreadStatic] private static WindowsRichEditView? _systemCaretOwner;
    private global::Windows.Foundation.Rect _caret;
    private global::Windows.Foundation.Rect? _dropCaret;
    private long _lastClickTime;
    private global::Windows.Foundation.Point _lastClick;

    private nint NativeWindow => XamlRoot is { } root ? Microsoft.UI.Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId) : 0;

    private void AttachInputWindow()
    {
        if (_disposed || _inputWindow != 0 || NativeWindow == 0) return;
        _inputWindow = NativeWindow;
        _inputHandle = GCHandle.Alloc(this);
        var identity = (nuint)GCHandle.ToIntPtr(_inputHandle);
        if (!SetWindowSubclass(_inputWindow, _windowProcedure, identity, identity))
        {
            _inputHandle.Free();
            _inputWindow = 0;
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        CharacterReceived += OnCharacterReceived;
        if (FocusState != FocusState.Unfocused) _engine.SetFocus(true);
    }

    private void DetachInputWindow()
    {
        if (_inputWindow == 0) return;
        _engine.SetFocus(false);
        CharacterReceived -= OnCharacterReceived;
        RemoveWindowSubclass(_inputWindow, _windowProcedure, (nuint)GCHandle.ToIntPtr(_inputHandle));
        _inputHandle.Free();
        _inputWindow = 0;
        _caretTimer?.Stop();
    }

    protected override void OnGotFocus(RoutedEventArgs args)
    {
        base.OnGotFocus(args);
        if (_disposed) return;
        AttachInputWindow();
        _engine.SetFocus(true);
        UpdateLanguageServices();
    }

    protected override void OnLostFocus(RoutedEventArgs args)
    {
        if (!_disposed) _engine.SetFocus(false);
        base.OnLostFocus(args);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        base.OnKeyDown(args);
        if (_disposed || args.Handled || !_acceptsReturn && args.Key == global::Windows.System.VirtualKey.Enter) return;
        _engine.Send(0x100, (uint)args.Key, KeyMessageData(args.KeyStatus));
        args.Handled = args.Key is global::Windows.System.VirtualKey.Left or global::Windows.System.VirtualKey.Right or
            global::Windows.System.VirtualKey.Up or global::Windows.System.VirtualKey.Down or
            global::Windows.System.VirtualKey.Home or global::Windows.System.VirtualKey.End or
            global::Windows.System.VirtualKey.PageUp or global::Windows.System.VirtualKey.PageDown or
            global::Windows.System.VirtualKey.Delete or global::Windows.System.VirtualKey.Back;
    }

    protected override void OnKeyUp(KeyRoutedEventArgs args)
    {
        base.OnKeyUp(args);
        if (!_disposed && !args.Handled) _engine.Send(0x101, (uint)args.Key, KeyMessageData(args.KeyStatus));
    }

    private static nint KeyMessageData(global::Windows.UI.Core.CorePhysicalKeyStatus status) =>
        (nint)(status.RepeatCount | status.ScanCode << 16 | (status.IsExtendedKey ? 1u << 24 : 0) |
            (status.IsMenuKeyDown ? 1u << 29 : 0) | (status.WasKeyDown ? 1u << 30 : 0) | (status.IsKeyReleased ? 1u << 31 : 0));

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (_disposed || args.Handled || !_acceptsReturn && args.Character == '\r') return;
        // C0 shortcut characters belong to the handler's canonical commands.
        if (args.Character < 32 && args.Character is not ('\r' or '\t' or '\b')) return;
        _engine.Send(0x102, args.Character, KeyMessageData(args.KeyStatus));
        args.Handled = true;
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs args)
    {
        base.OnPointerPressed(args);
        if (_disposed || args.Handled) return;
        _pointer = args.Pointer;
        var point = args.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus(FocusState.Pointer);
        var now = Environment.TickCount64;
        var isDouble = now - _lastClickTime <= GetDoubleClickTime() &&
            Math.Abs(point.Position.X - _lastClick.X) <= GetSystemMetrics(36) &&
            Math.Abs(point.Position.Y - _lastClick.Y) <= GetSystemMetrics(37);
        _lastClickTime = isDouble ? 0 : now;
        _lastClick = point.Position;
        _engine.Send(isDouble ? 0x203u : 0x201u, MouseFlags(args), MousePosition(point.Position));
        args.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs args)
    {
        base.OnPointerMoved(args);
        if (_disposed || args.Handled) return;
        _engine.Send(0x200, MouseFlags(args), MousePosition(args.GetCurrentPoint(this).Position));
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) args.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs args)
    {
        base.OnPointerReleased(args);
        if (_disposed || args.Handled) return;
        _engine.Send(0x202, MouseFlags(args), MousePosition(args.GetCurrentPoint(this).Position));
        _pointer = null;
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs args)
    {
        _pointer = null;
        if (!_disposed) _engine.Send(0x215);
        base.OnPointerCaptureLost(args);
    }

    private nuint MouseFlags(PointerRoutedEventArgs args)
    {
        var properties = args.GetCurrentPoint(this).Properties;
        return (nuint)((properties.IsLeftButtonPressed ? 1 : 0) | (properties.IsRightButtonPressed ? 2 : 0) |
            ((GetKeyState(0x10) & 0x8000) != 0 ? 4 : 0) | ((GetKeyState(0x11) & 0x8000) != 0 ? 8 : 0));
    }
    private static nint MousePosition(global::Windows.Foundation.Point point) => unchecked((nint)((ushort)(short)point.X | (uint)(ushort)(short)point.Y << 16));

    void IWindowsTextSite.Capture(bool capture)
    {
        if (_pointer is not { } pointer) return;
        if (capture) CapturePointer(pointer); else ReleasePointerCapture(pointer);
    }
    bool IWindowsTextSite.CreateCaret(nint bitmap, int width, int height)
    {
        _caret = new(_caret.X, _caret.Y, Math.Max(1, width), Math.Max(1, height));
        if (NativeWindow != 0 && FocusState != FocusState.Unfocused)
        {
            var scale = XamlRoot?.RasterizationScale ?? 1;
            CreateCaret(NativeWindow, bitmap, (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
            _systemCaretOwner = this;
        }
        return true;
    }
    void IWindowsTextSite.ShowCaret(bool show)
    {
        _caretVisible = show;
        _caretPhase = true;
        _caretTimer?.Stop();
        var interval = GetCaretBlinkTime();
        if (show && interval is > 0 and < uint.MaxValue)
        {
            if (_caretTimer is null)
            {
                _caretTimer = DispatcherQueue.CreateTimer();
                _caretTimer.Tick += (_, _) => { _caretPhase = !_caretPhase; _canvas.Invalidate(); };
            }
            _caretTimer.Interval = TimeSpan.FromMilliseconds(interval);
            _caretTimer.Start();
        }
        _canvas.Invalidate();
    }
    void IWindowsTextSite.MoveCaret(int x, int y)
    {
        _caret = new(x, y, _caret.Width, _caret.Height);
        _caretPhase = true;
        if (ReferenceEquals(_systemCaretOwner, this) && XamlRoot is { } root)
        {
            var point = TransformToVisual(root.Content).TransformPoint(new(x, y));
            SetCaretPos((int)Math.Round(point.X * root.RasterizationScale), (int)Math.Round(point.Y * root.RasterizationScale));
        }
        _canvas.Invalidate();
    }
    void IWindowsTextSite.DestroyCaret()
    {
        _caretVisible = false; _caretTimer?.Stop();
        if (ReferenceEquals(_systemCaretOwner, this)) { DestroyCaret(); _systemCaretOwner = null; }
    }
    void IWindowsTextSite.ShowDropCaret(bool show, RECT? bounds)
    {
        _dropCaret = show && bounds is { } rect ? new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top) : null;
        _canvas.Invalidate();
    }
    internal bool HideVirtualCaret { get; set; }

    [WinRT.DynamicWindowsRuntimeCast(typeof(SolidColorBrush))]
    private void DrawCaret(CanvasDrawingSession drawing)
    {
        var color = (Foreground as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Black;
        if (!HideVirtualCaret && _caretVisible && _caretPhase && FocusState != FocusState.Unfocused) drawing.FillRectangle(_caret, color);
        if (_dropCaret is { } bounds) drawing.FillRectangle(bounds, color);
    }
    void IWindowsTextSite.ClientToScreen(ref POINT point)
    {
        if (XamlRoot is not { } root) return;
        var transformed = TransformToVisual(root.Content).TransformPoint(new(point.x, point.y));
        point.x = (int)Math.Round(transformed.X * root.RasterizationScale);
        point.y = (int)Math.Round(transformed.Y * root.RasterizationScale);
        ClientToScreen(NativeWindow, ref point);
    }
    void IWindowsTextSite.ScreenToClient(ref POINT point)
    {
        if (XamlRoot is not { } root) return;
        ScreenToClient(NativeWindow, ref point);
        var transformed = root.Content.TransformToVisual(this).TransformPoint(new(point.x / root.RasterizationScale, point.y / root.RasterizationScale));
        point.x = (int)Math.Round(transformed.X); point.y = (int)Math.Round(transformed.Y);
    }

    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var view = (WindowsRichEditView)GCHandle.FromIntPtr((nint)data).Target!;
        if (!view._disposed && view.FocusState != FocusState.Unfocused &&
            (message is >= 0x10d and <= 0x10f or >= 0x281 and <= 0x291 or 0x51))
        {
            using var operation = view._engine.BeginOperation();
            if (message == 0x10d) view._compositionStarted = true;
            var result = view._engine.Send(message, wParam, lParam);
            if (message == 0x10e) view._compositionEnded = true;
            return result;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProcedure callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure callback, nuint id);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial int CreateCaret(nint window, nint bitmap, int width, int height);
    [LibraryImport("user32.dll")] private static partial int DestroyCaret();
    [LibraryImport("user32.dll")] private static partial int SetCaretPos(int x, int y);
    [LibraryImport("user32.dll")] private static partial uint GetCaretBlinkTime();
    [LibraryImport("user32.dll")] private static partial uint GetDoubleClickTime();
    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] private static partial short GetKeyState(int key);
    [LibraryImport("user32.dll")] private static partial int ClientToScreen(nint window, ref POINT point);
    [LibraryImport("user32.dll")] private static partial int ScreenToClient(nint window, ref POINT point);
}
