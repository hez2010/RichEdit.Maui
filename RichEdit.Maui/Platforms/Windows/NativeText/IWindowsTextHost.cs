using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

// Public Windows SDK textserv.h callback ABI. Integers retain Win32 BOOL's
// four-byte representation; HRESULT methods retain their status explicitly.
[GeneratedComInterface, Guid("13e670f4-1a5a-11cf-abeb-00aa00b65ea1")]
internal partial interface IWindowsTextHost
{
    [PreserveSig] nint TxGetDC();
    [PreserveSig] int TxReleaseDC(nint dc);
    [PreserveSig] int TxShowScrollBar(int bar, int show);
    [PreserveSig] int TxEnableScrollBar(int flags, int arrows);
    [PreserveSig] int TxSetScrollRange(int bar, int min, int max, int redraw);
    [PreserveSig] int TxSetScrollPos(int bar, int pos, int redraw);
    [PreserveSig] void TxInvalidateRect(nint rect, int erase);
    [PreserveSig] void TxViewChange(int update);
    [PreserveSig] int TxCreateCaret(nint bitmap, int width, int height);
    [PreserveSig] int TxShowCaret(int show);
    [PreserveSig] int TxSetCaretPos(int x, int y);
    [PreserveSig] int TxSetTimer(uint id, uint timeout);
    [PreserveSig] void TxKillTimer(uint id);
    [PreserveSig] void TxScrollWindowEx(int x, int y, nint scroll, nint clip, nint region, nint update, uint flags);
    [PreserveSig] void TxSetCapture(int capture);
    [PreserveSig] void TxSetFocus();
    [PreserveSig] void TxSetCursor(nint cursor, int text);
    [PreserveSig] int TxScreenToClient(ref POINT point);
    [PreserveSig] int TxClientToScreen(ref POINT point);
    [PreserveSig] int TxActivate(out int old);
    [PreserveSig] int TxDeactivate(int state);
    [PreserveSig] int TxGetClientRect(out RECT rect);
    [PreserveSig] int TxGetViewInset(out RECT rect);
    [PreserveSig] int TxGetCharFormat(out nint format);
    [PreserveSig] int TxGetParaFormat(out nint format);
    [PreserveSig] uint TxGetSysColor(int index);
    [PreserveSig] int TxGetBackStyle(out int style);
    [PreserveSig] int TxGetMaxLength(out uint length);
    [PreserveSig] int TxGetScrollBars(out uint flags);
    [PreserveSig] int TxGetPasswordChar(out ushort character);
    [PreserveSig] int TxGetAcceleratorPos(out int position);
    [PreserveSig] int TxGetExtent(out SIZE size);
    [PreserveSig] int OnTxCharFormatChange(nint format);
    [PreserveSig] int OnTxParaFormatChange(nint format);
    [PreserveSig] int TxGetPropertyBits(uint mask, out uint bits);
    [PreserveSig] int TxNotify(uint code, nint data);
    [PreserveSig] nint TxImmGetContext();
    [PreserveSig] void TxImmReleaseContext(nint context);
    [PreserveSig] int TxGetSelectionBarWidth(out int width);
}

[GeneratedComInterface, Guid("13e670f5-1a5a-11cf-abeb-00aa00b65ea1")]
internal partial interface IWindowsTextHost2 : IWindowsTextHost
{
    [PreserveSig] int TxIsDoubleClickPending();
    [PreserveSig] int TxGetWindow(out nint window);
    [PreserveSig] int TxSetForegroundWindow();
    [PreserveSig] nint TxGetPalette();
    [PreserveSig] int TxGetEastAsianFlags(out int flags);
    [PreserveSig] nint TxSetCursor2(nint cursor, int text);
    [PreserveSig] void TxFreeTextServicesNotification();
    [PreserveSig] int TxGetEditStyle(uint item, out uint data);
    [PreserveSig] int TxGetWindowStyles(out uint style, out uint extendedStyle);
    [PreserveSig] int TxShowDropCaret(int show, nint dc, nint rect);
    [PreserveSig] int TxDestroyCaret();
    [PreserveSig] int TxGetHorzExtent(out int extent);
}
