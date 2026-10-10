using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>Metadata for one authored rich transaction.</summary>
public readonly record struct RichEditOptions(string? Description = null, object? Tag = null, TextChangeOrigin Origin = TextChangeOrigin.Programmatic);

/// <summary>A rich transaction after its immutable state and every attached selection have been synchronized.</summary>
public sealed class RichDocumentChangedEventArgs : EventArgs
{
    internal RichDocumentChangedEventArgs(RichDocumentSnapshot before, RichDocumentSnapshot after, RichAddressMap map, RichEditOptions options)
    { Before = before; After = after; Map = map; Options = options; }
    /// <summary>Gets the preceding snapshot.</summary>
    public RichDocumentSnapshot Before { get; }
    /// <summary>Gets the committed snapshot.</summary>
    public RichDocumentSnapshot After { get; }
    /// <summary>Gets the recorded structural address map.</summary>
    public RichAddressMap Map { get; }
    /// <summary>Gets transaction metadata.</summary>
    public RichEditOptions Options { get; }
}

/// <summary>The outcome of a checked rich edit. Accepted no-ops have no change event.</summary>
public sealed record RichEditResult(TextEditRejection Rejection, RichDocumentChangedEventArgs? Change = null)
{
    /// <summary>Gets whether the edit was accepted.</summary>
    public bool Accepted => Rejection == TextEditRejection.None;
}

/// <summary>An immutable rich snapshot paired with its content-state save token.</summary>
public sealed class RichSaveCapture
{
    internal RichSaveCapture(object identity, long state, RichDocumentSnapshot snapshot) { Identity = identity; State = state; Snapshot = snapshot; }
    internal object Identity { get; }
    internal long State { get; }
    /// <summary>Gets the frozen snapshot whose successful lossless save can be acknowledged.</summary>
    public RichDocumentSnapshot Snapshot { get; }
}

/// <summary>A structured rich document with one authored state, shared sessions, atomic transactions, and document-owned history.</summary>
public sealed partial class RichDocument
{
    private readonly object _identity = new();
    private readonly DocumentAccess _access = new();
    private RichDocumentSnapshot _snapshot;
    private long _nextNode;
    private long _nextState;
    private long _state;
    private long _savedState;
    private readonly List<WeakReference<RichEditorSession>> _sessions = [];
    private readonly List<WeakReference<ProjectionSubscription>> _projections = [];
    private readonly LinkedList<HistoryEntry> _undo = [];
    private readonly LinkedList<HistoryEntry> _redo = [];
    private int _historyLimit = 1000;
    private long _historyByteLimit = 64 * 1024 * 1024;
    private long _historyBytes;
    internal RichEditorSession? ComposingSession { get; set; }

    /// <summary>Creates an empty, clean rich document with no history.</summary>
    public RichDocument() : this(RichDocumentSnapshot.Empty) { }
    /// <summary>Adopts a validated immutable snapshot into a fresh document identity and clean history.</summary>
    public RichDocument(RichDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot.WithRevision(new(_identity, 0));
        _nextNode = Math.Max(snapshot.Nodes.Keys.Max(id => id.Value), snapshot.Assets.Keys.Select(id => id.Value).DefaultIfEmpty().Max());
        _nextNode = Math.Max(_nextNode, snapshot.Bookmarks.Keys.Concat(snapshot.Comments.Keys).Select(id => id.Value).DefaultIfEmpty().Max());
    }
    /// <summary>Creates a document from semantic paragraphs and soft breaks in plain text.</summary>
    public static RichDocument FromPlainText(string text) => new(RichDocumentSnapshot.FromPlainText(text));
    /// <summary>Gets a frozen snapshot suitable for background reads.</summary>
    public RichDocumentSnapshot CurrentSnapshot => Volatile.Read(ref _snapshot);
    /// <summary>Gets document identity and monotonically increasing revision.</summary>
    public DocumentRevision Revision => CurrentSnapshot.Revision;
    /// <summary>Gets whether current content differs from the acknowledged saved state.</summary>
    public bool IsModified => _state != _savedState;
    /// <summary>Gets whether document undo is available.</summary>
    public bool CanUndo => _undo.Count != 0 && !IsUndoGroupOpen;
    /// <summary>Gets whether document redo is available.</summary>
    public bool CanRedo => _redo.Count != 0 && !IsUndoGroupOpen;
    /// <summary>Gets the next undo description.</summary>
    public string? UndoDescription => _undo.Last?.Value.Options.Description;
    /// <summary>Gets the next redo description.</summary>
    public string? RedoDescription => _redo.Last?.Value.Options.Description;
    /// <summary>Gets an estimate of document history retention, excluding application-held snapshots.</summary>
    public long RetainedHistoryByteEstimate => _historyBytes;
    /// <summary>Gets or sets the maximum retained undo and redo units.</summary>
    public int HistoryLimit { get => _historyLimit; set { VerifyIdle(); ArgumentOutOfRangeException.ThrowIfNegative(value); _historyLimit = value; TrimHistory(); NotifyState(); } }
    /// <summary>Gets or sets the estimated history byte budget.</summary>
    public long HistoryByteLimit { get => _historyByteLimit; set { VerifyIdle(); ArgumentOutOfRangeException.ThrowIfNegative(value); _historyByteLimit = value; TrimHistory(); NotifyState(); } }

