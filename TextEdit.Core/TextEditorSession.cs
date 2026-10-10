namespace TextEdit.Core;

/// <summary>Independent mutable selection and input state for one view of a shared source document.</summary>
public sealed class TextEditorSession : IDisposable
{
    private bool _disposed;
    private bool _readOnly;
    private TextSelectionSet _selections = TextSelectionSet.Caret(0);
    private TextComposition? _composition;
    internal TextEditorSession(TextDocument document)
    {
        Document = document;
        InputRules = new(document.VerifyAccess);
        document.Attach(this);
    }
    internal Guid Identity { get; } = Guid.NewGuid();
    /// <summary>Gets the shared source document.</summary>
    public TextDocument Document { get; }
    /// <summary>Gets synchronous rules applied to this session's native input and composition commits.</summary>
    public TextInputRules InputRules { get; }
    /// <summary>Gets this view's provisional composition, or null when input is committed.</summary>
    public TextComposition? Composition => _composition;
    /// <summary>Gets a generation that changes whenever this session's selection changes or is mapped.</summary>
    public long SelectionGeneration { get; private set; }
    /// <summary>Gets or sets whether edits originating from this view are rejected.</summary>
    public bool IsReadOnly
    {
        get => _readOnly;
        set { VerifyIdle(); VerifyNoComposition(); _readOnly = value; }
    }
    /// <summary>Gets or sets this view's independent selection set.</summary>
    public TextSelectionSet Selections
    {
        get => _selections;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            VerifyIdle();
            VerifyNoComposition();
            value.Validate(Document.Length);
            if (ReferenceEquals(_selections, value))
                return;
            _selections = value;
            Document.BreakTypingGroup();
            SelectionGeneration++;
            Document.Plan(() => { NotifyHandlers(SelectionChanged); return 0; });
        }
    }
    /// <summary>Occurs after the document and every view's selections have been synchronized.</summary>
    public event EventHandler<TextDocumentChangedEventArgs>? Changed;
    /// <summary>Occurs when the session's selections change.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Occurs when this view's provisional input starts, changes, commits, or cancels.</summary>
    public event EventHandler? CompositionChanged;

    /// <summary>Acquires the document's composition lease while leaving committed text unchanged.</summary>
    public TextComposition BeginComposition()
    {
        VerifyIdle();
        if (IsReadOnly || Document.ComposingSession is not null)
            throw new InvalidOperationException("Composition requires an editable view and an available document input lease.");
        _composition = new(this);
        Document.BreakTypingGroup();
        Document.ComposingSession = this;
        NotifyComposition();
        return _composition;
    }

    internal void EndComposition(TextComposition composition)
    {
        if (!ReferenceEquals(_composition, composition))
            throw new InvalidOperationException("The composition does not belong to this session.");
        _composition = null;
        Document.ComposingSession = null;
    }

    internal void NotifyComposition() => Document.Plan(() => { NotifyHandlers(CompositionChanged); return 0; });

    private void NotifyHandlers(EventHandler? handlers)
    {
        List<Exception>? errors = null;
        if (handlers is not null)
            foreach (EventHandler handler in handlers.GetInvocationList())
                try { handler(this, EventArgs.Empty); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null)
            throw new AggregateException(errors);
    }

    private void VerifyNoComposition()
    {
        if (_composition is not null)
            throw new InvalidOperationException("Complete or cancel composition before changing this session.");
    }

    /// <summary>Commits a view-originating transaction, optionally checking a captured revision and selection generation.</summary>
    public TextEditResult Edit(Action<TextDocumentEdit> edit, TextEditOptions options = default,
        DocumentRevision? expectedRevision = null, long? expectedSelectionGeneration = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Document.Execute(edit, options, this, expectedRevision, expectedSelectionGeneration);
    }

    /// <summary>Commits recorded sequential native replacements and their dependent input rules atomically.</summary>
    /// <remarks>The native adapter supplies all originating replacements and resulting selections. Rule conflicts throw before publication.</remarks>
    public TextEditResult ApplyInput(IEnumerable<TextReplacement> replacements, TextSelectionSet selections,
        DocumentRevision? expectedRevision = null, long? expectedSelectionGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(selections);
        VerifyIdle();
        var changes = replacements.ToArray();
        return Edit(edit =>
        {
            foreach (var change in changes)
            {
                ArgumentNullException.ThrowIfNull(change);
                edit.Replace(change.Range, change.Text);
            }
            selections.Validate(edit.Snapshot.Length);
            if (changes.Length != 0 && InputRules.Count != 0)
            {
                var rules = InputRules.Plan(Document.CurrentSnapshot, edit.Snapshot, Selections, selections, changes);
                edit.ReplaceBatch(rules.Replacements);
                selections = rules.Selections;
            }
            edit.SetSelections(selections);
        }, new(Origin: TextChangeOrigin.User), expectedRevision, expectedSelectionGeneration);
    }
    /// <summary>Undoes one unit and restores its recorded selection in this view.</summary>
    public void Undo() { VerifyIdle(); if (!IsReadOnly) Document.RestoreHistory(false, this); }
    /// <summary>Redoes one unit and restores its recorded selection in this view.</summary>
    public void Redo() { VerifyIdle(); if (!IsReadOnly) Document.RestoreHistory(true, this); }

    internal void Synchronize(TextDocumentChangedEventArgs change, TextSelectionSet? requested)
    {
        _selections = requested ?? change.Map.Map(_selections);
        SelectionGeneration++;
    }
    internal void Publish(TextDocumentChangedEventArgs change)
    {
        List<Exception>? errors = null;
        if (Changed is { } changed)
            foreach (EventHandler<TextDocumentChangedEventArgs> observer in changed.GetInvocationList())
                try { observer(this, change); } catch (Exception error) { (errors ??= []).Add(error); }
        if (SelectionChanged is { } selection)
            foreach (EventHandler observer in selection.GetInvocationList())
                try { observer(this, EventArgs.Empty); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null)
            throw new AggregateException(errors);
    }
    private void VerifyIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Document.VerifyIdle();
    }
    /// <summary>Detaches the view without clearing shared history or affecting other views.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _composition?.Cancel();
        Document.Detach(this);
        _disposed = true;
    }
}
