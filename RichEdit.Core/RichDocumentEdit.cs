using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>Builds one schema-validated rich transaction using structural identities and paragraph-local text addresses.</summary>
public sealed partial class RichDocumentEdit
{
    private readonly RichNodeId _body;
    private readonly ImmutableDictionary<RichNodeId, RichNode>.Builder _nodes;
    private RichStyleSheet _styles;
    private readonly ImmutableDictionary<RichAssetId, RichImageAsset>.Builder _assets;
    private readonly ImmutableDictionary<RichAnnotationId, RichBookmark>.Builder _bookmarks;
    private readonly ImmutableDictionary<RichAnnotationId, RichComment>.Builder _comments;
    private RichCharacterStyle _defaultCharacter;
    private RichParagraphStyle _defaultParagraph;
    private long _nextId;
    private bool _open = true;
    private readonly List<RichAddressMap.Step> _maps = [];
    internal RichSelection? SelectionAfter { get; private set; }

    internal RichDocumentEdit(RichDocumentSnapshot before, long nextId)
    {
        _body = before.Body;
        _nodes = before.Nodes.ToBuilder();
        _styles = before.Styles;
        _assets = before.Assets.ToBuilder();
        _bookmarks = before.Bookmarks.ToBuilder();
        _comments = before.Comments.ToBuilder();
        _defaultCharacter = before.DefaultCharacter;
        _defaultParagraph = before.DefaultParagraph;
        _nextId = nextId;
    }
    /// <summary>Gets the body story identity.</summary>
    public RichNodeId Body { get { VerifyOpen(); return _body; } }
    /// <summary>Gets the current candidate as an immutable, validated snapshot.</summary>
    public RichDocumentSnapshot Snapshot { get { VerifyOpen(); return Candidate(); } }
    /// <summary>Replaces document-owned styles without rewriting referenced content.</summary>
    public void SetStyles(RichStyleSheet styles) { VerifyOpen(); _styles = styles ?? throw new ArgumentNullException(nameof(styles)); }

