namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private RichTextCompositionState _composition;

    /// <summary>Gets authoritative native composition state without committing or changing marked text.</summary>
    public RichTextCompositionState Composition
    {
        get
        {
            VerifyAccess();
            return _composition;
        }
    }

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
        if (state == _composition)
            return;

        var previous = _composition;
        _composition = state;
        if (state.IsActive && !previous.IsActive)
        {
            _compositionDocument = Document;
            _compositionPreviewDocument = new RichTextDocument(Document.CurrentSnapshot);
            Document.BreakTypingUndoGroup();
        }
        if (!state.IsActive && previous.IsActive)
        {
            CommitComposition();
            Document.BreakTypingUndoGroup();
        }
        Commands.Refresh();
        CompositionChanged?.Invoke(this, new(previous, state));
        TextLayout.Invalidate();
    }

    internal void ObservePointer(RichTextPointerEventArgs args, bool pressed)
    {
        if (pressed)
            PointerPressed?.Invoke(this, args);
        else
            PointerMoved?.Invoke(this, args);
    }

    internal void ObservePointerExit() => PointerExited?.Invoke(this, EventArgs.Empty);
}
