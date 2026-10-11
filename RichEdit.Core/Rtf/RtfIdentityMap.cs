using System.Collections.Immutable;
using System.Globalization;

namespace RichEdit.Core.Rtf;

// Identities connect retained RTF slices to live content. Content, formatting,
// resources and hierarchy still use the same RTF document codec.
internal sealed class RtfIdentityMap
{
    internal Dictionary<RichNodeId, long> Nodes { get; } = [];
    internal Dictionary<RichAssetId, long> Assets { get; } = [];
    internal Dictionary<RichAnnotationId, long> Bookmarks { get; } = [];
    internal Dictionary<RichAnnotationId, long> Comments { get; } = [];

    internal static IEnumerable<RichNode> Preorder(RichDocumentSnapshot snapshot)
    {
        var pending = new Stack<RichNodeId>();
        pending.Push(snapshot.Body);
        while (pending.TryPop(out var id))
        {
            var node = snapshot.Get<RichNode>(id);
            yield return node;
            foreach (var child in RichDocumentSnapshot.Children(node).Reverse()) pending.Push(child);
        }
    }

    internal static int Kind(RichNode node) => node switch
    {
        RichStory => 1, RichParagraph => 2, RichList => 3, RichListItem => 4, RichTable => 5,
        RichTableRow => 6, RichTableCell => 7, RichText => 8, RichSoftBreak => 9, RichImage => 10, RichField => 11,
        _ => throw new ArgumentException("Unsupported rich node."),
    };

    internal static long Number(ReadOnlySpan<char> text, bool zero = false)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < (zero ? 0 : 1))
            throw new FormatException("Invalid retained RTF identity.");
        return value;
    }
}

internal sealed class RtfReadBudget
{
    internal int Resources;
    internal int TextLength;
    internal long RevisionWork;
}
