using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocument
{
    private RichDocumentSnapshot TrackRevision(RichDocumentSnapshot before, RichDocumentSnapshot after, RichRevisionInfo? info)
    {
        // Resolution already updates the retained records in its transaction.
        if (!ReferenceEquals(before.Revisions, after.Revisions)) return after;
        if (info is null && before.Revisions.IsEmpty) return after;
        var delta = RichRevisionDelta.Between(before, after);
        if (delta.IsEmpty) return after;
        var dependencies = before.Revisions.Values.Where(revision => delta.Overlaps(revision.Delta)).Select(revision => revision.Id).ToImmutableHashSet();
        if (info is null)
        {
            if (!dependencies.IsEmpty) throw new InvalidOperationException("Enable revision tracking or resolve overlapping revisions before editing their content or resources.");
            return after;
        }
        if (before.Revisions.Values.Any(revision => revision.Info.Group == info.Group && revision.Info != info))
            throw new ArgumentException("Transactions in one revision group must use the same author and creation time.", nameof(info));
        var id = checked(_nextRevision + 1);
        var result = after.WithRevisions(after.Revisions.Add(id, new(id, info, delta, dependencies)));
        _nextRevision = id;
        return result;
    }
}

public sealed partial class RichDocumentSnapshot
{
    /// <summary>Gets the exact pending transactions an atomic group resolution will include.</summary>
    /// <remarks>Acceptance includes prerequisites. Rejection includes dependent groups. Whole groups are always resolved together.</remarks>
    public ImmutableArray<RichRevision> GetRevisionResolution(Guid group, bool accept)
    {
        if (!Revisions.Values.Any(revision => revision.Info.Group == group)) throw new ArgumentException("The revision group does not exist.", nameof(group));
        var selected = Revisions.Values.Where(revision => revision.Info.Group == group).Select(revision => revision.Id).ToHashSet();
        bool changed;
        do
        {
            changed = false;
            var groups = selected.Select(id => Revisions[id].Info.Group).ToHashSet();
            foreach (var revision in Revisions.Values)
            {
                if (groups.Contains(revision.Info.Group)) changed |= selected.Add(revision.Id);
                if (accept && selected.Contains(revision.Id))
                    foreach (var dependency in revision.Dependencies) changed |= selected.Add(dependency);
                if (!accept && revision.Dependencies.Any(selected.Contains)) changed |= selected.Add(revision.Id);
            }
        } while (changed);
        return [.. selected.Order().Select(id => Revisions[id])];
    }

    /// <summary>Creates a detached original-content projection by rejecting every pending revision without changing the document or history.</summary>
    public RichDocumentSnapshot CreateOriginalReviewSnapshot() => CreateReviewProjection(RichReviewMode.Original).Snapshot;

    /// <summary>Captures final or original review content and snapshot-bound address maps without editing the document or history.</summary>
    public RichReviewProjection CreateReviewProjection(RichReviewMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == RichReviewMode.Final || Revisions.IsEmpty)
            return new(this, WithRevisions(ImmutableDictionary<long, RichRevision>.Empty).WithRevision(default), mode, new([]));
        var next = Nodes.Keys.Select(id => id.Value).DefaultIfEmpty().Max();
        var edit = new RichDocumentEdit(this, next);
        try
        {
            while (!edit.Snapshot.Revisions.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                edit.RejectRevision(edit.Snapshot.Revisions.Values.MinBy(revision => revision.Id)!.Info.Group);
            }
            var plan = edit.Finish();
            return new(this, plan.Snapshot, mode, plan.Map);
        }
        finally { edit.Close(); }
    }
}
