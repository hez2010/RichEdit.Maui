using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>An independent typed selection and edit origin over a shared rich document.</summary>
public sealed partial class RichEditorSession : IDisposable
{
    private bool _disposed;
    private bool _readOnly;
    private RichSelection _selection;
    private RichComposition? _composition;
    private RichCharacterStyle _typingFormat = new();
    internal RichEditorSession(RichDocument document)
    {
        Document = document;
        _selection = document.CurrentSnapshot.FirstSelection();
        document.Attach(this);
    }
    /// <summary>Gets the shared rich document.</summary>
    public RichDocument Document { get; }
    /// <summary>Gets this view's provisional composition, or null outside marked input.</summary>
    public RichComposition? Composition => _composition;
    /// <summary>Gets the generation of this session's selection.</summary>
    public long SelectionGeneration { get; private set; }
    /// <summary>Gets or sets view-owned character overrides for newly typed text. Moving the selection clears these overrides.</summary>
    public RichCharacterStyle TypingCharacterFormat
    {
        get => _typingFormat;
        set
        {
            VerifyIdle(); VerifyNoComposition(); ArgumentNullException.ThrowIfNull(value); RichStyleSheet.Validate(value);
            if (_typingFormat == value) return;
            _typingFormat = value;
            Document.Plan(() => { TypingFormatChanged?.Invoke(this, EventArgs.Empty); return 0; });
        }
    }
    /// <summary>Resolves the current caret's authored cascade and view-owned typing overrides.</summary>
    public ResolvedCharacterStyle ResolveTypingCharacterFormat()
    {
        var snapshot = Composition?.Snapshot ?? Document.CurrentSnapshot;
        var selection = Composition?.Selection ?? Selection;
        var point = selection switch { RichTextSelection text => text.Active, RichTextSelectionSet set => set.Primary.Selection.Active, _ => (RichTextPoint?)null };
        if (point is null) return TypingCharacterFormat.Apply(snapshot.DefaultCharacter.Apply(new()));
        var paragraph = snapshot.Get<RichParagraph>(point.Value.Paragraph);
        RichInline? inline = null;
        var offset = 0;
        foreach (var id in paragraph.Inlines)
        {
            inline = snapshot.Get<RichInline>(id);
            offset += RichDocumentSnapshot.InlineLength(inline);
            if (offset >= point.Value.Offset) break;
        }
        return snapshot.Styles.ResolveCharacter(paragraph.Style, inline?.Style,
            (inline?.Format ?? new()).Overlay(TypingCharacterFormat), snapshot.DefaultCharacter, paragraph.Character);
    }
    /// <summary>Gets or sets whether this view accepts authored edits.</summary>
    public bool IsReadOnly { get => _readOnly; set { VerifyIdle(); VerifyNoComposition(); _readOnly = value; } }
    /// <summary>Gets or sets the typed selection. Cell rectangles expand to whole merged cells.</summary>
    public RichSelection Selection
    {
        get => _selection;
        set
        {
            VerifyIdle();
            VerifyNoComposition();
            value = Document.CurrentSnapshot.NormalizeSelection(value);
            if (_selection == value) return;
            _selection = value;
            _typingFormat = new();
            SelectionGeneration++;
            Document.Plan(() => { NotifySelection(); return 0; });
        }
    }
    /// <summary>Occurs after this session's selection changes.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Occurs after authored content and every attached view's selection have synchronized.</summary>
    public event EventHandler<RichDocumentChangedEventArgs>? Changed;
    /// <summary>Occurs when provisional marked input starts, changes, commits, or cancels.</summary>
    public event EventHandler? CompositionChanged;
    /// <summary>Occurs when view-owned typing overrides are explicitly changed.</summary>
    public event EventHandler? TypingFormatChanged;

    /// <summary>Acquires the document's composition lease while keeping committed content and history unchanged.</summary>
    public RichComposition BeginComposition()
    {
        VerifyIdle();
        if (IsReadOnly || Document.ComposingSession is not null) throw new InvalidOperationException("Composition requires an editable view and an available document lease.");
        var composition = _composition = new(this);
        Document.ComposingSession = this;
        try { NotifyComposition(); }
        catch
        {
            EndComposition(composition);
            composition.Close();
            throw;
        }
        return composition;
    }

    internal void EndComposition(RichComposition composition)
    {
        if (!ReferenceEquals(_composition, composition)) throw new InvalidOperationException("The composition belongs to another session.");
        _composition = null;
        Document.ComposingSession = null;
    }

    internal void NotifyComposition() => Document.Plan(() =>
    {
        List<Exception>? errors = null;
        if (CompositionChanged is { } observers)
            foreach (EventHandler observer in observers.GetInvocationList())
                try { observer(this, EventArgs.Empty); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
        return 0;
    });
    /// <summary>Commits a checked view-originating transaction.</summary>
    public RichEditResult Edit(Action<RichDocumentEdit> edit, RichEditOptions options = default,
        DocumentRevision? expectedRevision = null, long? expectedSelectionGeneration = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Document.Execute(edit, options, this, expectedRevision, expectedSelectionGeneration);
    }
    /// <summary>Replaces the current typed selection or every rich text caret in one transaction.</summary>
    public RichEditResult ReplaceSelection(string text, RichEditOptions options = default) =>
        Edit(edit => edit.SetSelection(edit.ReplaceSelection(Selection, text, TypingCharacterFormat)), options);
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
    private void VerifyNoComposition() { if (_composition is not null) throw new InvalidOperationException("Complete or cancel composition before changing the session."); }
    /// <summary>Detaches this session without clearing document history.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        try { _composition?.Cancel(); }
        finally { Document.Detach(this); _disposed = true; }
    }
}
