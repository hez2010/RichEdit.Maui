using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

internal sealed record RtfRevisionExport(RichRevision Revision, RtfRevisionContext Before, RtfRevisionContext After,
    ImmutableList<RichNodeId>? BeforeBlocks, ImmutableList<RichNodeId>? AfterBlocks)
{
    internal static IEnumerable<RtfRevisionExport> Create(RichDocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Revisions.IsEmpty) yield break;
        var current = snapshot.WithRevisions(ImmutableDictionary<long, RichRevision>.Empty);
        foreach (var revision in snapshot.Revisions.Values.OrderByDescending(revision => revision.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = RtfRevisionContext.Before(current, revision.Delta);
            var root = revision.Nodes.GetValueOrDefault(snapshot.Body);
            yield return new(revision, RtfRevisionContext.Create(before, revision.Delta, cancellationToken), RtfRevisionContext.Create(current, revision.Delta, cancellationToken),
                (root?.Before as RichStory)?.Blocks, (root?.After as RichStory)?.Blocks);
            current = before;
        }
    }
}
