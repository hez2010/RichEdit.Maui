using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>An immutable structured rich-document graph with one body story and document-owned styles.</summary>
public sealed class RichDocumentSnapshot
{
    /// <summary>Gets an immutable empty body containing one empty paragraph.</summary>
    public static RichDocumentSnapshot Empty { get; } = new(new(1), new RichNode[]
    {
        new RichStory(new(1), [new(2)]), new RichParagraph(new(2), new(1), []),
    }.ToImmutableDictionary(node => node.Id), RichStyleSheet.Empty);

    /// <summary>Constructs and validates a detached immutable snapshot, suitable for background construction before adoption.</summary>
    public static RichDocumentSnapshot Build(Action<RichDocumentEdit> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var edit = new RichDocumentEdit(Empty, 2);
        try { build(edit); return edit.Finish().Snapshot; }
        finally { edit.Close(); }
    }

    /// <summary>Creates semantic paragraphs and soft breaks from plain text. Paragraph terminators normalize to rich structure.</summary>
    public static RichDocumentSnapshot FromPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var nodes = ImmutableDictionary.CreateBuilder<RichNodeId, RichNode>();
        var paragraphs = ImmutableList.CreateBuilder<RichNodeId>();
        var next = 1L;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var paragraph = new RichNodeId(++next);
            var inlines = ImmutableList.CreateBuilder<RichNodeId>();
            var pieces = line.Split('\u2028');
            for (var index = 0; index < pieces.Length; index++)
            {
                if (index != 0)
                {
                    var id = new RichNodeId(++next);
                    nodes.Add(id, new RichSoftBreak(id, paragraph));
                    inlines.Add(id);
                }
                if (pieces[index].Length != 0)
                {
                    var id = new RichNodeId(++next);
                    nodes.Add(id, new RichText(id, paragraph, TextSnapshot.FromText(pieces[index])));
                    inlines.Add(id);
                }
            }
            nodes.Add(paragraph, new RichParagraph(paragraph, new(1), inlines.ToImmutable()));
            paragraphs.Add(paragraph);
        }
        nodes.Add(new(1), new RichStory(new(1), paragraphs.ToImmutable()));
        return new(new(1), nodes.ToImmutable(), RichStyleSheet.Empty);
    }

    internal RichDocumentSnapshot(RichNodeId body, ImmutableDictionary<RichNodeId, RichNode> nodes, RichStyleSheet styles,
        DocumentRevision revision = default, bool validate = true, ImmutableDictionary<RichAssetId, RichImageAsset>? assets = null)
    {
        Body = body;
        Nodes = nodes;
        Styles = styles;
        Assets = assets ?? ImmutableDictionary<RichAssetId, RichImageAsset>.Empty;
        Revision = revision;
        if (validate) Validate();
    }

    /// <summary>Gets the body story identity.</summary>
    public RichNodeId Body { get; }
    /// <summary>Gets structural nodes indexed by their stable identities.</summary>
    public ImmutableDictionary<RichNodeId, RichNode> Nodes { get; }
    /// <summary>Gets document-owned reusable styles.</summary>
    public RichStyleSheet Styles { get; }
    /// <summary>Gets immutable document-owned image resources.</summary>
    public ImmutableDictionary<RichAssetId, RichImageAsset> Assets { get; }
    /// <summary>Gets the document revision, or an invalid revision for a standalone builder snapshot.</summary>
    public DocumentRevision Revision { get; }
    internal RichDocumentSnapshot WithRevision(DocumentRevision revision) => new(Body, Nodes, Styles, revision, validate: false, Assets);

    /// <summary>Gets a typed node or rejects a missing node or mismatched structural kind.</summary>
    public T Get<T>(RichNodeId id) where T : RichNode => Nodes.TryGetValue(id, out var node) && node is T value ? value :
        throw new ArgumentException("The node does not exist with the requested kind.", nameof(id));

    /// <summary>Gets the paragraph-local UTF-16 length, including one position for each soft break.</summary>
    public int GetParagraphLength(RichNodeId id)
    {
        var length = 0;
        foreach (var inline in Get<RichParagraph>(id).Inlines) length = checked(length + InlineLength(Get<RichInline>(inline)));
        return length;
    }

    internal static int InlineLength(RichInline inline) => inline switch
    {
        RichText text => text.Content.Length,
        RichSoftBreak => 1,
        RichImage => 1,
        _ => throw new ArgumentException("Unsupported inline node."),
    };

    /// <summary>Validates a rich point against its exact story, paragraph, UTF-16 length, and affinity.</summary>
    public void ValidatePoint(RichTextPoint point)
    {
        Get<RichStory>(point.Story);
        var paragraph = Get<RichParagraph>(point.Paragraph);
        if (!Enum.IsDefined(point.Affinity) || point.Offset < 0 || point.Offset > GetParagraphLength(paragraph.Id))
            throw new ArgumentOutOfRangeException(nameof(point));
        RichNode node = paragraph;
        while (node.Parent is { } parent) node = Get<RichNode>(parent);
        if (node.Id != point.Story) throw new ArgumentException("The point's paragraph does not belong to its story.", nameof(point));
    }

    internal RichSelection NormalizeSelection(RichSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        switch (selection)
        {
            case RichTextSelection text:
                ValidatePoint(text.Anchor);
                ValidatePoint(text.Active);
                if (text.Anchor.Story != text.Active.Story) throw new ArgumentException("A text selection must belong to one story.", nameof(selection));
                return text;
            case RichCellSelection cells: return GetTableGrid(cells.Table).Expand(cells);
            case RichObjectSelection selected when Nodes.GetValueOrDefault(selected.Object) is RichImage: return selected;
            default: throw new ArgumentException("This selection does not address a supported authored object.", nameof(selection));
        }
    }

    internal RichTextSelection FirstSelection()
    {
        var pending = new Stack<RichNodeId>();
        pending.Push(Body);
        while (pending.TryPop(out var id))
        {
            var node = Get<RichNode>(id);
            if (node is RichParagraph paragraph)
            {
                var point = new RichTextPoint(Body, paragraph.Id, 0);
                return new(point, point);
            }
            foreach (var child in Children(node).Reverse()) pending.Push(child);
        }
        throw new InvalidOperationException("A valid rich document must contain a paragraph.");
    }

    /// <summary>Explicitly projects a story to text with snapshot-bound mappings for its paragraphs and structural separators.</summary>
    public RichTextProjection ProjectText(RichNodeId? story = null) => new(this, story ?? Body);

    internal static IEnumerable<RichNodeId> Children(RichNode node) => node switch
    {
        RichStory story => story.Blocks,
        RichParagraph paragraph => paragraph.Inlines,
        RichList list => list.Items,
        RichListItem item => item.Blocks,
        RichTable table => table.Rows,
        RichTableRow row => row.Cells,
        RichTableCell cell => cell.Blocks,
        _ => [],
    };

    private void Validate()
    {
        ArgumentNullException.ThrowIfNull(Styles);
        foreach (var pair in Assets)
            if (!pair.Key.IsValid || pair.Value is null || pair.Value.Id != pair.Key || !Enum.IsDefined(pair.Value.Kind) || pair.Value.Data.IsDefaultOrEmpty)
                throw new ArgumentException("Image resources require valid matching identities, formats, and owned payloads.");
        if (Get<RichStory>(Body).Parent is not null) throw new ArgumentException("The body must be a root story.");
        var visited = new HashSet<RichNodeId>();
        var pending = new Stack<(RichNode Node, int Depth)>();
        pending.Push((Get<RichStory>(Body), 0));
        while (pending.TryPop(out var item))
        {
            var (node, depth) = item;
            if (depth > 128) throw new ArgumentException("Rich structural nesting cannot exceed 128 levels.");
            if (!node.Id.IsValid || !visited.Add(node.Id)) throw new ArgumentException("Structural identities must form a tree without cycles or repeated children.");
            if (!ReferenceEquals(Nodes.GetValueOrDefault(node.Id), node)) throw new ArgumentException("Node dictionary identities do not match their values.");
            ValidateNode(node);
            foreach (var id in Children(node))
            {
                var child = Get<RichNode>(id);
                if (child.Parent != node.Id || !Accepts(node, child)) throw new ArgumentException("A node has an invalid parent or child kind.");
                pending.Push((child, depth + 1));
            }
        }
        if (visited.Count != Nodes.Count) throw new ArgumentException("All authored nodes must be reachable from the body story.");
        foreach (var table in Nodes.Values.OfType<RichTable>()) _ = GetTableGrid(table.Id);
    }

    private static bool Accepts(RichNode parent, RichNode child) => parent switch
    {
        RichStory or RichListItem or RichTableCell => child is RichBlock,
        RichParagraph => child is RichInline,
        RichList => child is RichListItem,
        RichTable => child is RichTableRow,
        RichTableRow => child is RichTableCell,
        _ => false,
    };

    private void ValidateNode(RichNode node)
    {
        if (node is not (RichStory or RichParagraph or RichList or RichListItem or RichTable or RichTableRow or RichTableCell or RichText or RichSoftBreak or RichImage))
            throw new ArgumentException("Unsupported authored node kind.");
        if (node is RichStory { Blocks: null } or RichParagraph { Inlines: null } or RichList { Items: null } or
            RichListItem { Blocks: null } or RichTable { Rows: null } or RichTableRow { Cells: null } or RichTableCell { Blocks: null })
            throw new ArgumentException("Structural child collections cannot be null.");
        if (node is RichInline inline)
        {
            ArgumentNullException.ThrowIfNull(inline.Format);
            RichStyleSheet.Validate(inline.Format);
            if (inline.Style is { } id && (!Styles.Definitions.TryGetValue(id, out var style) || style.Kind != RichStyleKind.Character))
                throw new ArgumentException("Inline styles must reference character definitions.");
        }
        switch (node)
        {
            case RichStory { Blocks.Count: 0 } or RichListItem { Blocks.Count: 0 } or RichTableCell { Blocks.Count: 0 }:
                throw new ArgumentException("Text containers require at least one block.");
            case RichList { Items.Count: 0 } or RichList { StartNumber: < 0 }:
                throw new ArgumentException("Lists require items and a nonnegative starting number.");
            case RichTable table when table.Columns.IsDefaultOrEmpty || table.Rows.Count == 0 || table.Columns.Any(width => width.Twips <= 0):
                throw new ArgumentException("Tables require rows and positive column widths.");
            case RichText text:
                ArgumentNullException.ThrowIfNull(text.Content);
                foreach (var chunk in text.Content.GetChunks(new(0, text.Content.Length)))
                    if (chunk.Span.ContainsAny('\r', '\n', '\u2028'))
                        throw new ArgumentException("Text runs cannot contain paragraph terminators or soft-break characters.");
                break;
            case RichImage image when !Assets.ContainsKey(image.Asset) || image.Width.Twips <= 0 || image.Height.Twips <= 0:
                throw new ArgumentException("Images require an existing resource and positive authored dimensions.");
            case RichParagraph paragraph:
                ArgumentNullException.ThrowIfNull(paragraph.Format);
                ArgumentNullException.ThrowIfNull(paragraph.Character);
                RichStyleSheet.Validate(paragraph.Format);
                RichStyleSheet.Validate(paragraph.Character);
                if (paragraph.Style is { } id && (!Styles.Definitions.TryGetValue(id, out var style) || style.Kind != RichStyleKind.Paragraph))
                    throw new ArgumentException("Paragraph styles must reference paragraph definitions.");
                break;
        }
    }

    /// <summary>Gets a validated table grid, with every covered position referring to its owning cell identity.</summary>
    public RichTableGrid GetTableGrid(RichNodeId table) => new(this, Get<RichTable>(table));
}
