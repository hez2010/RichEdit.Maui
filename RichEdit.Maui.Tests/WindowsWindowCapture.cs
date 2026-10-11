using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace RichEdit.Maui.Tests;

// Captures only a test-owned window, including native text surfaces that
// RenderTargetBitmap omits. No input injection or foreground activation.
internal static class WindowsWindowCapture
{
    internal static Task SaveAsync(Microsoft.UI.Xaml.Window window, string path) => SaveAsync(WinRT.Interop.WindowNative.GetWindowHandle(window), path);

    internal static async Task SaveAsync(nint window, string path)
    {
        GetWindowThreadProcessId(window, out var process);
        if (process != Environment.ProcessId) throw new ArgumentException("Capture is restricted to this test process's windows.", nameof(window));
        var item = CreateItem(window);
        using var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var ready = new TaskCompletionSource<SoftwareBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        async void OnFrame(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null || Interlocked.Exchange(ref started, 1) != 0) return;
                var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                if (!ready.TrySetResult(bitmap)) bitmap.Dispose();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }
        pool.FrameArrived += OnFrame;
        try
        {
            session.StartCapture();
            using var bitmap = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            File.WriteAllBytes(path, bytes);
        }
        finally { pool.FrameArrived -= OnFrame; ready.TrySetCanceled(); }
    }

    internal static async Task<double> BrightPixelFractionAsync(string path, Microsoft.Maui.Graphics.Rect fraction)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();
        var bright = 0;
        var total = 0;
        for (var y = (int)(decoder.PixelHeight * fraction.Top); y < decoder.PixelHeight * fraction.Bottom; y++)
            for (var x = (int)(decoder.PixelWidth * fraction.Left); x < decoder.PixelWidth * fraction.Right; x++)
            {
                var offset = checked((int)((y * decoder.PixelWidth + x) * 4));
                if (pixels[offset] > 180 && pixels[offset + 1] > 180 && pixels[offset + 2] > 180) bright++;
                total++;
            }
        return total == 0 ? 0 : (double)bright / total;
    }

    private static unsafe GraphicsCaptureItem CreateItem(nint window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var text));
        var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        nint factory = 0, item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, in interopId, out factory));
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
            Marshal.ThrowExceptionForHR(create(factory, window, &itemId, &item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(text);
        }
    }

    private static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var native, out _, out var context));
        nint dxgi = 0, projected = 0;
        try
        {
            var id = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in id, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(projected);
        }
        finally
        {
            if (projected != 0) Marshal.Release(projected);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            Marshal.Release(native);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string value, int length, out nint text);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint text);
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint levels, uint count,
        uint version, out nint device, out int level, out nint context);
    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
