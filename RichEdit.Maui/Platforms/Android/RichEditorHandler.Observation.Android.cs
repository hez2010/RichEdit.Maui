using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using System.Runtime.Versioning;

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        private partial void ConnectInputObservation()
        {
            PlatformView.ProjectionHandler = this;
            PlatformView.CompositionStarting = BeginCompositionOperation;
            PlatformView.CompositionCompleted = EndCompositionOperation;
            PlatformView.PointerObserved = OnObservedPointer;
            VirtualView.TextChanged += OnSourceTextForInput;
            VirtualView.SelectionChanged += OnSourceSelectionForInput;
            VirtualView.CompositionChanged += OnSourceCompositionForInput;
            VirtualView.DocumentChanged += OnSourceDocumentForInput;
        }
        private partial void DisconnectInputObservation()
        {
            VirtualView.TextChanged -= OnSourceTextForInput;
            VirtualView.SelectionChanged -= OnSourceSelectionForInput;
            VirtualView.CompositionChanged -= OnSourceCompositionForInput;
            VirtualView.DocumentChanged -= OnSourceDocumentForInput;
            _adornmentView.ProjectionHandler = null;
            _adornmentView.CompositionStarting = null;
            _adornmentView.CompositionCompleted = null;
            _adornmentView.PointerObserved = null;
        }
        private void OnSourceTextForInput(object? sender, RichTextTextChangedEventArgs args) => PlatformView.PublishSourceState();
        private void OnSourceSelectionForInput(object? sender, RichTextSelectionChangedEventArgs args) => PlatformView.PublishSourceState();
        private void OnSourceCompositionForInput(object? sender, EventArgs args) => PlatformView.PublishSourceState();
        private void OnSourceDocumentForInput(object? sender, RichTextDocumentReplacedEventArgs args) => PlatformView.PublishSourceState();
        private partial RichTextCompositionState GetCompositionStateCore()
        {
            if (PlatformView.EditableText is not { } text) return default;
            var start = BaseInputConnection.GetComposingSpanStart(text);
            var end = BaseInputConnection.GetComposingSpanEnd(text);
            return start < 0 ? default : new(true, end >= start ? new(start, end - start) : null);
        }
        private partial void ReconcileCompositionSource()
        {
            if (_applyingDocument) return;
            if (PlatformView.Text != DisplaySourceSnapshot.Text)
            {
                var snapshot = ReadDocumentFromPlatform(PlatformView.Text ?? string.Empty);
                var start = Math.Clamp(Math.Min(PlatformView.SelectionStart, PlatformView.SelectionEnd), 0, snapshot.Length);
                var end = Math.Clamp(Math.Max(PlatformView.SelectionStart, PlatformView.SelectionEnd), start, snapshot.Length);
                UpdateDocumentFromDisplay(snapshot, start, end - start, _sourceToken);
            }
            OnNativeSelectionChanged(PlatformView, new(PlatformView.SelectionStart, PlatformView.SelectionEnd));
        }
        private void OnObservedPointer(MotionEvent e)
        {
            if (e.ActionMasked is MotionEventActions.Down or MotionEventActions.Move) _adornmentScrollAnchor = null;
            if (e.ActionMasked == MotionEventActions.HoverExit) { VirtualView.ObservePointerExit(); return; }
            if (e.ActionMasked is not (MotionEventActions.Down or MotionEventActions.Move or MotionEventActions.HoverMove or MotionEventActions.HoverEnter)) return;
            var kind = e.GetToolType(0) switch
            {
                MotionEventToolType.Stylus or MotionEventToolType.Eraser => RichTextPointerDeviceKind.Pen,
                MotionEventToolType.Mouse => RichTextPointerDeviceKind.Mouse,
                _ => RichTextPointerDeviceKind.Touch,
            };
            var buttons = RichTextPointerButtons.None;
            if ((e.ButtonState & MotionEventButtonState.Primary) != 0 || kind == RichTextPointerDeviceKind.Touch) buttons |= RichTextPointerButtons.Primary;
            if ((e.ButtonState & MotionEventButtonState.Secondary) != 0) buttons |= RichTextPointerButtons.Secondary;
            if ((e.ButtonState & MotionEventButtonState.Tertiary) != 0) buttons |= RichTextPointerButtons.Middle;
            var modifiers = EditorKeyModifiers.None;
            if ((e.MetaState & MetaKeyStates.ShiftOn) != 0) modifiers |= EditorKeyModifiers.Shift;
            if ((e.MetaState & MetaKeyStates.CtrlOn) != 0) modifiers |= EditorKeyModifiers.Control;
            if ((e.MetaState & MetaKeyStates.AltOn) != 0) modifiers |= EditorKeyModifiers.Alt;
            if ((e.MetaState & MetaKeyStates.MetaOn) != 0) modifiers |= EditorKeyModifiers.Meta;
            VirtualView.ObservePointer(new(new(e.GetX() / LayoutDensity, e.GetY() / LayoutDensity), kind, buttons, modifiers), e.ActionMasked == MotionEventActions.Down);
        }
    }
}

