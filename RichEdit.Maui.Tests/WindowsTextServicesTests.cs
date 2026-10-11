using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using RichEdit.Maui.Platforms.Windows.NativeText;
using Windows.Win32.Foundation;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task NativeTextServicesOwnTextAndReleaseTheirHost() => WindowsTestHost.RunAsync(() =>
    {
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        var site = new NativeTextTestSite(WinRT.Interop.WindowNative.GetWindowHandle(window));
        using var engine = new WindowsTextServices(site);
        SetText(engine, "first\r\nsecond");
        Assert.Equal(2, (int)engine.Send(0xBA)); // EM_GETLINECOUNT
        SetSelection(engine, -1, -1);
        ReplaceSelection(engine, "!");
        Assert.Equal("first\r\nsecond!", GetText(engine));
        engine.Dispose();
        engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.Send(0xE));
    });

    [Fact(Explicit = true)]
    public Task NativeTextServicesRenderDistantLinesInABoundedViewport() => WindowsTestHost.RunAsync(async () =>
    {
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        var site = new NativeTextTestSite(WinRT.Interop.WindowNative.GetWindowHandle(window));
        using var engine = new WindowsTextServices(site);
        SetText(engine, string.Concat(Enumerable.Repeat(new string('x', 105) + "\r\n", 100_000)));
        Assert.Equal(10_700_000, (int)engine.Send(0xE));
        Assert.Equal(100_001, (int)engine.Send(0xBA));
        using var device = new CanvasDevice();
        using var target = new CanvasRenderTarget(device, 1024, 650, 96);
        var previous = 0;
        foreach (var line in new[] { 0, 1000, 50_000, 99_960 })
        {
            engine.Send(0xB6, 0, line - previous); // EM_LINESCROLL
            previous = line;
            Assert.Equal(line, (int)engine.Send(0xCE)); // EM_GETFIRSTVISIBLELINE
            using (var drawing = target.CreateDrawingSession())
            {
                drawing.Clear(Microsoft.UI.Colors.White);
                DrawNativeText(engine, drawing);
            }
            var pixels = target.GetPixelColors(100, 100, 300, 300);
            Assert.True(pixels.Count(color => color.R < 100 && color.G < 100 && color.B < 100) > pixels.Length / 100,
                $"Native text is blank at line {line + 1}.");
        }
        await target.SaveAsync(Path.Combine(AppContext.BaseDirectory, "native-text-services-last-lines.png"), CanvasBitmapFileFormat.Png);
        SetSelection(engine, -1, -1);
        ReplaceSelection(engine, "tail");
        Assert.Equal(100_001, (int)engine.Send(0xBA));
    }, TimeSpan.FromMinutes(2));

    private static unsafe void SetText(WindowsTextServices engine, string value)
    {
        engine.Send(0x435, 0, int.MaxValue); // EM_EXLIMITTEXT
        fixed (char* text = value) engine.Send(0xC, 0, (nint)text);
    }
    private static unsafe void SetSelection(WindowsTextServices engine, int start, int end)
    {
        var range = new global::Windows.Win32.UI.Controls.RichEdit.CHARRANGE { cpMin = start, cpMax = end };
        engine.Send(0x437, 0, (nint)(&range)); // EM_EXSETSEL
    }
    private static unsafe void ReplaceSelection(WindowsTextServices engine, string value)
    {
        fixed (char* text = value) engine.Send(0xC2, 0, (nint)text); // EM_REPLACESEL
    }
    private static unsafe string GetText(WindowsTextServices engine)
    {
        var buffer = new char[(int)engine.Send(0xE) + 1];
        fixed (char* text = buffer)
        {
            var length = (int)engine.Send(0xD, (nuint)buffer.Length, (nint)text);
            return new(text, 0, length);
        }
    }

    // The Win2D SDK publishes ICanvasResourceWrapperNative as a native-only ABI.
    // Keep this test bridge scoped to its drawing session and release both QIs.
    private static unsafe void DrawNativeText(WindowsTextServices engine, CanvasDrawingSession drawing)
    {
        var session = WinRT.MarshalInspectable<CanvasDrawingSession>.FromManaged(drawing);
        nint wrapper = 0, target = 0;
        try
        {
            var iid = new Guid("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(session, in iid, out wrapper));
            var targetId = new Guid("2CD90694-12E2-11DC-9FED-001143A055F9");
            var get = (delegate* unmanaged[Stdcall]<nint, nint, float, Guid*, nint*, int>)(*(nint**)wrapper)[3];
            Marshal.ThrowExceptionForHR(get(wrapper, 0, 96, &targetId, &target));
            engine.Draw(target);
        }
        finally
        {
            if (target != 0) Marshal.Release(target);
            if (wrapper != 0) Marshal.Release(wrapper);
            Marshal.Release(session);
        }
    }

    private sealed class NativeTextTestSite(nint window) : IWindowsTextSite
    {
        public nint Window => window;
        public DispatcherQueue Dispatcher => DispatcherQueue.GetForCurrentThread();
        public RECT Client => new() { right = 1024, bottom = 650 };
        public RECT Inset => default;
        public uint PropertyBits => 1 | 2 | 0x1000000;
        public uint ScrollBars => 0x300000;
        public uint MaximumLength => int.MaxValue;
        public uint EditStyle => 0;
        public uint WindowStyle => 0x50000000 | 0x300000 | 4 | 0x40 | 0x80;
        public int HorizontalExtent => 1024;
        public void Invalidate() { }
        public void SetScrollRange(int bar, int minimum, int maximum) { }
        public void SetScrollPosition(int bar, int position) { }
        public void ShowScrollBar(int bar, bool show) { }
        public void EnableScrollBar(int bar, int arrows) { }
        public bool CreateCaret(nint bitmap, int width, int height) => true;
        public void ShowCaret(bool show) { }
        public void MoveCaret(int x, int y) { }
        public void DestroyCaret() { }
        public void ShowDropCaret(bool show, RECT? bounds) { }
        public void Capture(bool capture) { }
        public void Focus() { }
        public void ScreenToClient(ref POINT point) { }
        public void ClientToScreen(ref POINT point) { }
        public int Notify(uint code, nint data) => 0;
    }
}
