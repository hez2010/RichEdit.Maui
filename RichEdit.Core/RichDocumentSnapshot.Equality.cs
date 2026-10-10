namespace RichEdit.Core;

public sealed partial class RichDocumentSnapshot
{
    internal bool ContentEquals(RichDocumentSnapshot other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (Body != other.Body || DefaultCharacter != other.DefaultCharacter || DefaultParagraph != other.DefaultParagraph ||
            Nodes.Count != other.Nodes.Count || Styles.Definitions.Count != other.Styles.Definitions.Count || Assets.Count != other.Assets.Count ||
            Comments.Count != other.Comments.Count || Bookmarks.Count != other.Bookmarks.Count) return false;
        foreach (var pair in Styles.Definitions)
            if (other.Styles.Definitions.GetValueOrDefault(pair.Key) != pair.Value) return false;
        foreach (var pair in Assets)
            if (other.Assets.GetValueOrDefault(pair.Key) is not { } asset ||
                !ReferenceEquals(asset, pair.Value) && (asset.Kind != pair.Value.Kind || !asset.Data.AsSpan().SequenceEqual(pair.Value.Data.AsSpan()))) return false;
        foreach (var pair in Comments) if (other.Comments.GetValueOrDefault(pair.Key) != pair.Value) return false;
        foreach (var pair in Bookmarks) if (other.Bookmarks.GetValueOrDefault(pair.Key) != pair.Value) return false;
        foreach (var pair in Nodes)
            if (!other.Nodes.TryGetValue(pair.Key, out var node) || !SameNode(pair.Value, node)) return false;
        return true;
    }

    private static bool SameNode(RichNode first, RichNode second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first.Id != second.Id || first.Parent != second.Parent) return false;
        return (first, second) switch
        {
            (RichStory a, RichStory b) => a.Blocks.SequenceEqual(b.Blocks),
            (RichParagraph a, RichParagraph b) => a.Inlines.SequenceEqual(b.Inlines) && a with { Inlines = b.Inlines } == b,
            (RichText a, RichText b) => a.Content.ContentEquals(b.Content) && a with { Content = b.Content } == b,
            (RichList a, RichList b) => a.Items.SequenceEqual(b.Items) && a with { Items = b.Items } == b,
            (RichListItem a, RichListItem b) => a.Blocks.SequenceEqual(b.Blocks),
            (RichTable a, RichTable b) => a.Rows.SequenceEqual(b.Rows) && a.Columns.AsSpan().SequenceEqual(b.Columns.AsSpan()),
            (RichTableRow a, RichTableRow b) => a.Cells.SequenceEqual(b.Cells),
            (RichTableCell a, RichTableCell b) => a.Blocks.SequenceEqual(b.Blocks) && a with { Blocks = b.Blocks } == b,
            _ => first == second,
        };
    }
}
