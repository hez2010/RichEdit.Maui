using Android.Content;
using Android.Runtime;
using AndroidX.AppCompat.Widget;

namespace RichEdit.Maui.Platforms.Android;

public sealed class NativeSelectionChangedEventArgs(int start, int end) : EventArgs
{
    public int Start { get; } = start;

    public int End { get; } = end;
}

public class RichEditText : AppCompatEditText
{
    public RichEditText(Context context) : base(context)
    {
    }

    protected RichEditText(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public event EventHandler<NativeSelectionChangedEventArgs>? NativeSelectionChanged;

    protected override void OnSelectionChanged(int selectionStart, int selectionEnd)
    {
        base.OnSelectionChanged(selectionStart, selectionEnd);
        NativeSelectionChanged?.Invoke(this, new NativeSelectionChangedEventArgs(selectionStart, selectionEnd));
    }
}
