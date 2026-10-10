using System.Collections.Immutable;

namespace TextEdit.Core;

/// <summary>The cause of a committed source change.</summary>
public enum TextChangeOrigin
{
    /// <summary>An application transaction.</summary>
    Programmatic,
    /// <summary>A native user-input transaction.</summary>
    User,
    /// <summary>Restoring an earlier history state.</summary>
    Undo,
    /// <summary>Reapplying a history state.</summary>
    Redo,
}

/// <summary>An expected reason for rejecting a checked source edit.</summary>
public enum TextEditRejection
{
    /// <summary>The edit was accepted.</summary>
    None,
    /// <summary>The captured document identity or version is stale.</summary>
    StaleRevision,
    /// <summary>The invoking view moved its selection after the request was captured.</summary>
    StaleSelection,
    /// <summary>The invoking view is read-only.</summary>
    ReadOnly,
    /// <summary>Native composition owns the document's input lease.</summary>
    CompositionActive,
}

/// <summary>History and application metadata for a source transaction.</summary>
public readonly record struct TextEditOptions(string? Description = null, object? Tag = null, TextChangeOrigin Origin = TextChangeOrigin.Programmatic);

/// <summary>A committed source transaction with exact recorded changes and a position map.</summary>
public sealed class TextDocumentChangedEventArgs : EventArgs
{
    internal TextDocumentChangedEventArgs(TextSnapshot before, TextSnapshot after, ImmutableArray<TextChange> changes, TextEditOptions options)
    {
        Before = before;
        After = after;
        Changes = changes;
        Options = options;
        Map = new(before.Length, changes.Select(change => new PositionChange(change.OldSpan, change.Inserted.Length)));
    }
    /// <summary>Gets the committed snapshot before this transaction.</summary>
    public TextSnapshot Before { get; }
    /// <summary>Gets the committed snapshot after this transaction.</summary>
    public TextSnapshot After { get; }
    /// <summary>Gets recorded replacements in sequential coordinates.</summary>
    public ImmutableArray<TextChange> Changes { get; }
    /// <summary>Gets the exact position transformation.</summary>
    public PositionMap Map { get; }
    /// <summary>Gets the origin, history description, and application tag.</summary>
    public TextEditOptions Options { get; }
}

/// <summary>The outcome of a checked transaction. An accepted no-op has no change event.</summary>
public sealed record TextEditResult(TextEditRejection Rejection, TextDocumentChangedEventArgs? Change = null)
{
    /// <summary>Gets whether the checked request was accepted.</summary>
    public bool Accepted => Rejection == TextEditRejection.None;
}

/// <summary>An immutable snapshot paired with the content state to acknowledge after a successful save.</summary>
public sealed class TextSaveCapture
{
    internal TextSaveCapture(object identity, long state, TextSnapshot snapshot) { Identity = identity; State = state; Snapshot = snapshot; }
    internal object Identity { get; }
    internal long State { get; }
    /// <summary>Gets the exact text captured with this save token.</summary>
    public TextSnapshot Snapshot { get; }
}

/// <summary>An independent plain-text document with persistent snapshots, atomic edits, shared sessions, and one history.</summary>
/// <remarks>Preserves every UTF-16 code unit and line ending. The first mutation or attached session claims a mutation thread; immutable snapshots may be read on any thread.</remarks>
public sealed class TextDocument
{
    private readonly object _identity = new();
    private TextSnapshot _snapshot;
    private int _ownerThread;
    private bool _busy;
    private long _nextState;
    private long _state;
    private long _savedState;
    private readonly List<WeakReference<TextEditorSession>> _sessions = [];
    private readonly List<WeakReference<ProjectionSubscription>> _projections = [];
    private readonly LinkedList<HistoryEntry> _undo = [];
    private readonly LinkedList<HistoryEntry> _redo = [];
    private int _historyLimit = 1000;
    private long _historyByteLimit = 64 * 1024 * 1024;
    private long _historyBytes;
    private readonly Stack<UndoScope> _groups = [];
    private HistoryEntry? _groupEntry;
    private string? _groupDescription;
    private Guid? _typingSession;
    private TextSelectionSet? _typingSelection;
    private TextChange? _typingChange;
    private HistoryEntry? _typingEntry;
    private long _typingTick;
    internal TextEditorSession? ComposingSession { get; set; }