    /// <summary>Occurs after the document and all view selections have been synchronized.</summary>
    public event EventHandler<RichDocumentChangedEventArgs>? Changed;
    /// <summary>Occurs after history availability or saved state changes without an authored edit.</summary>
    public event EventHandler? StateChanged;
    /// <summary>Creates an independent view session without clearing document history.</summary>
    public RichEditorSession CreateSession() => new(this);

    /// <summary>Commits one schema-validated authored transaction.</summary>
    public RichDocumentChangedEventArgs? Edit(Action<RichDocumentEdit> edit, RichEditOptions options = default)
    {
        var result = Execute(edit, options, null, null, null);
        if (!result.Accepted) throw new InvalidOperationException($"The rich edit was rejected: {result.Rejection}.");
        return result.Change;
    }
    /// <summary>Commits only if the captured rich revision is current.</summary>
    public RichEditResult TryEdit(DocumentRevision revision, Action<RichDocumentEdit> edit, RichEditOptions options = default) =>
        Execute(edit, options, null, revision, null);

    internal RichEditResult Execute(Action<RichDocumentEdit> edit, RichEditOptions options, RichEditorSession? session,
        DocumentRevision? expectedRevision, long? expectedSelection)
    {
        ArgumentNullException.ThrowIfNull(edit);
        VerifyIdle();
        if (!Enum.IsDefined(options.Origin)) throw new ArgumentOutOfRangeException(nameof(options));
        if (expectedRevision is { } revision && revision != Revision) return new(TextEditRejection.StaleRevision);
        if (expectedSelection is { } generation && session?.SelectionGeneration != generation) return new(TextEditRejection.StaleSelection);
        if (session?.IsReadOnly == true) return new(TextEditRejection.ReadOnly);
        if (ComposingSession is not null) return new(TextEditRejection.CompositionActive);
        var before = CurrentSnapshot;
        var builder = new RichDocumentEdit(before, _nextNode);
        var plan = _access.Plan(() =>
        {
            try { edit(builder); return builder.Finish(); }
            finally { builder.Close(); }
        });
        return CommitPrepared(before, plan.Snapshot, plan.Map, plan.NextId, builder.SelectionAfter, options, session);
    }

    internal long NextNodeId => _nextNode;

    internal RichEditResult CommitPrepared(RichDocumentSnapshot before, RichDocumentSnapshot candidate, RichAddressMap map,
        long nextId, RichSelection? requestedSelection, RichEditOptions options, RichEditorSession? session, bool checkContentEquality = false)
    {
        VerifyIdle();
        if (!ReferenceEquals(before, CurrentSnapshot)) return new(TextEditRejection.StaleRevision);
        if (requestedSelection is not null && session is null) throw new InvalidOperationException("Resulting selections require an invoking session.");
        if (checkContentEquality && candidate.ContentEquals(before) || ReferenceEquals(candidate.Nodes, before.Nodes) && ReferenceEquals(candidate.Styles, before.Styles) && ReferenceEquals(candidate.Assets, before.Assets) &&
            ReferenceEquals(candidate.Bookmarks, before.Bookmarks) && ReferenceEquals(candidate.Comments, before.Comments) &&
            candidate.DefaultCharacter == before.DefaultCharacter && candidate.DefaultParagraph == before.DefaultParagraph)
        {
            if (requestedSelection is { } selection) session!.Selection = selection;
            return new(TextEditRejection.None);
        }
        var after = candidate.WithRevision(new(_identity, checked(Revision.Version + 1)));
        var beforeState = _state;
        var afterSelection = session is null ? null : requestedSelection ?? MapSelection(session.Selection, map, after);
        _state = checked(++_nextState);
        _nextNode = nextId;
        var estimate = EstimateChange(before, after);
        StoreHistory(new HistoryEntry(before, after, map, beforeState, _state, session?.Selection, afterSelection, options with { Tag = null }, estimate));
        _historyBytes -= _redo.Sum(entry => entry.EstimatedBytes);
        _redo.Clear();
        TrimHistory();
        Volatile.Write(ref _snapshot, after);
        var change = new RichDocumentChangedEventArgs(before, after, map, options);
        Publish(change, session, afterSelection);
        return new(TextEditRejection.None, change);
    }

