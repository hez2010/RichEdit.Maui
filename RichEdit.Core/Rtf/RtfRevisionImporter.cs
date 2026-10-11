using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

internal sealed class RtfRevisionImporter
{
    internal sealed record Context(RichDocumentSnapshot Snapshot, RtfIdentityMap Identities)
    {
        internal Dictionary<long, RichNodeId> Nodes { get; } = Identities.Nodes.Where(pair => pair.Value != 0).ToDictionary(pair => pair.Value, pair => pair.Key);
        internal Dictionary<long, RichAssetId> Assets { get; } = Identities.Assets.ToDictionary(pair => pair.Value, pair => pair.Key);
        internal Dictionary<long, RichAnnotationId> Bookmarks { get; } = Identities.Bookmarks.ToDictionary(pair => pair.Value, pair => pair.Key);
        internal Dictionary<long, RichAnnotationId> Comments { get; } = Identities.Comments.ToDictionary(pair => pair.Value, pair => pair.Key);
    }

    private readonly Context _main;
    private readonly long _body;
    private long _next;
    private readonly Dictionary<long, (RichNodeId Id, int Kind)> _nodes = [];
    private readonly Dictionary<long, RichAssetId> _assets = [];
    private readonly Dictionary<long, (RichAnnotationId Id, bool Comment)> _annotations = [];

    internal RtfRevisionImporter(Context main)
    {
        _main = main;
        _body = main.Identities.Nodes[main.Snapshot.Body];
        Include(main);
    }

    internal void Include(Context context)
    {
        if (context.Identities.Nodes[context.Snapshot.Body] != _body) throw new FormatException("Retained content belongs to another story.");
        foreach (var pair in context.Identities.Nodes)
        {
            if (pair.Value == 0) continue;
            var kind = RtfIdentityMap.Kind(context.Snapshot.Get<RichNode>(pair.Key));
            if (_nodes.TryGetValue(pair.Value, out var prior))
            { if (prior.Kind != kind) throw new FormatException("A retained identity changes structural kind."); }
            else _nodes.Add(pair.Value, (new(++_next), kind));
        }
        foreach (var id in context.Identities.Assets.Values)
            if (!_assets.ContainsKey(id)) _assets.Add(id, new(++_next));
        foreach (var id in context.Identities.Bookmarks.Values) Annotation(id, false);
        foreach (var id in context.Identities.Comments.Values) Annotation(id, true);
        void Annotation(long id, bool comment)
        {
            if (_annotations.TryGetValue(id, out var prior))
            { if (prior.Comment != comment) throw new FormatException("A retained annotation changes kind."); }
            else _annotations.Add(id, (new(++_next), comment));
        }
    }

    internal RichDocumentSnapshot MainSnapshot() => new(Node(_body),
        _main.Snapshot.Nodes.Values.Select(node => RemapNode(_main, node)).ToImmutableDictionary(node => node.Id), _main.Snapshot.Styles,
        assets: _main.Snapshot.Assets.Values.Select(asset => RemapAsset(_main, asset)).ToImmutableDictionary(asset => asset.Id),
        bookmarks: _main.Snapshot.Bookmarks.Values.Select(bookmark => RemapBookmark(_main, bookmark)).ToImmutableDictionary(bookmark => bookmark.Id),
        comments: _main.Snapshot.Comments.Values.Select(comment => RemapComment(_main, comment)).ToImmutableDictionary(comment => comment.Id),
        defaultCharacter: _main.Snapshot.DefaultCharacter, defaultParagraph: _main.Snapshot.DefaultParagraph);

    internal RichRevisionDelta ReadDelta(Context before, Context after, ImmutableArray<long> nodes, ImmutableArray<long> styles,
        ImmutableArray<long> assets, ImmutableArray<long> bookmarks, ImmutableArray<long> comments,
        ImmutableArray<long> beforeBlocks, ImmutableArray<long> afterBlocks, bool characterDefaults, bool paragraphDefaults)
    {
        if (beforeBlocks.IsDefault != afterBlocks.IsDefault || nodes.Contains(_body) != (!beforeBlocks.IsDefault && !afterBlocks.IsDefault))
            throw new FormatException("A changed story requires both ordered block reference lists.");
        return new()
        {
            Nodes = Values(nodes, Node, (context, id) => context.Nodes.TryGetValue(id, out var local)
                ? RemapNode(context, context.Snapshot.Get<RichNode>(local), id == _body ? ReferenceEquals(context, before) ? beforeBlocks : afterBlocks : default) : null),
            Styles = Values(styles, id => new RichStyleId(checked((int)id)), (context, id) => context.Snapshot.Styles.Definitions.GetValueOrDefault(new(checked((int)id)))),
            Assets = Values(assets, Asset, (context, id) => context.Assets.TryGetValue(id, out var local) ? RemapAsset(context, context.Snapshot.Assets[local]) : null),
            Bookmarks = Values(bookmarks, Annotation, (context, id) => context.Bookmarks.TryGetValue(id, out var local) ? RemapBookmark(context, context.Snapshot.Bookmarks[local]) : null),
            Comments = Values(comments, Annotation, (context, id) => context.Comments.TryGetValue(id, out var local) ? RemapComment(context, context.Snapshot.Comments[local]) : null),
            DefaultCharacter = characterDefaults ? new(before.Snapshot.DefaultCharacter, after.Snapshot.DefaultCharacter) : null,
            DefaultParagraph = paragraphDefaults ? new(before.Snapshot.DefaultParagraph, after.Snapshot.DefaultParagraph) : null,
        };

        ImmutableDictionary<TKey, RichRevisionValue<TValue>> Values<TKey, TValue>(ImmutableArray<long> keys, Func<long, TKey> map,
            Func<Context, long, TValue?> value) where TKey : notnull where TValue : class
        {
            var result = ImmutableDictionary.CreateBuilder<TKey, RichRevisionValue<TValue>>();
            foreach (var id in keys)
            {
                var old = value(before, id); var next = value(after, id);
                if (old is null && next is null) throw new FormatException("A changed value is missing from both retained contexts.");
                result.Add(map(id), new(old, next));
            }
            return result.ToImmutable();
        }
    }

