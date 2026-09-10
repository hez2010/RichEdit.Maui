#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        private UIHoverGestureRecognizer? _pointerHover;
        private partial void ConnectInputObservation()
        {
            PlatformView.ProjectionHandler = this;
            _sourceDragDelegate = new(this);
            PlatformView.TextDragDelegate = _sourceDragDelegate;
            PlatformView.CompositionStarting = BeginCompositionOperation;
            PlatformView.CompositionCompleted = EndCompositionOperation;
            PlatformView.PointerObserved = (args, pressed) => VirtualView.ObservePointer(args, pressed);
            _pointerHover = new UIHoverGestureRecognizer(recognizer =>
            {
                if (recognizer.State is UIGestureRecognizerState.Ended or UIGestureRecognizerState.Cancelled) { VirtualView.ObservePointerExit(); return; }
                var point = recognizer.LocationInView(PlatformView);
                VirtualView.ObservePointer(new(new(point.X - PlatformView.ContentOffset.X, point.Y - PlatformView.ContentOffset.Y),
                    RichTextPointerDeviceKind.Mouse, RichTextPointerButtons.None, EditorKeyModifiers.None), false);
            }) { CancelsTouchesInView = false, DelaysTouchesBegan = false, DelaysTouchesEnded = false };
            PlatformView.AddGestureRecognizer(_pointerHover);
        }
        private partial void DisconnectInputObservation()
        {
            _adornmentView.ProjectionHandler = null;
            _adornmentView.TextDragDelegate = null;
            _sourceDragDelegate?.Dispose();
            _sourceDragDelegate = null;
            _adornmentView.CompositionStarting = null;
            _adornmentView.CompositionCompleted = null;
            _adornmentView.PointerObserved = null;
            if (_pointerHover is null) return;
            _adornmentView.RemoveGestureRecognizer(_pointerHover);
            _pointerHover.Dispose();
            _pointerHover = null;
        }
        private partial RichTextCompositionState GetCompositionStateCore()
        {
            using var marked = PlatformView.MarkedTextRange;
            if (marked is null) return default;
            var start = checked((int)PlatformView.GetDisplayOffset(PlatformView.BeginningOfDocument, marked.Start));
            var end = checked((int)PlatformView.GetDisplayOffset(PlatformView.BeginningOfDocument, marked.End));
            return new(true, start >= 0 && end >= start ? new(start, end - start) : null);
        }
        private partial void ReconcileCompositionSource()
        {
            if (_nativeEditGeneration != _readNativeGeneration || PlatformView.TextStorage.Value != DisplaySourceSnapshot.Text) OnNativeDocumentChanged(PlatformView);
            OnNativeSelectionChanged(PlatformView);
        }
    }
}

namespace RichEdit.Maui.Platforms.Apple
{
    public partial class RichTextView
    {
        internal RichEditorHandler? ProjectionHandler { get; set; }
        internal Action<bool>? CompositionStarting { get; set; }
        internal Action? CompositionCompleted { get; set; }
        internal Action<RichTextPointerEventArgs, bool>? PointerObserved { get; set; }
        internal bool MoveSourceVertically(EditorKey key, EditorKeyModifiers modifiers)
        {
            if (ProjectionHandler is not { NativeProjection.IsEmpty: false } handler || key is not (EditorKey.Up or EditorKey.Down) ||
                modifiers is not (EditorKeyModifiers.None or EditorKeyModifiers.Shift)) return false;
            var selection = handler.VirtualView.SelectionState;
            var position = handler.NativeProjection.ToDisplay(selection).Active;
            var direction = key == EditorKey.Up ? UITextLayoutDirection.Up : UITextLayoutDirection.Down;
            do
            {
                using var current = GetDisplayPosition(BeginningOfDocument, position);
                using var next = current is null ? null : GetDisplayPosition(current, direction, 1);
                if (next is null) return true;
                var offset = (int)GetDisplayOffset(BeginningOfDocument, next);
                if (offset == position) return true;
                position = offset;
            } while (handler.IsDisplayReservationLine(position));
            var source = handler.NativeProjection.ToSource(position);
            handler.VirtualView.SelectionState = new(modifiers == EditorKeyModifiers.Shift ? selection.Anchor : source, source);
            return true;
        }
        /// <inheritdoc />
        public override void SetMarkedText(string markedText, NSRange selectedRange)
        {
            CompositionStarting?.Invoke(true);
            try { base.SetMarkedText(markedText, selectedRange); }
            finally { CompositionCompleted?.Invoke(); }
        }
        /// <inheritdoc />
        public override void UnmarkText()
        {
            CompositionStarting?.Invoke(false);
            try { base.UnmarkText(); }
            finally { CompositionCompleted?.Invoke(); }
        }
        /// <inheritdoc />
        public override void InsertText(string text)
        {
            CompositionStarting?.Invoke(false);
            // Direct UITextInput insertion can omit ShouldChangeText. Retain its
            // native replacement range for the same validated incremental reader.
            ProjectionHandler?.RecordNativeInsertion();
            try { base.InsertText(text); }
            finally { CompositionCompleted?.Invoke(); }
        }
        /// <inheritdoc />
        public override void DeleteBackward()
        {
            ProjectionHandler?.PrepareNativeSourceKey(EditorKey.Backspace);
            base.DeleteBackward();
        }
        /// <inheritdoc />
        public override void TouchesBegan(NSSet touches, UIEvent? evt)
        {
            ObserveTouches(touches, evt, true);
            base.TouchesBegan(touches, evt);
        }
        /// <inheritdoc />
        public override void TouchesMoved(NSSet touches, UIEvent? evt)
        {
            ObserveTouches(touches, evt, false);
            base.TouchesMoved(touches, evt);
        }
        private void ObserveTouches(NSSet touches, UIEvent? evt, bool pressed)
        {
            if (PointerObserved is null) return;
            foreach (var touch in touches.Cast<UITouch>())
            {
                var location = touch.LocationInView(this);
                var kind = touch.Type switch
                {
                    UITouchType.Stylus => RichTextPointerDeviceKind.Pen,
                    UITouchType.IndirectPointer => RichTextPointerDeviceKind.Mouse,
                    _ => RichTextPointerDeviceKind.Touch,
                };
                var modifiers = EditorKeyModifiers.None;
                var flags = evt?.ModifierFlags ?? 0;
                if ((flags & UIKeyModifierFlags.Shift) != 0) modifiers |= EditorKeyModifiers.Shift;
                if ((flags & UIKeyModifierFlags.Control) != 0) modifiers |= EditorKeyModifiers.Control;
                if ((flags & UIKeyModifierFlags.Alternate) != 0) modifiers |= EditorKeyModifiers.Alt;
                if ((flags & UIKeyModifierFlags.Command) != 0) modifiers |= EditorKeyModifiers.Meta;
                var buttons = kind == RichTextPointerDeviceKind.Mouse ? RichTextPointerButtons.None : RichTextPointerButtons.Primary;
                if ((evt?.ButtonMask & UIEventButtonMask.Primary) != 0) buttons |= RichTextPointerButtons.Primary;
                if ((evt?.ButtonMask & UIEventButtonMask.Secondary) != 0) buttons |= RichTextPointerButtons.Secondary;
                PointerObserved(new(new(location.X - ContentOffset.X, location.Y - ContentOffset.Y), kind, buttons, modifiers), pressed);
            }
        }
    }
}
#endif