    /// <summary>Creates an independent source document without normalizing input.</summary>
    public TextDocument(string text = "") : this(TextSnapshot.FromText(text)) { }
    /// <summary>Adopts immutable text into a new document identity and clean history.</summary>
    public TextDocument(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot.WithRevision(new(_identity, 0));
        Tracking = new(this);
    }

    /// <summary>Gets an immutable snapshot suitable for background reads.</summary>
    public TextSnapshot CurrentSnapshot => Volatile.Read(ref _snapshot);
    /// <summary>Gets the current document identity and monotonically increasing version.</summary>
    public DocumentRevision Revision => CurrentSnapshot.Revision;
    /// <summary>Gets indexed positions and ranges owned by this document.</summary>
    public TextTracking Tracking { get; }
    /// <summary>Gets source length in UTF-16 code units.</summary>
    public int Length => CurrentSnapshot.Length;
    /// <summary>Materializes the complete source text.</summary>
    public string Text => CurrentSnapshot.ToString();
    /// <summary>Gets whether the current content state differs from the acknowledged save.</summary>
    public bool IsModified => _state != _savedState;
    /// <summary>Gets whether document undo is available.</summary>
    public bool CanUndo => _undo.Count != 0;
    /// <summary>Gets whether document redo is available.</summary>
    public bool CanRedo => _redo.Count != 0;
    /// <summary>Gets the next undo description.</summary>
    public string? UndoDescription => _undo.Last?.Value.Description;
    /// <summary>Gets the next redo description.</summary>
    public string? RedoDescription => _redo.Last?.Value.Description;
    /// <summary>Gets whether an explicit history group is open.</summary>
    public bool IsUndoGroupOpen => _groups.Count != 0;
    /// <summary>Gets an estimate of text and tree-path storage retained by undo and redo. Application-held snapshots are excluded.</summary>
    public long RetainedHistoryByteEstimate => _historyBytes;

    /// <summary>Gets or sets the retained-history byte estimate limit, applied when complete history units are stored.</summary>
    public long HistoryByteLimit
    {
        get => _historyByteLimit;
        set { VerifyIdle(); ArgumentOutOfRangeException.ThrowIfNegative(value); _historyByteLimit = value; TrimHistory(); NotifyState(); }
    }

    /// <summary>Gets or sets the maximum retained undo units. Zero disables history retention.</summary>
    public int HistoryLimit
    {
        get => _historyLimit;
        set
        {
            VerifyIdle();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _historyLimit = value;
            TrimHistory();
            NotifyState();
        }
    }

    /// <summary>Occurs after all session selections have been synchronized.</summary>
    public event EventHandler<TextDocumentChangedEventArgs>? Changed;
    /// <summary>Occurs when history availability or saved state changes without changing text.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Creates an independent view session over this document.</summary>
    public TextEditorSession CreateSession() => new(this);

    /// <summary>Commits a sequential transaction. Programmer errors leave the live document unchanged.</summary>
    public TextDocumentChangedEventArgs? Edit(Action<TextDocumentEdit> edit, TextEditOptions options = default)
    {
        var result = Execute(edit, options, null, null, null);
        if (!result.Accepted)
            throw new InvalidOperationException($"The source edit was rejected: {result.Rejection}.");
        return result.Change;
    }

    /// <summary>Commits only if the supplied document revision is current.</summary>
    public TextEditResult TryEdit(DocumentRevision expectedRevision, Action<TextDocumentEdit> edit, TextEditOptions options = default) =>
        Execute(edit, options, null, expectedRevision, null);

