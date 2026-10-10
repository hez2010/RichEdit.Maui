namespace RichEdit.Core;

public sealed partial class RichDocument
{
    private readonly Stack<UndoScope> _groups = [];
    private HistoryEntry? _groupEntry;
    private string? _groupDescription;

    /// <summary>Gets whether an explicit document history group is open.</summary>
    public bool IsUndoGroupOpen => _groups.Count != 0;

    /// <summary>Groups separately published rich transactions into one undo unit. Nested scopes close in reverse order.</summary>
    public IDisposable BeginUndoGroup(string? description = null)
    {
        VerifyIdle();
        if (ComposingSession is not null) throw new InvalidOperationException("Complete composition before opening a history group.");
        if (!IsUndoGroupOpen) _groupDescription = description;
        var scope = new UndoScope(this);
        _groups.Push(scope);
        try { NotifyState(); }
        catch (Exception error)
        {
            _groups.Pop();
            scope.Closed = true;
            if (!IsUndoGroupOpen) _groupDescription = null;
            try { NotifyState(); }
            catch (Exception rollback) { throw new AggregateException(error, rollback); }
            throw;
        }
        return scope;
    }

    private void StoreHistory(HistoryEntry entry)
    {
        if (IsUndoGroupOpen)
        {
            _groupEntry = _groupEntry is not { } first ? entry : first with
            {
                After = entry.After,
                AfterState = entry.AfterState,
                AfterSelection = entry.AfterSelection,
                Map = first.Map.Then(entry.Map),
                EstimatedBytes = first.EstimatedBytes + entry.EstimatedBytes,
            };
            return;
        }
        _undo.AddLast(entry);
        _historyBytes += entry.EstimatedBytes;
    }

    private void EndUndoGroup(UndoScope scope)
    {
        VerifyIdle();
        if (!_groups.TryPeek(out var current) || !ReferenceEquals(current, scope)) throw new InvalidOperationException("History groups must close in reverse nesting order.");
        _groups.Pop();
        scope.Closed = true;
        if (!IsUndoGroupOpen && _groupEntry is { } entry)
        {
            if (entry.Before.ContentEquals(entry.After)) _state = entry.BeforeState;
            else
            {
                _undo.AddLast(entry with { Options = entry.Options with { Description = _groupDescription ?? entry.Options.Description } });
                _historyBytes += entry.EstimatedBytes;
            }
            _groupEntry = null;
            _groupDescription = null;
            TrimHistory();
        }
        NotifyState();
    }

    private sealed class UndoScope(RichDocument owner) : IDisposable
    {
        internal bool Closed { get; set; }
        public void Dispose() { if (!Closed) owner.EndUndoGroup(this); }
    }
}