    /// <summary>Undoes one document unit while mapping all attached selections.</summary>
    public void Undo() => RestoreHistory(false, null);
    /// <summary>Redoes one document unit while mapping all attached selections.</summary>
    public void Redo() => RestoreHistory(true, null);
    internal void RestoreHistory(bool redo, RichEditorSession? invokingSession)
    {
        VerifyIdle();
        if (IsUndoGroupOpen) throw new InvalidOperationException("Close the explicit undo group before restoring history.");
        if (ComposingSession is not null) throw new InvalidOperationException("Complete composition before restoring rich history.");
        var from = redo ? _redo : _undo;
        var to = redo ? _undo : _redo;
        if (from.Last is not { } node) return;
        var entry = node.Value;
        from.RemoveLast();
        to.AddLast(entry);
        var before = CurrentSnapshot;
        var after = (redo ? entry.After : entry.Before).WithRevision(new(_identity, checked(Revision.Version + 1)));
        _state = redo ? entry.AfterState : entry.BeforeState;
        Volatile.Write(ref _snapshot, after);
        Publish(new(before, after, redo ? entry.Map : entry.Map.Invert(), entry.Options with { Origin = redo ? TextChangeOrigin.Redo : TextChangeOrigin.Undo }),
            invokingSession, redo ? entry.AfterSelection : entry.BeforeSelection);
    }
    /// <summary>Clears document history without changing content or saved-state identity.</summary>
    public void ClearHistory()
    {
        VerifyIdle();
        if (IsUndoGroupOpen) throw new InvalidOperationException("Close the explicit undo group before clearing history.");
        _undo.Clear();
        _redo.Clear();
        _historyBytes = 0;
        NotifyState();
    }
    /// <summary>Captures a frozen snapshot paired with the state to acknowledge after successful lossless RTF saving.</summary>
    public RichSaveCapture CaptureSave()
    {
        VerifyAccess();
        if (ComposingSession is not null) throw new InvalidOperationException("Complete composition before capturing a rich save.");
        return new(_identity, _state, CurrentSnapshot);
    }
    /// <summary>Acknowledges the successfully saved capture, keeping intervening edits dirty.</summary>
    public void MarkSaved(RichSaveCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        VerifyIdle();
        if (!ReferenceEquals(capture.Identity, _identity)) throw new ArgumentException("The save capture belongs to another document.", nameof(capture));
        _savedState = capture.State;
        NotifyState();
    }

    internal void VerifyAccess() => _access.VerifyAccess();
    internal void VerifyIdle() => _access.VerifyIdle();
    internal T Plan<T>(Func<T> plan) => _access.Plan(plan);
    internal void Attach(RichEditorSession session) { VerifyIdle(); _sessions.RemoveAll(reference => !reference.TryGetTarget(out _)); _sessions.Add(new(session)); }
    internal void Detach(RichEditorSession session) { VerifyIdle(); _sessions.RemoveAll(reference => !reference.TryGetTarget(out var item) || ReferenceEquals(item, session)); }

    internal static RichSelection MapSelection(RichSelection selection, RichAddressMap map, RichDocumentSnapshot after)
    {
        if (selection is RichTextSelectionSet set)
            return new RichTextSelectionSet(after, set.Select(item => item with
            { Selection = (RichTextSelection)MapSelection(item.Selection, map, after) }), set.PrimaryIndex);
        if (selection is RichObjectSelection selected && after.Nodes.GetValueOrDefault(selected.Object) is RichImage or RichField) return selected;
        if (selection is RichBoundarySelection boundary && after.Nodes.GetValueOrDefault(boundary.Block) is RichBlock)
            return after.NormalizeSelection(boundary);
        if (selection is RichTextSelection text && map.Map(text.Anchor) is { } anchor && map.Map(text.Active) is { } active &&
            after.Nodes.ContainsKey(anchor.Paragraph) && after.Nodes.ContainsKey(active.Paragraph))
            return new RichTextSelection(anchor with { Offset = Math.Min(anchor.Offset, after.GetParagraphLength(anchor.Paragraph)) },
                active with { Offset = Math.Min(active.Offset, after.GetParagraphLength(active.Paragraph)) });
        if (selection is RichCellSelection cells && after.Nodes.GetValueOrDefault(cells.Table) is RichTable)
        {
            cells = map.MapCells(cells);
            var grid = after.GetTableGrid(cells.Table);
            var row = Math.Min(cells.Row, grid.Rows - 1);
            var column = Math.Min(cells.Column, grid.Columns - 1);
            return grid.Expand(cells with { Row = row, Column = column, RowCount = Math.Min(cells.RowCount, grid.Rows - row), ColumnCount = Math.Min(cells.ColumnCount, grid.Columns - column) });
        }
        return after.FirstSelection();
    }