    internal TextEditResult Execute(Action<TextDocumentEdit> edit, TextEditOptions options, TextEditorSession? session,
        DocumentRevision? expectedRevision, long? expectedSelection)
    {
        ArgumentNullException.ThrowIfNull(edit);
        VerifyIdle();
        if (expectedRevision is { } revision && (!revision.IsValid || revision != Revision))
            return new(TextEditRejection.StaleRevision);
        if (session is { IsReadOnly: true })
            return new(TextEditRejection.ReadOnly);
        if (expectedSelection is { } generation && session?.SelectionGeneration != generation)
            return new(TextEditRejection.StaleSelection);
        if (ComposingSession is not null)
            return new(TextEditRejection.CompositionActive);
        if (options.Origin is not (TextChangeOrigin.Programmatic or TextChangeOrigin.User))
            throw new ArgumentOutOfRangeException(nameof(options));
        _busy = true;
        var builder = new TextDocumentEdit(CurrentSnapshot);
        (TextSnapshot Snapshot, ImmutableArray<TextChange> Changes, TextSelectionSet? Selections) plan;
        try
        {
            edit(builder);
            plan = builder.Finish();
        }
        finally
        {
            builder.Close();
            _busy = false;
        }
        if (plan.Selections is not null && session is null)
            throw new InvalidOperationException("Resulting selections require an invoking view session.");
        if (plan.Changes.IsEmpty)
        {
            if (plan.Selections is { } selections)
                session!.Selections = selections;
            return new(TextEditRejection.None);
        }
        var before = CurrentSnapshot;
        var after = plan.Snapshot.WithRevision(new(_identity, checked(Revision.Version + 1)));
        var change = new TextDocumentChangedEventArgs(before, after, plan.Changes, options);
        var beforeState = _state;
        _state = checked(++_nextState);
        var selectionBefore = session?.Selections;
        var selectionAfter = session is null ? null : plan.Selections ?? change.Map.Map(session.Selections);
        StoreHistory(new HistoryEntry(before, after, beforeState, _state, plan.Changes.ToImmutableList(), options.Description, selectionBefore, selectionAfter,
            plan.Changes.Sum(change => 8192L + 2L * (change.Removed.Length + (long)change.Inserted.Length))), session, options.Origin);
        _historyBytes -= _redo.Sum(entry => entry.EstimatedBytes);
        _redo.Clear();
        TrimHistory();
        Volatile.Write(ref _snapshot, after);
        Publish(change, session, selectionAfter);
        return new(TextEditRejection.None, change);
    }

    /// <summary>Restores the preceding content state and maps every session's current selection.</summary>
    public void Undo() => RestoreHistory(redo: false, null);
    /// <summary>Reapplies the next content state and maps every session's current selection.</summary>
    public void Redo() => RestoreHistory(redo: true, null);

    internal void RestoreHistory(bool redo, TextEditorSession? invokingSession)
    {
        VerifyIdle();
        if (IsUndoGroupOpen)
            throw new InvalidOperationException("Close the explicit undo group before using history.");
        if (ComposingSession is not null)
            throw new InvalidOperationException("Complete native composition before using history.");
        var from = redo ? _redo : _undo;
        var to = redo ? _undo : _redo;
        if (from.Last is not { } node)
            return;
        var entry = node.Value;
        BreakTypingGroup();
        from.RemoveLast();
        to.AddLast(entry);
        var target = redo ? entry.After : entry.Before;
        var changes = redo ? entry.Changes.ToImmutableArray() : entry.Changes.Reverse().Select(change => change.Invert()).ToImmutableArray();
        var before = CurrentSnapshot;
        var after = target.WithRevision(new(_identity, checked(Revision.Version + 1)));
        _state = redo ? entry.AfterState : entry.BeforeState;
        Volatile.Write(ref _snapshot, after);
        Publish(new(before, after, changes, new(entry.Description, Origin: redo ? TextChangeOrigin.Redo : TextChangeOrigin.Undo)),
            invokingSession, redo ? entry.AfterSelection : entry.BeforeSelection);
    }

    /// <summary>Clears undo and redo without changing source or saved-state identity.</summary>
    public void ClearHistory()
    {
        VerifyIdle();
        if (IsUndoGroupOpen)
            throw new InvalidOperationException("Close the explicit undo group before clearing history.");
        BreakTypingGroup();
        _undo.Clear();
        _redo.Clear();
        _historyBytes = 0;
        NotifyState();
    }

    /// <summary>Captures one immutable snapshot and its paired save token. Active composition must finish first.</summary>
    public TextSaveCapture CaptureSave()
    {
        VerifyAccess();
        if (ComposingSession is not null)
            throw new InvalidOperationException("Complete native composition before capturing a save.");
        BreakTypingGroup();
        return new(_identity, _state, CurrentSnapshot);
    }

    /// <summary>Groups separately published transactions into one undo unit. Nested scopes must close in reverse order.</summary>
    public IDisposable BeginUndoGroup(string? description = null)
    {
        VerifyIdle();
        if (ComposingSession is not null)
            throw new InvalidOperationException("Complete native composition before opening an undo group.");
        BreakTypingGroup();
        if (!IsUndoGroupOpen)
            _groupDescription = description;
        var scope = new UndoScope(this);
        _groups.Push(scope);
        return scope;
    }