    /// <summary>Changes authored document character defaults without rewriting text or style references.</summary>
    public void SetDefaultCharacterFormat(RichCharacterStyle format)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        _defaultCharacter = format;
    }

    /// <summary>Changes authored document paragraph defaults without rewriting paragraph nodes.</summary>
    public void SetDefaultParagraphFormat(RichParagraphStyle format)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        _defaultParagraph = format;
    }

    /// <summary>Sets the invoking view's selection in final candidate coordinates.</summary>
    public void SetSelection(RichSelection selection) { VerifyOpen(); SelectionAfter = selection ?? throw new ArgumentNullException(nameof(selection)); }

    /// <summary>Inserts a paragraph into a story, list item, or cell at a zero-based block index.</summary>
    public RichNodeId InsertParagraph(RichNodeId parent, int index, string text = "", RichStyleId? style = null)
    {
        VerifyOpen();
        var blocks = Blocks(Get<RichNode>(parent));
        if (index < 0 || index > blocks.Count) throw new ArgumentOutOfRangeException(nameof(index));
        ValidateText(text);
        var id = Allocate();
        var inlines = ImmutableList<RichNodeId>.Empty;
        if (text.Length != 0)
        {
            var run = Allocate();
            _nodes.Add(run, new RichText(run, id, TextSnapshot.FromText(text)));
            inlines = inlines.Add(run);
        }
        _nodes.Add(id, new RichParagraph(id, parent, inlines) { Style = style });
        SetBlocks(parent, blocks.Insert(index, id));
        return id;
    }

    /// <summary>Materializes an authored paragraph at a structural caret boundary, without changing the adjacent block.</summary>
    public RichNodeId InsertParagraph(RichBoundarySelection boundary, string text = "", RichStyleId? style = null)
    {
        VerifyOpen();
        Candidate(validate: false).NormalizeSelection(boundary);
        var block = Get<RichBlock>(boundary.Block);
        var parent = block.Parent!.Value;
        var index = Blocks(Get<RichNode>(parent)).IndexOf(block.Id) + (boundary.Edge == RichBlockEdge.After ? 1 : 0);
        return InsertParagraph(parent, index, text, style);
    }

    /// <summary>Changes direct paragraph properties without changing its identity or text.</summary>
    public void SetParagraphFormat(RichNodeId paragraph, RichParagraphStyle format)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        var node = Get<RichParagraph>(paragraph);
        _nodes[paragraph] = node with { Format = format };
    }

    /// <summary>Assigns a reusable paragraph style. References are validated before the transaction can commit.</summary>
    public void SetParagraphStyle(RichNodeId paragraph, RichStyleId? style)
    {
        VerifyOpen();
        _nodes[paragraph] = Get<RichParagraph>(paragraph) with { Style = style };
    }

    /// <summary>Replaces an exact UTF-16 range inside one paragraph, retaining unaffected inline styles and identities.</summary>
    /// <remarks>Paragraph and soft-break characters require their explicit structural commands.</remarks>
    public void ReplaceText(RichTextPoint start, int length, string text)
    {
        VerifyOpen();
        ValidatePoint(start);
        ValidateText(text);
        var paragraph = Get<RichParagraph>(start.Paragraph);
        new TextSpan(start.Offset, length).Validate(Length(paragraph));
        if (IsPlainTextRange(paragraph, new(start.Offset, length)) && ReadText(paragraph, new(start.Offset, length)) == text) return;
        var inherited = InlineAt(paragraph, start.Offset);
        var (left, remainder) = Partition(paragraph, start.Offset, paragraph.Id);
        var (removed, right) = Partition(paragraph with { Inlines = remainder }, length, paragraph.Id);
        foreach (var id in removed) _nodes.Remove(id);
        if (text.Length != 0)
        {
            var id = Allocate();
            _nodes[id] = new RichText(id, paragraph.Id, TextSnapshot.FromText(text))
            { Style = inherited?.Style, Format = inherited?.Format ?? new(), Link = inherited?.Link };
            left = left.Add(id);
        }
        _nodes[paragraph.Id] = paragraph with { Inlines = Coalesce(left.AddRange(right)) };
        Record(new RichAddressMap.Text(paragraph.Id, start.Offset, length, text.Length));
    }

    /// <summary>Inserts an authored soft line break without creating a paragraph boundary.</summary>
    public RichNodeId InsertSoftBreak(RichTextPoint point)
    {
        VerifyOpen();
        ValidatePoint(point);
        var paragraph = Get<RichParagraph>(point.Paragraph);
        var inherited = InlineAt(paragraph, point.Offset);
        var (left, right) = Partition(paragraph, point.Offset, paragraph.Id);
        var id = Allocate();
        _nodes.Add(id, new RichSoftBreak(id, paragraph.Id) { Style = inherited?.Style, Format = inherited?.Format ?? new(), Link = inherited?.Link });
        _nodes[paragraph.Id] = paragraph with { Inlines = left.Add(id).AddRange(right) };
        Record(new RichAddressMap.Text(paragraph.Id, point.Offset, 0, 1));
        return id;
    }

    /// <summary>Sets direct character overrides on a bounded paragraph-local range.</summary>
    public void SetCharacterFormat(RichTextPoint start, int length, RichCharacterStyle format)
    {
        VerifyOpen();
        ValidatePoint(start);
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        var paragraph = Get<RichParagraph>(start.Paragraph);
        new TextSpan(start.Offset, length).Validate(Length(paragraph));
        if (length == 0) return;
        var (left, remainder) = Partition(paragraph, start.Offset, paragraph.Id);
        var (middle, right) = Partition(paragraph with { Inlines = remainder }, length, paragraph.Id);
        foreach (var id in middle) _nodes[id] = Get<RichInline>(id) with { Format = format };
        _nodes[paragraph.Id] = paragraph with { Inlines = Coalesce(left.AddRange(middle).AddRange(right)) };
    }

    /// <summary>Splits a paragraph at an exact local text boundary, retaining the original identity on the first part.</summary>
    public RichNodeId SplitParagraph(RichTextPoint point)
    {
        VerifyOpen();
        ValidatePoint(point);
        var paragraph = Get<RichParagraph>(point.Paragraph);
        var parent = paragraph.Parent!.Value;
        var siblings = Blocks(Get<RichNode>(parent));
        var index = siblings.IndexOf(paragraph.Id);
        var next = Allocate();
        var (first, second) = Partition(paragraph, point.Offset, next);
        var style = paragraph.Style;
        if (style is { } id && _styles.Definitions.TryGetValue(id, out var definition)) style = definition.NextStyle ?? id;
        _nodes[paragraph.Id] = paragraph with { Inlines = first };
        _nodes.Add(next, paragraph with { Id = next, Inlines = second, Style = style });
        SetBlocks(parent, siblings.Insert(index + 1, next));
        Record(new RichAddressMap.Split(paragraph.Id, next, point.Offset));
        return next;
    }

    /// <summary>Joins adjacent paragraphs in the same text container. Joining across a cell or list boundary is rejected.</summary>
    public void JoinParagraphs(RichNodeId first, RichNodeId second)
    {
        VerifyOpen();
        var left = Get<RichParagraph>(first);
        var right = Get<RichParagraph>(second);
        if (left.Parent != right.Parent || first == second) throw new ArgumentException("Paragraphs must share one text container.");
        var parent = left.Parent!.Value;
        var siblings = Blocks(Get<RichNode>(parent));
        var index = siblings.IndexOf(first);
        if (index < 0 || index + 1 >= siblings.Count || siblings[index + 1] != second)
            throw new ArgumentException("Paragraphs must be adjacent in document order.");
        var length = Length(left);
        foreach (var id in right.Inlines) _nodes[id] = Reparent(Get<RichNode>(id), first);
        _nodes[first] = left with { Inlines = Coalesce(left.Inlines.AddRange(right.Inlines)) };
        _nodes.Remove(second);
        SetBlocks(parent, siblings.RemoveAt(index + 1));
        Record(new RichAddressMap.Join(second, first, length));
    }

    /// <summary>Moves a block to a new position while preserving all identities and text addresses.</summary>
    /// <remarks>The insertion index addresses the destination before removal of the moved block.</remarks>
    public void MoveBlock(RichNodeId block, RichNodeId destination, int index)
    {
        VerifyOpen();
        var node = Get<RichBlock>(block);
        var target = Get<RichNode>(destination);
        var blocks = Blocks(target);
        if (index < 0 || index > blocks.Count) throw new ArgumentOutOfRangeException(nameof(index));
        for (var current = target; ; current = Get<RichNode>(current.Parent!.Value))
        {
            if (current.Id == block) throw new ArgumentException("A block cannot move into its own descendants.", nameof(destination));
            if (current.Parent is null) break;
        }
        var parent = node.Parent!.Value;
        var source = Blocks(Get<RichNode>(parent));
        var oldIndex = source.IndexOf(block);
        if (parent == destination)
        {
            if (index > oldIndex) index--;
            SetBlocks(parent, source.RemoveAt(oldIndex).Insert(index, block));
            return;
        }
        SetBlocks(parent, source.RemoveAt(oldIndex));
        EnsureParagraph(parent);
        _nodes[block] = Reparent(node, destination);
        SetBlocks(destination, blocks.Insert(index, block));
    }

    /// <summary>Deletes one complete block and its descendants, leaving an empty paragraph when its container would become empty.</summary>
    public void DeleteBlock(RichNodeId block)
    {
        VerifyOpen();
        var node = Get<RichBlock>(block);
        var parent = node.Parent!.Value;
        SetBlocks(parent, Blocks(Get<RichNode>(parent)).Remove(block));
        var removed = ImmutableHashSet.CreateBuilder<RichNodeId>();
        var pending = new Stack<RichNodeId>();
        pending.Push(block);
        while (pending.TryPop(out var id))
        {
            foreach (var child in RichDocumentSnapshot.Children(Get<RichNode>(id))) pending.Push(child);
            removed.Add(id);
            _nodes.Remove(id);
        }
        EnsureParagraph(parent);
        Record(new RichAddressMap.Remove(removed.ToImmutable()));
    }

    private (ImmutableList<RichNodeId> First, ImmutableList<RichNodeId> Second) Partition(RichParagraph paragraph, int offset, RichNodeId secondParent)
    {
        var first = ImmutableList.CreateBuilder<RichNodeId>();
        var second = ImmutableList.CreateBuilder<RichNodeId>();
        var position = 0;
        foreach (var id in paragraph.Inlines)
        {
            var inline = Get<RichInline>(id);
            var end = checked(position + RichDocumentSnapshot.InlineLength(inline));
            if (end <= offset) first.Add(id);
            else if (position >= offset)
            {
                second.Add(id);
                _nodes[id] = Reparent(inline, secondParent);
            }
            else if (inline is RichText text)
            {
                var local = offset - position;
                _nodes[id] = text with { Content = text.Content.Slice(new(0, local)) };
                first.Add(id);
                var next = Allocate();
                _nodes.Add(next, text with { Id = next, Parent = secondParent, Content = text.Content.Slice(new(local, text.Content.Length - local)) });
                second.Add(next);
            }
            else throw new ArgumentException("A split cannot divide an inline atom.");
            position = end;
        }
        return (first.ToImmutable(), second.ToImmutable());
    }

    private void ValidatePoint(RichTextPoint point)
    {
        if (point.Story != _body || !Enum.IsDefined(point.Affinity) || point.Offset < 0 || point.Offset > Length(Get<RichParagraph>(point.Paragraph)))
            throw new ArgumentException("The point must address the current story and a bounded paragraph offset.", nameof(point));
    }
    private ImmutableList<RichNodeId> Coalesce(ImmutableList<RichNodeId> inlines)
    {
        var result = ImmutableList.CreateBuilder<RichNodeId>();
        foreach (var id in inlines)
        {
            if (result.Count != 0 && Get<RichInline>(result[^1]) is RichText before && Get<RichInline>(id) is RichText after &&
                before.Style == after.Style && before.Format == after.Format && before.Link == after.Link)
            {
                _nodes[before.Id] = before with { Content = before.Content.Concat(after.Content) };
                _nodes.Remove(id);
            }
            else result.Add(id);
        }
        return result.ToImmutable();
    }
    private int Length(RichParagraph paragraph) => checked(paragraph.Inlines.Sum(id => RichDocumentSnapshot.InlineLength(Get<RichInline>(id))));
    private RichInline? InlineAt(RichParagraph paragraph, int offset)
    {
        RichInline? previous = null;
        var position = 0;
        foreach (var id in paragraph.Inlines)
        {
            var inline = Get<RichInline>(id);
            var end = position + RichDocumentSnapshot.InlineLength(inline);
            if (offset > position && offset <= end || position == 0 && offset == 0) return inline;
            position = end;
            previous = inline;
        }
        return previous;
    }
    private string ReadText(RichParagraph paragraph, TextSpan range)
    {
        var result = new System.Text.StringBuilder(range.Length);
        var position = 0;
        foreach (var id in paragraph.Inlines)
        {
            var inline = Get<RichInline>(id);
            var end = position + RichDocumentSnapshot.InlineLength(inline);
            var first = Math.Max(position, range.Start);
            var last = Math.Min(end, range.End);
            if (first < last)
            {
                if (inline is RichText text) result.Append(text.Content.GetText(new(first - position, last - first)));
                else result.Append(inline is RichSoftBreak ? '\u2028' : '\uFFFC');
            }
            if (end >= range.End) break;
            position = end;
        }
        return result.ToString();
    }
    private bool IsPlainTextRange(RichParagraph paragraph, TextSpan range)
    {
        var position = 0;
        foreach (var id in paragraph.Inlines)
        {
            var inline = Get<RichInline>(id);
            var end = position + RichDocumentSnapshot.InlineLength(inline);
            if (Math.Max(position, range.Start) < Math.Min(end, range.End) && inline is not RichText) return false;
            if (end >= range.End) break;
            position = end;
        }
        return true;
    }
    private T Get<T>(RichNodeId id) where T : RichNode => _nodes.TryGetValue(id, out var node) && node is T value ? value :
        throw new ArgumentException("The structural node does not exist with the requested kind.", nameof(id));
    private RichNodeId Allocate() => new(checked(++_nextId));
    private static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.AsSpan().ContainsAny('\r', '\n', '\u2028')) throw new ArgumentException("Use structural paragraph and soft-break operations for line separators.", nameof(text));
    }
    private static ImmutableList<RichNodeId> Blocks(RichNode node) => node switch
    {
        RichStory story => story.Blocks,
        RichListItem item => item.Blocks,
        RichTableCell cell => cell.Blocks,
        _ => throw new ArgumentException("This node is not a block container.", nameof(node)),
    };
    private void SetBlocks(RichNodeId parent, ImmutableList<RichNodeId> blocks) => _nodes[parent] = Get<RichNode>(parent) switch
    {
        RichStory story => story with { Blocks = blocks },
        RichListItem item => item with { Blocks = blocks },
        RichTableCell cell => cell with { Blocks = blocks },
        _ => throw new ArgumentException("This node is not a block container.", nameof(parent)),
    };
    private void EnsureParagraph(RichNodeId parent)
    {
        if (Blocks(Get<RichNode>(parent)).Count == 0) InsertParagraph(parent, 0);
    }
    private static RichNode Reparent(RichNode node, RichNodeId parent) => node with { Parent = parent };
    private void VerifyOpen() { if (!_open) throw new InvalidOperationException("A rich edit is valid only inside its transaction callback."); }
    internal (RichDocumentSnapshot Snapshot, RichAddressMap Map, long NextId) Finish()
    {
        VerifyOpen();
        var result = Candidate();
        if (SelectionAfter is { } selection) SelectionAfter = result.NormalizeSelection(selection);
        _open = false;
        return (result, new(_maps), _nextId);
    }
    internal void Close() => _open = false;
    private RichDocumentSnapshot Candidate(bool validate = true) => new(_body, _nodes.ToImmutable(), _styles, validate: validate,
        assets: _assets.ToImmutable(), bookmarks: _bookmarks.ToImmutable(), comments: _comments.ToImmutable(),
        defaultCharacter: _defaultCharacter, defaultParagraph: _defaultParagraph);
}
