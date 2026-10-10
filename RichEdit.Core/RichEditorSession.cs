using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>An independent typed selection and edit origin over a shared rich document.</summary>
public sealed class RichEditorSession : IDisposable
{
    private bool _disposed;
    private bool _readOnly;
    private RichSelection _selection;
    internal RichEditorSession(RichDocument document)
    {
        Document = document;
        _selection = document.CurrentSnapshot.FirstSelection();
        document.Attach(this);
    }
    /// <summary>Gets the shared rich document.</summary>
    public RichDocument Document { get; }
    /// <summary>Gets the generation of this session's selection.</summary>
    public long SelectionGeneration { get; private set; }
    /// <summary>Gets or sets whether this view accepts authored edits.</summary>
    public bool IsReadOnly { get => _readOnly; set { VerifyIdle(); _readOnly = value; } }
    /// <summary>Gets or sets the typed selection. Cell rectangles expand to whole merged cells.</summary>
    public RichSelection Selection
    {
        get => _selection;
        set
        {
            VerifyIdle();
            value = Document.CurrentSnapshot.NormalizeSelection(value);
            if (_selection == value) return;
            _selection = value;
            SelectionGeneration++;
            Document.Plan(() => { NotifySelection(); return 0; });
        }
    }
    /// <summary>Occurs after this session's selection changes.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Occurs after authored content and every attached view's selection have synchronized.</summary>
    public event EventHandler<RichDocumentChangedEventArgs>? Changed;
    /// <summary>Commits a checked view-originating transaction.</summary>
    public RichEditResult Edit(Action<RichDocumentEdit> edit, RichEditOptions options = default,
        DocumentRevision? expectedRevision = null, long? expectedSelectionGeneration = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Document.Execute(edit, options, this, expectedRevision, expectedSelectionGeneration);
    }
    /// <summary>Undoes one global document unit and restores its recorded selection in this invoking view.</summary>
    public void Undo() { VerifyIdle(); if (!IsReadOnly) Document.RestoreHistory(false, this); }
    /// <summary>Redoes one global document unit and restores its recorded selection in this invoking view.</summary>
    public void Redo() { VerifyIdle(); if (!IsReadOnly) Document.RestoreHistory(true, this); }
    internal void Synchronize(RichDocumentChangedEventArgs change, RichSelection? requested)
    {
        _selection = requested ?? RichDocument.MapSelection(_selection, change.Map, change.After);
        SelectionGeneration++;
    }
    internal void Publish(RichDocumentChangedEventArgs change)
    {
        List<Exception>? errors = null;
        if (Changed is { } observers)
            foreach (EventHandler<RichDocumentChangedEventArgs> observer in observers.GetInvocationList())
                try { observer(this, change); } catch (Exception error) { (errors ??= []).Add(error); }
        try { NotifySelection(); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
    }
    private void NotifySelection()
    {
        List<Exception>? errors = null;
        if (SelectionChanged is { } observers)
            foreach (EventHandler observer in observers.GetInvocationList())
                try { observer(this, EventArgs.Empty); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
    }
    private void VerifyIdle() { ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyIdle(); }
    /// <summary>Detaches this session without clearing document history.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Document.Detach(this);
        _disposed = true;
    }
}
