using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.UI.Dispatching;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.UI.Controls.RichEdit;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal interface IWindowsTextSite
{
    nint Window { get; }
    DispatcherQueue Dispatcher { get; }
    RECT Client { get; }
    RECT Inset { get; }
    uint PropertyBits { get; }
    uint ScrollBars { get; }
    uint MaximumLength { get; }
    uint EditStyle { get; }
    uint WindowStyle { get; }
    int HorizontalExtent { get; }
    void Invalidate();
    void SetScrollRange(int bar, int minimum, int maximum);
    void SetScrollPosition(int bar, int position);
    void ShowScrollBar(int bar, bool show);
    void EnableScrollBar(int bar, int arrows);
    bool CreateCaret(nint bitmap, int width, int height);
    void ShowCaret(bool show);
    void MoveCaret(int x, int y);
    void DestroyCaret();
    void ShowDropCaret(bool show, RECT? bounds);
    void Capture(bool capture);
    void Focus();
    void ScreenToClient(ref POINT point);
    void ClientToScreen(ref POINT point);
    int Notify(uint code, nint data);
}

// One native engine owns document layout, shaping, input, selection and drawing.
// The site supplies its bounded viewport and native window services. In
// particular, the render target never uses a document-sized coordinate space.
[GeneratedComClass]
internal sealed unsafe partial class WindowsTextServices : IWindowsTextHost2, IDisposable
{
    private readonly IWindowsTextSite _site;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<uint, DispatcherQueueTimer> _timers = [];
    private readonly CHARFORMATW* _character;
    private readonly PARAFORMAT* _paragraph;
    private ITextServices2* _services;
    private bool _disposed;