    internal IDisposable SubscribeProjection(Action<RichDocumentChangedEventArgs> synchronize)
    {
        VerifyIdle();
        ArgumentNullException.ThrowIfNull(synchronize);
        var lease = new ProjectionSubscription(this, synchronize);
        _projections.Add(new(lease));
        return lease;
    }
    private sealed class ProjectionSubscription(RichDocument owner, Action<RichDocumentChangedEventArgs> synchronize) : IDisposable
    {
        internal Action<RichDocumentChangedEventArgs> Synchronize { get; } = synchronize;
        public void Dispose() { owner.VerifyAccess(); owner._projections.RemoveAll(reference => !reference.TryGetTarget(out var item) || ReferenceEquals(item, this)); }
    }

    private void Publish(RichDocumentChangedEventArgs change, RichEditorSession? invokingSession, RichSelection? requested)
    {
        _access.IsBusy = true;
        _sessions.RemoveAll(reference => !reference.TryGetTarget(out _));
        _projections.RemoveAll(reference => !reference.TryGetTarget(out _));
        var sessions = _sessions.Select(reference => reference.TryGetTarget(out var session) ? session : null).OfType<RichEditorSession>().ToArray();
        var projections = _projections.Select(reference => reference.TryGetTarget(out var projection) ? projection : null).OfType<ProjectionSubscription>().ToArray();
        List<Exception>? errors = null;
        void Notify(Action action) { try { action(); } catch (Exception error) { (errors ??= []).Add(error); } }
        try
        {
            foreach (var session in sessions) session.Synchronize(change, ReferenceEquals(session, invokingSession) ? requested : null);
            foreach (var projection in projections) Notify(() => projection.Synchronize(change));
            foreach (var session in sessions) Notify(() => session.Publish(change));
            if (Changed is { } observers)
                foreach (EventHandler<RichDocumentChangedEventArgs> observer in observers.GetInvocationList()) Notify(() => observer(this, change));
        }
        finally { _access.IsBusy = false; }
        if (errors is not null) throw new AggregateException("The rich document committed, but observers failed.", errors);
    }

    private void NotifyState() => _access.Plan(() =>
    {
        List<Exception>? errors = null;
        if (StateChanged is { } observers)
            foreach (EventHandler observer in observers.GetInvocationList())
                try { observer(this, EventArgs.Empty); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
        return 0;
    });
    private void TrimHistory()
    {
        while (_undo.Count + _redo.Count > _historyLimit || _historyBytes > _historyByteLimit)
        {
            var history = _undo.Count != 0 ? _undo : _redo;
            if (history.First is not { } oldest) break;
            _historyBytes -= oldest.Value.EstimatedBytes;
            history.RemoveFirst();
        }
    }
    private static long EstimateChange(RichDocumentSnapshot before, RichDocumentSnapshot after)
    {
        long estimate = 4096;
        foreach (var pair in before.Nodes)
            if (!after.Nodes.TryGetValue(pair.Key, out var value) || !ReferenceEquals(pair.Value, value))
                estimate += 256 + (pair.Value is RichText text ? (long)text.Content.Length * 2 : 0);
        foreach (var pair in after.Nodes)
            if (!before.Nodes.TryGetValue(pair.Key, out var value) || !ReferenceEquals(pair.Value, value))
                estimate += 256 + (pair.Value is RichText text ? (long)text.Content.Length * 2 : 0);
        foreach (var pair in before.Assets)
            if (!after.Assets.TryGetValue(pair.Key, out var asset) || !ReferenceEquals(asset, pair.Value)) estimate += pair.Value.Data.Length;
        foreach (var pair in after.Assets)
            if (!before.Assets.TryGetValue(pair.Key, out var asset) || !ReferenceEquals(asset, pair.Value)) estimate += pair.Value.Data.Length;
        estimate += before.Bookmarks.Where(pair => after.Bookmarks.GetValueOrDefault(pair.Key) != pair.Value).Sum(pair => 128L + pair.Value.Name.Length * 2L);
        estimate += after.Bookmarks.Where(pair => before.Bookmarks.GetValueOrDefault(pair.Key) != pair.Value).Sum(pair => 128L + pair.Value.Name.Length * 2L);
        estimate += before.Comments.Where(pair => after.Comments.GetValueOrDefault(pair.Key) != pair.Value).Sum(pair => 256L + 2L * (pair.Value.Author.Length + (long)pair.Value.Text.Length));
        estimate += after.Comments.Where(pair => before.Comments.GetValueOrDefault(pair.Key) != pair.Value).Sum(pair => 256L + 2L * (pair.Value.Author.Length + (long)pair.Value.Text.Length));
        return estimate;
    }
    private sealed record HistoryEntry(RichDocumentSnapshot Before, RichDocumentSnapshot After, RichAddressMap Map,
        long BeforeState, long AfterState, RichSelection? BeforeSelection, RichSelection? AfterSelection, RichEditOptions Options, long EstimatedBytes);
}
