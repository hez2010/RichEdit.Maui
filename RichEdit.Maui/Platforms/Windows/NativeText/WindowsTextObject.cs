using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.RichEdit;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal abstract unsafe partial class WindowsTextObject<T>(WindowsTextServices owner, nint pointer) : IDisposable where T : unmanaged
{
    private readonly ApartmentReference _reference = new(owner.Dispatcher, pointer);
    protected WindowsTextServices Owner { get; } = owner;
    internal T* Pointer
    {
        get
        {
            Owner.VerifyAccess();
            ObjectDisposedException.ThrowIf(_reference.IsClosed, this);
            return (T*)_reference.DangerousGetHandle();
        }
    }
    public void Dispose() => _reference.Dispose();

    private sealed partial class ApartmentReference : SafeHandle
    {
        private readonly DispatcherQueue _dispatcher;
        internal ApartmentReference(DispatcherQueue dispatcher, nint value) : base(0, ownsHandle: true)
        {
            _dispatcher = dispatcher;
            SetHandle(value);
        }
        public override bool IsInvalid => handle == 0;
        protected override bool ReleaseHandle()
        {
            var value = handle;
            if (_dispatcher.HasThreadAccess) Marshal.Release(value);
            else
            {
                // Native TOM objects belong to the editing apartment. A range
                // collected by the GC must release its COM reference there too.
                // If that apartment has shut down, its native objects cannot be
                // safely called from the finalizer thread.
                return _dispatcher.TryEnqueue(() => Marshal.Release(value));
            }
            return true;
        }
    }
}

internal static unsafe partial class WindowsTextValues
{
    internal static int Option(int value) => value == (int)tomConstants.tomUndefined ? 0 : value + 1;
    internal static int NativeOption(int value) => value == 0 ? (int)tomConstants.tomUndefined : value - 1;
    internal static FormatEffect Effect(int value) => value switch
    {
        (int)tomConstants.tomFalse => FormatEffect.Off,
        (int)tomConstants.tomTrue => FormatEffect.On,
        (int)tomConstants.tomToggle => FormatEffect.Toggle,
        _ => FormatEffect.Undefined,
    };
    internal static int Effect(FormatEffect value) => value switch
    {
        FormatEffect.Off => (int)tomConstants.tomFalse,
        FormatEffect.On => (int)tomConstants.tomTrue,
        FormatEffect.Toggle => (int)tomConstants.tomToggle,
        FormatEffect.Undefined => (int)tomConstants.tomUndefined,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    internal static string TakeString(BSTR value)
    {
        if (value.Value is null) return "";
        try { return Marshal.PtrToStringBSTR((nint)value.Value); }
        finally { Marshal.FreeBSTR((nint)value.Value); }
    }
    internal static byte[] TakeBytes(BSTR value)
    {
        if (value.Value is null) return [];
        try { return new ReadOnlySpan<byte>(value.Value, checked((int)SysStringByteLen((nint)value.Value))).ToArray(); }
        finally { Marshal.FreeBSTR((nint)value.Value); }
    }
    internal static global::Windows.UI.Color Color(int value) => value < 0
        ? global::Windows.UI.Color.FromArgb(0, 0, 0, 0)
        : global::Windows.UI.Color.FromArgb(255, (byte)value, (byte)(value >> 8), (byte)(value >> 16));
    internal static int Color(global::Windows.UI.Color value) => value.A == 0
        ? (int)tomConstants.tomAutoColor : value.R | value.G << 8 | value.B << 16;

    internal readonly ref struct Text : IDisposable
    {
        private readonly nint _value;
        internal Text(string value) => _value = Marshal.StringToBSTR(value);
        internal BSTR Value => new((char*)_value);
        public void Dispose() => Marshal.FreeBSTR(_value);
    }

    // TOM represents RTF as a byte BSTR, including non-ASCII source bytes and
    // its byte length. It must not pass through the UTF-16 BSTR marshaller.
    internal readonly ref struct Bytes : IDisposable
    {
        private readonly nint _value;
        internal Bytes(ReadOnlySpan<byte> value)
        {
            fixed (byte* source = value) _value = SysAllocStringByteLen(source, (uint)value.Length);
            if (_value == 0) throw new OutOfMemoryException();
        }
        internal BSTR Value => new((char*)_value);
        public void Dispose() => Marshal.FreeBSTR(_value);
    }

    [LibraryImport("oleaut32.dll")] private static partial uint SysStringByteLen(nint value);
    [LibraryImport("oleaut32.dll")] private static partial nint SysAllocStringByteLen(byte* value, uint length);
}