    private void EndUndoGroup(UndoScope scope)
    {
        VerifyIdle();
        if (!_groups.TryPeek(out var current) || !ReferenceEquals(current, scope))
            throw new InvalidOperationException("Undo groups must close in reverse nesting order.");
        _groups.Pop();
        scope.Closed = true;
        if (!IsUndoGroupOpen && _groupEntry is { } entry)
        {
            _undo.AddLast(entry with { Description = _groupDescription ?? entry.Description });
            _historyBytes += entry.EstimatedBytes;
            _groupEntry = null;
            _groupDescription = null;
            TrimHistory();
        }
        NotifyState();
    }

    internal void BreakTypingGroup()
    {
        _typingSession = null;
        _typingSelection = null;
        _typingChange = null;
        _typingEntry = null;
    }

    private void StoreHistory(HistoryEntry entry, TextEditorSession? session, TextChangeOrigin origin)
    {
        if (IsUndoGroupOpen)
        {
            _groupEntry = _groupEntry is { } group ? Merge(group, entry) : entry;
            BreakTypingGroup();
            return;
        }
        var native = origin == TextChangeOrigin.User && session is not null && entry.Changes.Count == 1 ? entry.Changes[0] : null;
        if (native is not null && _typingChange is { } previous && session!.Identity == _typingSession &&
            ReferenceEquals(entry.BeforeSelection, _typingSelection) && ReferenceEquals(_undo.Last?.Value, _typingEntry) &&
            Environment.TickCount64 - _typingTick <= 750 && CompatibleTyping(previous, native))
        {
            entry = Merge(_undo.Last!.Value, entry);
            _historyBytes -= _undo.Last.Value.EstimatedBytes;
            _undo.RemoveLast();
        }
        _undo.AddLast(entry);
        _historyBytes += entry.EstimatedBytes;
        BreakTypingGroup();
        if (native is not null)
        {
            _typingSession = session!.Identity;
            _typingSelection = entry.AfterSelection;
            _typingChange = native;
            _typingEntry = entry;
            _typingTick = Environment.TickCount64;
        }
    }

    private static bool CompatibleTyping(TextChange previous, TextChange current) =>
        previous.OldSpan.IsEmpty && current.OldSpan.IsEmpty && previous.Inserted.Length > 0 && current.Inserted.Length > 0 &&
            current.OldSpan.Start == previous.NewSpan.End ||
        previous.Inserted.Length == 0 && current.Inserted.Length == 0 && !previous.OldSpan.IsEmpty && !current.OldSpan.IsEmpty &&
            (current.OldSpan.Start == previous.OldSpan.Start || current.OldSpan.End == previous.OldSpan.Start);

    private static HistoryEntry Merge(HistoryEntry before, HistoryEntry after) => before with
    {
        After = after.After,
        AfterState = after.AfterState,
        AfterSelection = after.AfterSelection,
        Changes = before.Changes.AddRange(after.Changes),
        EstimatedBytes = before.EstimatedBytes + after.EstimatedBytes,
    };

    /// <summary>Acknowledges a successful save of the captured text, retaining dirty state for newer edits.</summary>
    public void MarkSaved(TextSaveCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        VerifyIdle();
        if (!ReferenceEquals(capture.Identity, _identity))
            throw new ArgumentException("The save capture belongs to a different document.", nameof(capture));
        _savedState = capture.State;
        NotifyState();
    }

    internal void VerifyAccess()
    {
        var thread = Environment.CurrentManagedThreadId;
        var owner = Interlocked.CompareExchange(ref _ownerThread, thread, 0);
        if (owner != 0 && owner != thread)
            throw new InvalidOperationException("Live document mutations belong to the owning thread. Share immutable snapshots for background reads.");
    }
    internal void VerifyIdle()
    {
        VerifyAccess();
        if (_busy)
            throw new InvalidOperationException("A live document cannot be mutated from an edit callback or change notification.");
    }
    internal T Plan<T>(Func<T> action)
    {
        VerifyIdle();
        _busy = true;
        try { return action(); }
        finally { _busy = false; }
    }
    internal void Attach(TextEditorSession session)
    {
        VerifyIdle();
        _sessions.RemoveAll(reference => !reference.TryGetTarget(out _));
        _sessions.Add(new(session));
    }
    internal void Detach(TextEditorSession session)
    {
        VerifyIdle();
        _sessions.RemoveAll(reference => !reference.TryGetTarget(out var item) || ReferenceEquals(item, session));
    }

