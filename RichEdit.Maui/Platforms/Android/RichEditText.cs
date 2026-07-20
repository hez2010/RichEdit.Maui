using Android.Content;
using Android.Runtime;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Views.InputMethods;
using AndroidX.AppCompat.Widget;

namespace RichEdit.Maui.Platforms.Android;

/// <summary>Provides data for a native Android selection change.</summary>
public sealed class NativeSelectionChangedEventArgs(int start, int end) : EventArgs
{
    /// <summary>Gets the native selection start.</summary>
    public int Start { get; } = start;

    /// <summary>Gets the native selection end.</summary>
    public int End { get; } = end;
}

/// <summary>
/// Provides the Android editable-text surface used by <see cref="RichEditorHandler"/>.
/// </summary>
public class RichEditText : AppCompatEditText
{
    private float _pointerDownX;
    private float _pointerDownY;

    internal Func<Task>? PasteRequested { get; set; }

    internal Action? UndoRequested { get; set; }

    internal Action? RedoRequested { get; set; }

    internal Func<string, bool>? LinkInvoked { get; set; }

    internal Func<int, bool>? InlineObjectInvoked { get; set; }

    /// <summary>Initializes the native editor.</summary>
    /// <param name="context">The Android context.</param>
    public RichEditText(Context context) : base(context)
    {
    }

    /// <summary>Initializes a managed wrapper for an existing Java peer.</summary>
    /// <param name="javaReference">The Java peer handle.</param>
    /// <param name="transfer">The handle-ownership transfer policy.</param>
    protected RichEditText(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    /// <summary>Occurs after the native selection changes.</summary>
    public event EventHandler<NativeSelectionChangedEventArgs>? NativeSelectionChanged;

    /// <summary>Occurs when the input method reports an editing action.</summary>
    public event EventHandler? EditingCompleted;

    /// <summary>Gets or sets whether a hardware Tab key may insert a tab.</summary>
    public bool AcceptsTab { get; set; }

    /// <inheritdoc />
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (e?.IsCtrlPressed == true && keyCode == Keycode.Z && UndoRequested is { } undo)
        {
            undo();
            return true;
        }

        if (e?.IsCtrlPressed == true && keyCode == Keycode.Y && RedoRequested is { } redo)
        {
            redo();
            return true;
        }

        if (keyCode == Keycode.Tab && !AcceptsTab)
        {
            return false;
        }

        return base.OnKeyDown(keyCode, e);
    }

    /// <inheritdoc />
    public override bool OnTextContextMenuItem(int id)
    {
        var isPaste = id == global::Android.Resource.Id.Paste ||
            id == global::Android.Resource.Id.PasteAsPlainText;
        if (isPaste && PasteRequested is { } pasteRequested)
        {
            _ = pasteRequested();
            return true;
        }

        return base.OnTextContextMenuItem(id);
    }

    /// <inheritdoc />
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return base.OnTouchEvent(e);
        }

        if (e.ActionMasked == MotionEventActions.Down)
        {
            _pointerDownX = e.GetX();
            _pointerDownY = e.GetY();
        }

        var handled = base.OnTouchEvent(e);
        if (e.ActionMasked != MotionEventActions.Up)
        {
            return handled;
        }

        var touchSlop = ViewConfiguration.Get(Context!)?.ScaledTouchSlop ?? 0;
        if (Math.Abs(e.GetX() - _pointerDownX) > touchSlop ||
            Math.Abs(e.GetY() - _pointerDownY) > touchSlop ||
            e.EventTime - e.DownTime > ViewConfiguration.LongPressTimeout)
        {
            return handled;
        }

        var offset = GetTextOffset(e.GetX(), e.GetY());
        if (offset < 0 || TextFormatted is not ISpanned spanned)
        {
            return handled;
        }

        if (spanned.Length() == 0)
        {
            return handled;
        }

        offset = Math.Min(offset, spanned.Length() - 1);
        var queryEnd = offset + 1;
        var imageSpan = spanned.GetSpans(offset, queryEnd, Java.Lang.Class.FromType(typeof(ImageSpan)))
            ?.OfType<ImageSpan>()
            .FirstOrDefault();
        if (imageSpan is not null && InlineObjectInvoked is { } inlineInvoked)
        {
            var position = spanned.GetSpanStart(imageSpan);
            _ = inlineInvoked(position);
            return true;
        }

        var linkSpan = spanned.GetSpans(offset, queryEnd, Java.Lang.Class.FromType(typeof(URLSpan)))
            ?.OfType<URLSpan>()
            .FirstOrDefault();
        if (linkSpan is not null && LinkInvoked is { } linkInvoked)
        {
            if (linkInvoked(linkSpan.URL ?? string.Empty))
            {
                linkSpan.OnClick(this);
            }

            return true;
        }

        return handled;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PasteRequested = null;
            UndoRequested = null;
            RedoRequested = null;
            LinkInvoked = null;
            InlineObjectInvoked = null;
        }

        base.Dispose(disposing);
    }

    private int GetTextOffset(float x, float y)
    {
        var layout = Layout;
        if (layout is null)
        {
            return -1;
        }

        var localX = x - TotalPaddingLeft + ScrollX;
        var localY = y - TotalPaddingTop + ScrollY;
        if (localX < 0 || localY < 0 || localY > layout.Height)
        {
            return -1;
        }

        var line = layout.GetLineForVertical((int)localY);
        return layout.GetOffsetForHorizontal(line, localX);
    }

    /// <inheritdoc />
    public override void OnEditorAction(ImeAction actionCode)
    {
        base.OnEditorAction(actionCode);
        if (actionCode != ImeAction.None)
        {
            EditingCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged(int selectionStart, int selectionEnd)
    {
        base.OnSelectionChanged(selectionStart, selectionEnd);
        NativeSelectionChanged?.Invoke(this, new NativeSelectionChangedEventArgs(selectionStart, selectionEnd));
    }
}