namespace RichEdit.Maui.Platforms.Android
{
    public partial class RichEditText
    {
        internal RichEditorHandler? ProjectionHandler { get; set; }
        internal Action<bool>? CompositionStarting { get; set; }
        internal Action? CompositionCompleted { get; set; }
        internal Action<MotionEvent>? PointerObserved { get; set; }

        /// <inheritdoc />
        public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
        {
            var connection = base.OnCreateInputConnection(outAttrs);
            if (outAttrs is not null && ProjectionHandler is { } handler && !handler.NativeProjection.IsEmpty)
            {
                outAttrs.InitialSelStart = handler.VirtualView.SelectionState.Anchor;
                outAttrs.InitialSelEnd = handler.VirtualView.SelectionState.Active;
                if (OperatingSystem.IsAndroidVersionAtLeast(30)) outAttrs.SetInitialSurroundingText(handler.VirtualView.Document.Text);
            }
            return _sourceInputConnection = connection is null ? null : new ObservedInputConnection(this, connection);
        }
        /// <inheritdoc />
        public override bool OnHoverEvent(MotionEvent? e)
        {
            if (e is not null) PointerObserved?.Invoke(e);
            return base.OnHoverEvent(e);
        }

        private sealed partial class ObservedInputConnection(RichEditText owner, IInputConnection target) : InputConnectionWrapper(target, false)
        {
            private bool Observe(bool starts, Func<bool> operation)
            {
                owner.CompositionStarting?.Invoke(starts);
                try { return operation(); }
                finally { owner.CompositionCompleted?.Invoke(); }
            }
            public override bool SetComposingText(Java.Lang.ICharSequence? text, int newCursorPosition) => Observe(true, () => base.SetComposingText(text, newCursorPosition));
            public override bool SetComposingRegion(int start, int end) => Observe(true, () => base.SetComposingRegion(Display(start), Display(end)));
            public override bool FinishComposingText() => Observe(false, () => base.FinishComposingText());
            public override bool CommitText(Java.Lang.ICharSequence? text, int newCursorPosition) => Observe(false, () => base.CommitText(text, newCursorPosition));
            [SupportedOSPlatform("android33.0")]
            public override bool SetComposingText(Java.Lang.ICharSequence text, int newCursorPosition, TextAttribute? textAttribute) => Observe(true, () => base.SetComposingText(text, newCursorPosition, textAttribute));
            [SupportedOSPlatform("android33.0")]
            public override bool SetComposingRegion(int start, int end, TextAttribute? textAttribute) => Observe(true, () => base.SetComposingRegion(Display(start), Display(end), textAttribute));
            [SupportedOSPlatform("android33.0")]
            public override bool CommitText(Java.Lang.ICharSequence text, int newCursorPosition, TextAttribute? textAttribute) => Observe(false, () => base.CommitText(text, newCursorPosition, textAttribute));
        }
    }
}
