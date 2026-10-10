using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>A provisional rich root owned by one input session while committed content remains frozen.</summary>
public sealed class RichComposition : IDisposable
{
    private readonly RichEditorSession _session;
    private readonly RichDocumentSnapshot _before;
    private RichAddressMap _map = new([]);
    private long _nextId;
    private bool _closed;
    internal RichComposition(RichEditorSession session)
    {
        _session = session;
        _before = session.Document.CurrentSnapshot;
        Snapshot = _before.WithRevision(default);
        Selection = session.Selection;
        _nextId = session.Document.NextNodeId;
    }
    /// <summary>Gets provisional content with an invalid document revision.</summary>
    public RichDocumentSnapshot Snapshot { get; private set; }
    /// <summary>Gets the primary selection in provisional coordinates.</summary>
    public RichSelection Selection { get; private set; }
    /// <summary>Gets a generation incremented by every successful provisional update.</summary>
    public long Generation { get; private set; }
    internal long NextNodeId => _nextId;

    /// <summary>Stages native input operations without publishing document content or creating history.</summary>
    /// <remarks>A resulting selection can be supplied here or through the builder, but not both.</remarks>
    public void Update(Action<RichDocumentEdit> update, RichSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(update);
        VerifyOpen();
        var builder = new RichDocumentEdit(Snapshot, _nextId);
        var plan = _session.Document.Plan(() =>
        {
            try { update(builder); return builder.Finish(); }
            finally { builder.Close(); }
        });
        if (selection is not null && builder.SelectionAfter is not null) throw new ArgumentException("Supply one resulting composition selection.", nameof(selection));
        var requested = selection ?? builder.SelectionAfter ?? RichDocument.MapSelection(Selection, plan.Map, plan.Snapshot);
        requested = plan.Snapshot.NormalizeSelection(requested);
        Snapshot = plan.Snapshot.WithRevision(default);
        Selection = requested;
        _map = _map.Then(plan.Map);
        _nextId = plan.NextId;
        Generation++;
        _session.NotifyComposition();
    }

    /// <summary>Commits the final validated root as one authored transaction and one undo unit.</summary>
    public RichEditResult Commit(string? description = null)
    {
        VerifyOpen();
        _session.EndComposition(this);
        _closed = true;
        try
        {
            return _session.Document.CommitPrepared(_before, Snapshot, _map, _nextId, Selection,
                new(description, Origin: TextChangeOrigin.User), _session, checkContentEquality: true);
        }
        finally { _session.NotifyComposition(); }
    }

    /// <summary>Discards provisional content and retains the original content, selection, revision, and history.</summary>
    public void Cancel()
    {
        if (_closed) return;
        VerifyOpen();
        _session.EndComposition(this);
        _closed = true;
        _session.NotifyComposition();
    }
    internal void Close() => _closed = true;
    private void VerifyOpen() { ObjectDisposedException.ThrowIf(_closed, this); _session.Document.VerifyIdle(); }
    /// <summary>Cancels an uncommitted composition.</summary>
    public void Dispose() => Cancel();
}
