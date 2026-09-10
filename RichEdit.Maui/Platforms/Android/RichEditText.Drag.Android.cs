using Android.Content;
using Android.Views;

namespace RichEdit.Maui.Platforms.Android;

public partial class RichEditText
{
    private RichTextRange? _sourceDragCandidate;
    private SourceDragState? _sourceDrag;

    private sealed class SourceDragState(RichTextDocument document, RichTextRange range) : Java.Lang.Object
    {
        internal RichTextDocument Document { get; } = document;
        internal RichTextRevision Revision { get; } = document.Revision;
        internal RichTextRange Range { get; } = range;
        internal RichTextDocumentFragment Fragment { get; } = RichTextDocumentFragment.FromRange(document.CurrentSnapshot, range);
    }

    private bool ObserveSourceDrag(MotionEvent e)
    {
        if (ProjectionHandler is not { NativeProjection.IsEmpty: false } handler) { _sourceDragCandidate = null; return false; }
        if (e.ActionMasked == MotionEventActions.Down)
        {
            var range = handler.VirtualView.SelectedRange;
            var position = handler.NativeProjection.ToSource(GetOffsetForPosition(e.GetX(), e.GetY()));
            _sourceDragCandidate = !range.IsEmpty && position >= range.Start && position < range.End ? range : null;
        }
        else if (e.ActionMasked == MotionEventActions.Move && _sourceDragCandidate is not null)
        {
            var slop = ViewConfiguration.Get(Context!)?.ScaledTouchSlop ?? 0;
            if (Math.Abs(e.GetX() - _pointerDownX) > slop || Math.Abs(e.GetY() - _pointerDownY) > slop)
            {
                if (!StartSourceDrag()) return false;
                using var cancel = MotionEvent.Obtain(e)!;
                cancel.Action = MotionEventActions.Cancel;
                base.OnTouchEvent(cancel);
                return true;
            }
        }
        else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) _sourceDragCandidate = null;
        return false;
    }

    /// <inheritdoc />
    public override bool PerformLongClick() => StartSourceDrag() || base.PerformLongClick();

    private bool StartSourceDrag()
    {
        if (_sourceDragCandidate is not { } range || ProjectionHandler is not { } handler || handler.VirtualView.Composition.IsActive) return false;
        _sourceDragCandidate = null;
        if (range != handler.VirtualView.SelectedRange) return false;
        var state = new SourceDragState(handler.VirtualView.Document, range);
        using var data = ClipData.NewPlainText(null, state.Fragment.Text);
        using var shadow = new DragShadowBuilder(this);
        bool started;
        if (OperatingSystem.IsAndroidVersionAtLeast(24)) started = StartDragAndDrop(data, shadow, state, (int)DragFlags.Global);
#pragma warning disable CS0618, CA1422
        else started = StartDrag(data, shadow, state, 0);
#pragma warning restore CS0618, CA1422
        if (started) _sourceDrag = state;
        else state.Dispose();
        return started;
    }

    /// <inheritdoc />
    public override bool OnDragEvent(DragEvent? e)
    {
        if (e?.LocalState is SourceDragState state && ReferenceEquals(state, _sourceDrag))
        {
            if (e.Action == DragAction.Ended) { _sourceDrag = null; state.Dispose(); return true; }
            if (ProjectionHandler is not { } handler) return false;
            var editor = handler.VirtualView;
            if (e.Action == DragAction.Drop)
            {
                if (editor.IsReadOnly || editor.Composition.IsActive || editor.Document.Revision != state.Revision) return false;
                var position = handler.NativeProjection.ToSource(GetOffsetForPosition(e.GetX(), e.GetY()));
                if (position >= state.Range.Start && position <= state.Range.End) return true;
                var insertion = position > state.Range.End ? position - state.Range.Length : position;
                editor.EditDocument(edit =>
                {
                    edit.DeleteText(state.Range);
                    edit.ReplaceFragment(new(insertion, 0), state.Fragment);
                }, new RichTextRange(insertion + state.Fragment.Text.Length, 0));
                return true;
            }
            return !editor.IsReadOnly;
        }
        return base.OnDragEvent(e);
    }
}
