using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

// A revision retains sparse values in memory. Its RTF context contains only the
// complete top-level blocks needed to describe those values and their anchors.
// Unchanged blocks, unrelated styles and unrelated image payloads are excluded.
internal sealed record RtfRevisionContext(RichDocumentSnapshot Content, ImmutableHashSet<RichNodeId> Scaffolding)
{
    internal static RtfRevisionContext Create(RichDocumentSnapshot state, RichRevisionDelta delta, CancellationToken cancellationToken = default)
    {
        var roots = new HashSet<RichNodeId>();
        foreach (var id in delta.Nodes.Keys) Include(id);
        var bookmarks = state.Bookmarks.Where(pair => delta.Bookmarks.ContainsKey(pair.Key)).ToImmutableDictionary();
        var comments = state.Comments.Where(pair => delta.Comments.ContainsKey(pair.Key)).ToImmutableDictionary();
        foreach (var range in bookmarks.Values.Select(value => value.Range).Concat(comments.Values.Select(value => value.Range)).OfType<RichTextSelection>())
        { Include(range.Anchor.Paragraph); Include(range.Active.Paragraph); }
        var nodes = ImmutableDictionary.CreateBuilder<RichNodeId, RichNode>();
        var blocks = state.Get<RichStory>(state.Body).Blocks.Where(roots.Contains).ToImmutableList();
        foreach (var block in blocks) Copy(block);
        var scaffolding = ImmutableHashSet<RichNodeId>.Empty;
        if (blocks.IsEmpty)
        {
            var empty = new RichNodeId(state.Body.Value == 1 ? 2 : 1);
            nodes.Add(empty, new RichParagraph(empty, state.Body, []));
            blocks = blocks.Add(empty);
            scaffolding = scaffolding.Add(empty);
        }
        nodes.Add(state.Body, new RichStory(state.Body, blocks));
        var assets = nodes.Values.OfType<RichImage>().Select(image => image.Asset).Concat(delta.Assets.Keys)
            .Distinct().Where(state.Assets.ContainsKey).ToImmutableDictionary(id => id, id => state.Assets[id]);
        var styles = new HashSet<RichStyleId>();
        var pending = new Stack<RichStyleId>(nodes.Values.Select(node => node switch
        { RichParagraph paragraph => paragraph.Style, RichInline inline => inline.Style, _ => null }).OfType<RichStyleId>().Concat(delta.Styles.Keys));
        while (pending.TryPop(out var id))
        {
            if (!state.Styles.Definitions.TryGetValue(id, out var style) || !styles.Add(id)) continue;
            if (style.BasedOn is { } based) pending.Push(based);
            if (style.NextStyle is { } next) pending.Push(next);
        }
        return new(new(state.Body, nodes.ToImmutable(), new(styles.Select(id => state.Styles.Definitions[id])), assets: assets,
            bookmarks: bookmarks, comments: comments, defaultCharacter: state.DefaultCharacter, defaultParagraph: state.DefaultParagraph), scaffolding);

        void Include(RichNodeId id)
        {
            if (!state.Nodes.TryGetValue(id, out var node) || node is RichStory) return;
            while (node.Parent != state.Body) node = state.Get<RichNode>(node.Parent!.Value);
            roots.Add(node.Id);
        }
        void Copy(RichNodeId id)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = state.Get<RichNode>(id);
            nodes.Add(id, node);
            foreach (var child in RichDocumentSnapshot.Children(node)) Copy(child);
        }
    }

    internal static RichDocumentSnapshot Before(RichDocumentSnapshot after, RichRevisionDelta delta) => new(after.Body,
        Restore(after.Nodes, delta.Nodes), new(Restore(after.Styles.Definitions, delta.Styles).Values),
        assets: Restore(after.Assets, delta.Assets), bookmarks: Restore(after.Bookmarks, delta.Bookmarks), comments: Restore(after.Comments, delta.Comments),
        defaultCharacter: delta.DefaultCharacter?.Before ?? after.DefaultCharacter, defaultParagraph: delta.DefaultParagraph?.Before ?? after.DefaultParagraph);

    private static ImmutableDictionary<TKey, TValue> Restore<TKey, TValue>(ImmutableDictionary<TKey, TValue> current,
        ImmutableDictionary<TKey, RichRevisionValue<TValue>> changes) where TKey : notnull where TValue : class
    {
        var result = current.ToBuilder();
        foreach (var pair in changes)
            if (pair.Value.Before is { } before) result[pair.Key] = before;
            else result.Remove(pair.Key);
        return result.ToImmutable();
    }
}