    internal IDisposable SubscribeProjection(Action<TextDocumentChangedEventArgs> synchronize)
    {
        ArgumentNullException.ThrowIfNull(synchronize);
        VerifyIdle();
        var subscription = new ProjectionSubscription(this, synchronize);
        _projections.Add(new(subscription));
        return subscription;
    }

    private sealed class ProjectionSubscription(TextDocument document, Action<TextDocumentChangedEventArgs> synchronize) : IDisposable
    {
        internal Action<TextDocumentChangedEventArgs> Synchronize { get; } = synchronize;
        public void Dispose()
        {
            document.VerifyAccess();
            document._projections.RemoveAll(reference => !reference.TryGetTarget(out var item) || ReferenceEquals(item, this));
        }
    }

    private void Publish(TextDocumentChangedEventArgs change, TextEditorSession? invokingSession, TextSelectionSet? selection)
    {
        _busy = true;
        _sessions.RemoveAll(reference => !reference.TryGetTarget(out _));
        var sessions = _sessions.Select(reference => reference.TryGetTarget(out var session) ? session : null).OfType<TextEditorSession>().ToArray();
        _projections.RemoveAll(reference => !reference.TryGetTarget(out _));
        var projections = _projections.Select(reference => reference.TryGetTarget(out var projection) ? projection : null).OfType<ProjectionSubscription>().ToArray();
        List<Exception>? errors = null;
        void Notify(Action action) { try { action(); } catch (Exception error) { (errors ??= []).Add(error); } }
        try
        {
            Tracking.MapThrough(change.Map);
            foreach (var session in sessions)
                session.Synchronize(change, ReferenceEquals(session, invokingSession) ? selection : null);
            foreach (var projection in projections)
                Notify(() => projection.Synchronize(change));
            foreach (var session in sessions)
                Notify(() => session.Publish(change));
            if (Changed is { } observers)
                foreach (EventHandler<TextDocumentChangedEventArgs> observer in observers.GetInvocationList())
                    Notify(() => observer(this, change));
        }
        finally { _busy = false; }
        if (errors is not null)
            throw new AggregateException("The source committed, but one or more observers failed.", errors);
    }
    private void TrimHistory()
    {
        while (_undo.Count + _redo.Count > _historyLimit)
        {
            if (_undo.Count > 0)
            {
                _historyBytes -= _undo.First!.Value.EstimatedBytes;
                _undo.RemoveFirst();
            }
            else
            {
                _historyBytes -= _redo.First!.Value.EstimatedBytes;
                _redo.RemoveFirst();
            }
        }
        while (_historyBytes > _historyByteLimit && _undo.First is { } oldest)
        {
            _historyBytes -= oldest.Value.EstimatedBytes;
            _undo.RemoveFirst();
        }
        while (_historyBytes > _historyByteLimit && _redo.First is { } farthest)
        {
            _historyBytes -= farthest.Value.EstimatedBytes;
            _redo.RemoveFirst();
        }
        if (!ReferenceEquals(_typingEntry, _undo.Last?.Value))
            BreakTypingGroup();
    }
    private void NotifyState()
    {
        _busy = true;
        List<Exception>? errors = null;
        try
        {
            if (StateChanged is { } observers)
                foreach (EventHandler observer in observers.GetInvocationList())
                    try { observer(this, EventArgs.Empty); } catch (Exception error) { (errors ??= []).Add(error); }
        }
        finally { _busy = false; }
        if (errors is not null)
            throw new AggregateException("The document state changed, but one or more observers failed.", errors);
    }

    private sealed record HistoryEntry(TextSnapshot Before, TextSnapshot After, long BeforeState, long AfterState,
        ImmutableList<TextChange> Changes, string? Description, TextSelectionSet? BeforeSelection, TextSelectionSet? AfterSelection, long EstimatedBytes);

    private sealed class UndoScope(TextDocument document) : IDisposable
    {
        internal bool Closed { get; set; }
        public void Dispose() { if (!Closed) document.EndUndoGroup(this); }
    }
}
