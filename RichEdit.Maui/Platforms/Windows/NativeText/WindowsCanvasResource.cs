using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

// Native-only Win2D interop, declared by Microsoft.Graphics.Canvas.native.h.
// The target is borrowed from this drawing session and never retained across frames.
internal sealed unsafe partial class WindowsCanvasResource : IDisposable
{
    internal nint Target { get; private set; }
    private WindowsCanvasResource(nint target) => Target = target;
    internal static WindowsCanvasResource Acquire(CanvasDrawingSession session)
    {
        var inspectable = WinRT.MarshalInspectable<CanvasDrawingSession>.FromManaged(session);
        nint wrapper = 0;
        try
        {
            var iid = new Guid("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(inspectable, in iid, out wrapper));
            var targetId = new Guid("2CD90694-12E2-11DC-9FED-001143A055F9");
            nint target;
            var table = *(ResourceVTable**)wrapper;
            Marshal.ThrowExceptionForHR(table->GetNativeResource(wrapper, 0, 96, &targetId, &target));
            return new(target);
        }
        finally
        {
            if (wrapper != 0) Marshal.Release(wrapper);
            Marshal.Release(inspectable);
        }
    }
    public void Dispose() { if (Target != 0) Marshal.Release(Target); Target = 0; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResourceVTable
    {
        internal nint QueryInterface, AddRef, Release;
        internal delegate* unmanaged[Stdcall]<nint, nint, float, Guid*, nint*, int> GetNativeResource;
    }
}