    internal WindowsTextServices(IWindowsTextSite site)
    {
        _site = site;
        _character = (CHARFORMATW*)NativeMemory.AllocZeroed((nuint)sizeof(CHARFORMATW));
        _paragraph = (PARAFORMAT*)NativeMemory.AllocZeroed((nuint)sizeof(PARAFORMAT));
        _character->cbSize = (uint)sizeof(CHARFORMATW);
        _character->dwMask = CFM_MASK.CFM_FACE | CFM_MASK.CFM_SIZE | CFM_MASK.CFM_COLOR;
        _character->yHeight = 240;
        "Segoe UI".AsSpan().CopyTo(_character->szFaceName.AsSpan());
        _paragraph->cbSize = (uint)sizeof(PARAFORMAT);
        _paragraph->dwMask = PARAFORMAT_MASK.PFM_ALIGNMENT;
        _paragraph->wAlignment = PARAFORMAT_ALIGNMENT.PFA_LEFT;
        var host = ComInterfaceMarshaller<IWindowsTextHost2>.ConvertToUnmanaged(this);
        nint unknown = 0;
        try
        {
            Marshal.ThrowExceptionForHR(CreateTextServices(0, (nint)host, out unknown));
            var iid = new Guid("8d33f741-cf58-11ce-a89d-00aa006cadc5");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var services));
            _services = (ITextServices2*)services;
            var rect = _site.Client;
            _services->OnTxInPlaceActivate(&rect);
        }
        catch { Dispose(); throw; }
        finally
        {
            if (unknown != 0) Marshal.Release(unknown);
            ComInterfaceMarshaller<IWindowsTextHost2>.Free(host);
        }
    }

    internal nint Send(uint message, nuint wParam = 0, nint lParam = 0)
    {
        VerifyAccess();
        LRESULT result;
        _services->TxSendMessage(message, new(wParam), new(lParam), &result);
        return result.Value;
    }

    internal void PropertyChanged(uint mask, uint bits)
    {
        VerifyAccess();
        _services->OnTxPropertyBitsChange(mask, bits);
    }

    internal void SetDefaultCharacterFormat(CHARFORMATW format)
    {
        VerifyAccess();
        format.cbSize = (uint)sizeof(CHARFORMATW);
        *_character = format;
        PropertyChanged(global::Windows.Win32.PInvoke.TXTBIT_CHARFORMATCHANGE, global::Windows.Win32.PInvoke.TXTBIT_CHARFORMATCHANGE);
    }

    internal void Draw(nint renderTarget)
    {
        VerifyAccess();
        var client = _site.Client;
        var bounds = new RECTL { left = client.left, top = client.top, right = client.right, bottom = client.bottom };
        _services->TxDrawD2D((ID2D1RenderTarget*)renderTarget, &bounds, &client, 1);
    }

    // The returned reference is owned by the caller, as with ordinary QueryInterface.
    internal nint QueryInterface(in Guid iid)
    {
        VerifyAccess();
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface((nint)_services, in iid, out var result));
        return result;
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Native text services belong to their UI thread.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        VerifyAccess();
        foreach (var timer in _timers.Values) timer.Stop();
        _timers.Clear();
        var shutdown = 0;
        if (_services is not null)
        {
            shutdown = ShutdownTextServices((nint)_services);
            _services->Release();
            _services = null;
        }
        _site.DestroyCaret();
        NativeMemory.Free(_character);
        NativeMemory.Free(_paragraph);
        _disposed = true;
        Marshal.ThrowExceptionForHR(shutdown);
    }

    nint IWindowsTextHost.TxGetDC() => GetDC(_site.Window);
    int IWindowsTextHost.TxReleaseDC(nint dc) => ReleaseDC(_site.Window, dc);
    int IWindowsTextHost.TxShowScrollBar(int bar, int show) { _site.ShowScrollBar(bar, show != 0); return 1; }
    int IWindowsTextHost.TxEnableScrollBar(int flags, int arrows) { _site.EnableScrollBar(flags, arrows); return 1; }
    int IWindowsTextHost.TxSetScrollRange(int bar, int min, int max, int redraw) { _site.SetScrollRange(bar, min, max); return 1; }
    int IWindowsTextHost.TxSetScrollPos(int bar, int pos, int redraw) { _site.SetScrollPosition(bar, pos); return 1; }
    void IWindowsTextHost.TxInvalidateRect(nint rect, int erase) => _site.Invalidate();
    void IWindowsTextHost.TxViewChange(int update) => _site.Invalidate();
    int IWindowsTextHost.TxCreateCaret(nint bitmap, int width, int height) => _site.CreateCaret(bitmap, width, height) ? 1 : 0;
    int IWindowsTextHost.TxShowCaret(int show) { _site.ShowCaret(show != 0); return 1; }
    int IWindowsTextHost.TxSetCaretPos(int x, int y) { _site.MoveCaret(x, y); return 1; }
    int IWindowsTextHost.TxSetTimer(uint id, uint timeout)
    {
        ((IWindowsTextHost)this).TxKillTimer(id);
        var timer = _site.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, timeout));
        timer.Tick += (_, _) => { if (!_disposed && _services is not null) Send(0x113, id); };
        _timers.Add(id, timer);
        timer.Start();
        return 1;
    }
    void IWindowsTextHost.TxKillTimer(uint id) { if (_timers.Remove(id, out var timer)) timer.Stop(); }
    void IWindowsTextHost.TxScrollWindowEx(int x, int y, nint scroll, nint clip, nint region, nint update, uint flags) => _site.Invalidate();
    void IWindowsTextHost.TxSetCapture(int capture) => _site.Capture(capture != 0);
    void IWindowsTextHost.TxSetFocus() => _site.Focus();
    void IWindowsTextHost.TxSetCursor(nint cursor, int text) => SetCursor(cursor);
    int IWindowsTextHost.TxScreenToClient(ref POINT point) { _site.ScreenToClient(ref point); return 1; }
    int IWindowsTextHost.TxClientToScreen(ref POINT point) { _site.ClientToScreen(ref point); return 1; }
    int IWindowsTextHost.TxActivate(out int old) { old = 0; return 0; }
    int IWindowsTextHost.TxDeactivate(int state) => 0;
    int IWindowsTextHost.TxGetClientRect(out RECT rect) { rect = _site.Client; return 0; }
    int IWindowsTextHost.TxGetViewInset(out RECT rect) { rect = _site.Inset; return 0; }
    int IWindowsTextHost.TxGetCharFormat(out nint format) { format = (nint)_character; return 0; }
    int IWindowsTextHost.TxGetParaFormat(out nint format) { format = (nint)_paragraph; return 0; }
    uint IWindowsTextHost.TxGetSysColor(int index) => GetSysColor(index);
    int IWindowsTextHost.TxGetBackStyle(out int style) { style = 0; return 0; }
    int IWindowsTextHost.TxGetMaxLength(out uint length) { length = _site.MaximumLength; return 0; }
    int IWindowsTextHost.TxGetScrollBars(out uint flags) { flags = _site.ScrollBars; return 0; }
    int IWindowsTextHost.TxGetPasswordChar(out ushort character) { character = 0; return 0; }
    int IWindowsTextHost.TxGetAcceleratorPos(out int position) { position = -1; return 0; }
    int IWindowsTextHost.TxGetExtent(out SIZE size)
    {
        var rect = _site.Client;
        size = new() { cx = (rect.right - rect.left) * 2540 / 96, cy = (rect.bottom - rect.top) * 2540 / 96 };
        return 0;
    }
    int IWindowsTextHost.OnTxCharFormatChange(nint format) { _site.Invalidate(); return 0; }
    int IWindowsTextHost.OnTxParaFormatChange(nint format) { _site.Invalidate(); return 0; }
    int IWindowsTextHost.TxGetPropertyBits(uint mask, out uint bits) { bits = _site.PropertyBits & mask; return 0; }
    int IWindowsTextHost.TxNotify(uint code, nint data) => _site.Notify(code, data);
    nint IWindowsTextHost.TxImmGetContext() => ImmGetContext(_site.Window);
    void IWindowsTextHost.TxImmReleaseContext(nint context) => ImmReleaseContext(_site.Window, context);
    int IWindowsTextHost.TxGetSelectionBarWidth(out int width) { width = 0; return 0; }
    int IWindowsTextHost2.TxIsDoubleClickPending() => 0;
    int IWindowsTextHost2.TxGetWindow(out nint window) { window = _site.Window; return window == 0 ? unchecked((int)0x80004005) : 0; }
    int IWindowsTextHost2.TxSetForegroundWindow() { _site.Focus(); return 0; }
    nint IWindowsTextHost2.TxGetPalette() => 0;
    int IWindowsTextHost2.TxGetEastAsianFlags(out int flags) { flags = 0; return 0; }
    nint IWindowsTextHost2.TxSetCursor2(nint cursor, int text) => SetCursor(cursor);
    void IWindowsTextHost2.TxFreeTextServicesNotification() { }
    int IWindowsTextHost2.TxGetEditStyle(uint item, out uint data) { data = _site.EditStyle & item; return 0; }
    int IWindowsTextHost2.TxGetWindowStyles(out uint style, out uint extendedStyle) { style = _site.WindowStyle; extendedStyle = 0; return 0; }
    int IWindowsTextHost2.TxShowDropCaret(int show, nint dc, nint rect) { _site.ShowDropCaret(show != 0, rect == 0 ? null : *(RECT*)rect); return 0; }
    int IWindowsTextHost2.TxDestroyCaret() { _site.DestroyCaret(); return 0; }
    int IWindowsTextHost2.TxGetHorzExtent(out int extent) { extent = _site.HorizontalExtent; return 0; }

    [LibraryImport("msftedit.dll")] private static partial int CreateTextServices(nint outer, nint host, out nint services);
    [LibraryImport("msftedit.dll")] private static partial int ShutdownTextServices(nint services);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("user32.dll")] private static partial uint GetSysColor(int index);
    [LibraryImport("user32.dll")] private static partial nint SetCursor(nint cursor);
    [LibraryImport("imm32.dll")] private static partial nint ImmGetContext(nint window);
    [LibraryImport("imm32.dll")] private static partial int ImmReleaseContext(nint window, nint context);
}