    private RichNodeId Node(long wire) => _nodes.TryGetValue(wire, out var value) ? value.Id : throw new FormatException("Missing retained node reference.");
    private RichAssetId Asset(long wire) => _assets.TryGetValue(wire, out var value) ? value : throw new FormatException("Missing retained image reference.");
    private RichAnnotationId Annotation(long wire) => _annotations.TryGetValue(wire, out var value) ? value.Id : throw new FormatException("Missing retained annotation reference.");
    private RichNodeId LocalNode(Context context, RichNodeId local) => Node(context.Identities.Nodes[local]);

    private RichNode RemapNode(Context context, RichNode node, ImmutableArray<long> bodyBlocks = default)
    {
        var mapped = node switch
        {
            RichStory story => (RichNode)(story with { Blocks = bodyBlocks.IsDefault ? Map(story.Blocks) : bodyBlocks.Select(Node).ToImmutableList() }),
            RichParagraph paragraph => paragraph with { Inlines = Map(paragraph.Inlines) },
            RichList list => list with { Items = Map(list.Items) },
            RichListItem item => item with { Blocks = Map(item.Blocks) },
            RichTable table => table with { Rows = Map(table.Rows) },
            RichTableRow row => row with { Cells = Map(row.Cells) },
            RichTableCell cell => cell with { Blocks = Map(cell.Blocks) },
            RichImage image => image with { Asset = Asset(context.Identities.Assets[image.Asset]) },
            _ => node,
        };
        return mapped with { Id = LocalNode(context, node.Id), Parent = node.Parent is { } parent ? LocalNode(context, parent) : null };
        ImmutableList<RichNodeId> Map(ImmutableList<RichNodeId> ids) => ids.Select(id => LocalNode(context, id)).ToImmutableList();
    }

    private RichImageAsset RemapAsset(Context context, RichImageAsset asset) => asset with { Id = Asset(context.Identities.Assets[asset.Id]) };
    private RichBookmark RemapBookmark(Context context, RichBookmark bookmark) => bookmark with
    { Id = Annotation(context.Identities.Bookmarks[bookmark.Id]), Range = RemapRange(context, bookmark.Range) };
    private RichComment RemapComment(Context context, RichComment comment) => comment with
    { Id = Annotation(context.Identities.Comments[comment.Id]), Range = RemapRange(context, comment.Range) };
    private RichTextSelection? RemapRange(Context context, RichTextSelection? range)
    {
        return range is null ? null : new(Point(range.Anchor), Point(range.Active));
        RichTextPoint Point(RichTextPoint point) => point with
        { Story = LocalNode(context, point.Story), Paragraph = LocalNode(context, point.Paragraph) };
    }

    internal void ValidateAfter(RichDocumentSnapshot state, RichRevisionDelta delta)
    {
        Validate(state.Nodes, delta.Nodes, RichDocumentSnapshot.SameNode);
        Validate(state.Styles.Definitions, delta.Styles);
        Validate(state.Assets, delta.Assets, (first, second) => first.Kind == second.Kind && first.Data.AsSpan().SequenceEqual(second.Data.AsSpan()));
        Validate(state.Bookmarks, delta.Bookmarks);
        Validate(state.Comments, delta.Comments);
        if (delta.DefaultCharacter is { } character && character.After != state.DefaultCharacter ||
            delta.DefaultParagraph is { } paragraph && paragraph.After != state.DefaultParagraph)
            throw new FormatException("Retained defaults do not match the current revision state.");

        static void Validate<TKey, TValue>(ImmutableDictionary<TKey, TValue> current, ImmutableDictionary<TKey, RichRevisionValue<TValue>> changes,
            Func<TValue, TValue, bool>? same = null) where TKey : notnull where TValue : class
        {
            foreach (var pair in changes)
            {
                var value = current.GetValueOrDefault(pair.Key);
                var expected = pair.Value.After;
                if (value is null && expected is null) continue;
                if (value is null || expected is null || !(same?.Invoke(value, expected) ?? EqualityComparer<TValue>.Default.Equals(value, expected)))
                    throw new FormatException("Retained content does not match the current revision state.");
            }
        }
    }
}
