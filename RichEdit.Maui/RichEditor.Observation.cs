namespace RichEdit.Maui;

/// <summary>The current native marked-text state. An active composition need not have a known source range.</summary>
/// <param name="IsActive">Whether the native input method owns a marked-text session.</param>
/// <param name="Range">The logical marked range when known accurately.</param>
public readonly record struct RichTextCompositionState(bool IsActive, RichTextRange? Range);

/// <summary>A native composition transition, after reconciliation of committed source and selection.</summary>
/// <param name="previous">The previous marked-text state.</param>
/// <param name="current">The current marked-text state.</param>
public sealed class RichTextCompositionChangedEventArgs(RichTextCompositionState previous, RichTextCompositionState current) : EventArgs
{
    /// <summary>Gets the previous state.</summary>
    public RichTextCompositionState Previous { get; } = previous;
    /// <summary>Gets the current state.</summary>
    public RichTextCompositionState Current { get; } = current;
}

/// <summary>The physical device generating a pointer observation.</summary>
public enum RichTextPointerDeviceKind
{
    /// <summary>A mouse or indirect pointer.</summary>
    Mouse,
    /// <summary>A direct touch contact.</summary>
    Touch,
    /// <summary>A pen or stylus.</summary>
    Pen,
}

/// <summary>Buttons held during a pointer observation.</summary>
[Flags]
public enum RichTextPointerButtons
{
    /// <summary>No button is held.</summary>
    None = 0,
    /// <summary>The primary button or touch contact.</summary>
    Primary = 1,
    /// <summary>The secondary button.</summary>
    Secondary = 2,
    /// <summary>The middle button.</summary>
    Middle = 4,
}

/// <summary>A pointer observation in the same local coordinate space as TextLayout. It cannot suppress native input.</summary>
/// <param name="point">The device-independent local point.</param>
/// <param name="deviceKind">The physical device.</param>
/// <param name="buttons">The held buttons.</param>
/// <param name="modifiers">The held keyboard modifiers.</param>
public sealed class RichTextPointerEventArgs(Point point, RichTextPointerDeviceKind deviceKind, RichTextPointerButtons buttons, EditorKeyModifiers modifiers) : EventArgs
{
    /// <summary>Gets the local point.</summary>
    public Point Point { get; } = point;
    /// <summary>Gets the physical device.</summary>
    public RichTextPointerDeviceKind DeviceKind { get; } = deviceKind;
    /// <summary>Gets the held buttons.</summary>
    public RichTextPointerButtons Buttons { get; } = buttons;
    /// <summary>Gets the held keyboard modifiers.</summary>
    public EditorKeyModifiers Modifiers { get; } = modifiers;
}

public sealed partial class RichEditor
{
    private RichTextCompositionState _composition;
    /// <summary>Gets authoritative native composition state without committing or changing marked text.</summary>
    public RichTextCompositionState Composition { get { VerifyAccess(); return _composition; } }
    /// <summary>Occurs when native marked text starts, changes, ends, or is cleared by document/handler replacement.</summary>
    public event EventHandler<RichTextCompositionChangedEventArgs>? CompositionChanged;
    /// <summary>Observes pointer movement without suppressing native input.</summary>
    public event EventHandler<RichTextPointerEventArgs>? PointerMoved;
    /// <summary>Observes a pointer press without suppressing native selection or focus behavior.</summary>
    public event EventHandler<RichTextPointerEventArgs>? PointerPressed;
    /// <summary>Occurs when a hovering pointer exits the editor.</summary>
    public event EventHandler? PointerExited;

    internal void SetCompositionState(RichTextCompositionState state)
    {
        if (state == _composition) return;
        var previous = _composition;
        _composition = state;
        CompositionChanged?.Invoke(this, new(previous, state));
        TextLayout.Invalidate();
    }
    internal void ObservePointer(RichTextPointerEventArgs args, bool pressed)
    {
        if (pressed) PointerPressed?.Invoke(this, args);
        else PointerMoved?.Invoke(this, args);
    }
    internal void ObservePointerExit() => PointerExited?.Invoke(this, EventArgs.Empty);
}

public partial class RichEditorHandler
{
    private int _compositionOperationDepth;
    private void BeginCompositionOperation(bool startsComposition)
    {
        _compositionOperationDepth++;
        if (startsComposition || VirtualView.Composition.IsActive) VirtualView.SetCompositionState(new(true, null));
    }
    private void EndCompositionOperation()
    {
        if (--_compositionOperationDepth != 0) return;
        _compositionOperationDepth = 1;
        try { ReconcileCompositionSource(); }
        finally { _compositionOperationDepth = 0; }
        UpdateCompositionState();
    }
    private void UpdateCompositionState()
    {
        if (_applyingDocument || _compositionOperationDepth != 0) return;
        var state = GetCompositionStateCore();
        if (state.Range is { } nativeRange)
        {
            var start = NativeProjection.ToSource(nativeRange.Start);
            state = new(state.IsActive, new(start, NativeProjection.ToSource(nativeRange.End) - start));
        }
        if (state.Range is { } range && range.End > VirtualView.Document.Length) state = new(state.IsActive, null);
        VirtualView.SetCompositionState(state);
        if (!state.IsActive) QueueAdornmentLayout();
    }
    private partial RichTextCompositionState GetCompositionStateCore();
    private partial void ReconcileCompositionSource();
    private partial void ConnectInputObservation();
    private partial void DisconnectInputObservation();
}
