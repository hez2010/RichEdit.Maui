using System.Collections.Immutable;
using RichEdit.Core;

namespace RichEdit.Maui;

// Neighboring structural blocks delimit a native text flow. New paragraphs
// created by Enter remain inside the same flow without inventing a child document.
internal sealed record RichTextViewScope(RichNodeId Root, RichNodeId? PreviousBlock = null, RichNodeId? NextBlock = null)
{
    internal RichTextProjection? Project(RichDocumentSnapshot snapshot)
    {
        if (!snapshot.Nodes.TryGetValue(Root, out var node)) return null;
        if (PreviousBlock is null && NextBlock is null) return snapshot.ProjectText(Root);
        if (node is not (RichStory or RichTableCell or RichListItem)) return null;
        var blocks = RichDocumentSnapshot.Children(node).ToImmutableArray();
        var first = PreviousBlock is { } previous ? blocks.IndexOf(previous) : -1;
        var last = NextBlock is { } next ? blocks.IndexOf(next) : blocks.Length;
        if (PreviousBlock is not null && first < 0 || last < 0 || first >= last) return null;
        return new(snapshot, Root, blocks.Slice(first + 1, last - first - 1));
    }

    internal RichSelection StartSelection(RichTextProjection projection)
    {
        if (projection.GetPoint(0) is { } point) return new RichEdit.Core.RichTextSelection(point, point);
        if (NextBlock is { } next) return new RichBoundarySelection(projection.Story, next, RichBlockEdge.Before);
        if (PreviousBlock is { } previous) return new RichBoundarySelection(projection.Story, previous, RichBlockEdge.After);
        throw new InvalidOperationException("The scope has no insertion boundary.");
    }

    internal static RichTextViewScope AroundParagraph(RichDocumentSnapshot snapshot, RichNodeId paragraph)
    {
        var node = snapshot.Get<RichParagraph>(paragraph);
        var parent = node.Parent!.Value;
        var blocks = RichDocumentSnapshot.Children(snapshot.Get<RichNode>(parent)).ToImmutableArray();
        var first = blocks.IndexOf(paragraph);
        var last = first;
        while (first > 0 && snapshot.Get<RichBlock>(blocks[first - 1]) is RichParagraph) first--;
        while (last + 1 < blocks.Length && snapshot.Get<RichBlock>(blocks[last + 1]) is RichParagraph) last++;
        return new(parent, first == 0 ? null : blocks[first - 1], last + 1 == blocks.Length ? null : blocks[last + 1]);
    }
}
