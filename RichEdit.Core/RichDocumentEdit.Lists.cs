using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Splits a list item at a paragraph point. An empty single-paragraph item leaves its list.</summary>
    public RichTextPoint SplitListItem(RichTextPoint point)
    {
        VerifyOpen();
        ValidatePoint(point);
        var paragraph = Get<RichParagraph>(point.Paragraph);
        var item = Get<RichListItem>(paragraph.Parent!.Value);
        var list = Get<RichList>(item.Parent!.Value);
        if (item.Blocks.Count == 1 && Length(paragraph) == 0)
        {
            UnwrapListItem(item.Id);
            return point;
        }
        var second = SplitParagraph(point);
        item = Get<RichListItem>(item.Id);
        var index = item.Blocks.IndexOf(second);
        var nextItem = Allocate();
        var blocks = item.Blocks.GetRange(index, item.Blocks.Count - index);
        foreach (var block in blocks) _nodes[block] = Reparent(Get<RichBlock>(block), nextItem);
        _nodes[item.Id] = item with { Blocks = item.Blocks.RemoveRange(index, blocks.Count) };
        _nodes.Add(nextItem, new RichListItem(nextItem, list.Id, blocks));
        _nodes[list.Id] = list with { Items = list.Items.Insert(list.Items.IndexOf(item.Id) + 1, nextItem) };
        return point with { Paragraph = second, Offset = 0 };
    }

    /// <summary>Removes one item's list membership, retaining its blocks and the surrounding lists' displayed counters.</summary>
    public void UnwrapListItem(RichNodeId itemId)
    {
        VerifyOpen();
        var item = Get<RichListItem>(itemId);
        var list = Get<RichList>(item.Parent!.Value);
        var parent = list.Parent!.Value;
        var siblings = Blocks(Get<RichNode>(parent));
        var itemIndex = list.Items.IndexOf(itemId);
        var replacement = ImmutableList.CreateBuilder<RichNodeId>();
        if (itemIndex > 0)
        {
            _nodes[list.Id] = list with { Items = list.Items.GetRange(0, itemIndex) };
            replacement.Add(list.Id);
        }
        foreach (var block in item.Blocks)
        {
            _nodes[block] = Reparent(Get<RichBlock>(block), parent);
            replacement.Add(block);
        }
        if (itemIndex + 1 < list.Items.Count)
        {
            var suffixId = itemIndex == 0 ? list.Id : Allocate();
            var suffix = list.Items.GetRange(itemIndex + 1, list.Items.Count - itemIndex - 1);
            foreach (var id in suffix) _nodes[id] = Get<RichListItem>(id) with { Parent = suffixId };
            _nodes[suffixId] = list with { Id = suffixId, Items = suffix, StartNumber = checked(list.StartNumber + itemIndex + 1) };
            replacement.Add(suffixId);
        }
        else if (itemIndex == 0) _nodes.Remove(list.Id);
        _nodes.Remove(itemId);
        SetBlocks(parent, siblings.Remove(list.Id).InsertRange(siblings.IndexOf(list.Id), replacement));
    }

    /// <summary>Joins a list item's leading paragraph with the previous item, or unwraps the first item.</summary>
    /// <remarks>Following nested blocks move with their item. Adjacent paragraphs join without losing their text-address maps.</remarks>
    public RichTextPoint JoinListItem(RichTextPoint point)
    {
        VerifyOpen();
        ValidatePoint(point);
        var paragraph = Get<RichParagraph>(point.Paragraph);
        var item = Get<RichListItem>(paragraph.Parent!.Value);
        if (point.Offset != 0 || item.Blocks[0] != paragraph.Id) throw new ArgumentException("Join from the start of the item's leading paragraph.", nameof(point));
        var list = Get<RichList>(item.Parent!.Value);
        var index = list.Items.IndexOf(item.Id);
        if (index == 0) { UnwrapListItem(item.Id); return point; }
        var previous = Get<RichListItem>(list.Items[index - 1]);
        var last = Get<RichBlock>(previous.Blocks[^1]);
        foreach (var block in item.Blocks) _nodes[block] = Reparent(Get<RichBlock>(block), previous.Id);
        _nodes[previous.Id] = previous with { Blocks = previous.Blocks.AddRange(item.Blocks) };
        _nodes[list.Id] = list with { Items = list.Items.RemoveAt(index) };
        _nodes.Remove(item.Id);
        if (last is not RichParagraph preceding) return point;
        var result = point with { Paragraph = preceding.Id, Offset = Length(preceding) };
        JoinParagraphs(preceding.Id, paragraph.Id);
        return result;
    }

    /// <summary>Wraps contiguous blocks in a semantic list, retaining their identities and rich text addresses.</summary>
    /// <remarks>Each item starts with a paragraph. A nonparagraph block receives an empty marker paragraph.</remarks>
    public RichNodeId WrapInList(RichNodeId parent, int index, int count, bool numbered = false, int startNumber = 1,
        RichListNumberingStyle numberStyle = RichListNumberingStyle.Decimal)
    {
        VerifyOpen();
        var blocks = Blocks(Get<RichNode>(parent));
        ValidateRemoval(index, count, blocks.Count);
        if (startNumber < 0 || (long)startNumber + count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startNumber));
        ValidateNumberStyle(numbered, numberStyle);
        var list = Allocate();
        var items = ImmutableList.CreateBuilder<RichNodeId>();
        for (var offset = 0; offset < count; offset++)
        {
            var item = Allocate();
            var block = blocks[index + offset];
            var children = ImmutableList<RichNodeId>.Empty;
            if (Get<RichBlock>(block) is not RichParagraph)
            {
                var paragraph = Allocate();
                _nodes.Add(paragraph, new RichParagraph(paragraph, item, []));
                children = children.Add(paragraph);
            }
            children = children.Add(block);
            _nodes[block] = Reparent(Get<RichBlock>(block), item);
            _nodes.Add(item, new RichListItem(item, list, children));
            items.Add(item);
        }
        _nodes.Add(list, new RichList(list, parent, items.ToImmutable()) { Numbered = numbered, StartNumber = startNumber, NumberStyle = numberStyle });
        SetBlocks(parent, blocks.RemoveRange(index, count).Insert(index, list));
        return list;
    }

    /// <summary>Unwraps a list into its parent container, retaining item blocks and their paragraph addresses.</summary>
    public void UnwrapList(RichNodeId listId)
    {
        VerifyOpen();
        var list = Get<RichList>(listId);
        var parent = list.Parent!.Value;
        var blocks = Blocks(Get<RichNode>(parent));
        var index = blocks.IndexOf(listId);
        var unwrapped = ImmutableList.CreateBuilder<RichNodeId>();
        foreach (var itemId in list.Items)
        {
            var item = Get<RichListItem>(itemId);
            foreach (var block in item.Blocks)
            {
                _nodes[block] = Reparent(Get<RichBlock>(block), parent);
                unwrapped.Add(block);
            }
            _nodes.Remove(itemId);
        }
        _nodes.Remove(listId);
        SetBlocks(parent, blocks.RemoveAt(index).InsertRange(index, unwrapped));
    }

    /// <summary>Changes a list's marker kind or starting counter without rewriting its item content.</summary>
    public void SetListNumbering(RichNodeId listId, bool numbered, int startNumber = 1,
        RichListNumberingStyle numberStyle = RichListNumberingStyle.Decimal)
    {
        VerifyOpen();
        var list = Get<RichList>(listId);
        if (startNumber < 0 || (long)startNumber + list.Items.Count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startNumber));
        ValidateNumberStyle(numbered, numberStyle);
        _nodes[listId] = list with { Numbered = numbered, StartNumber = startNumber, NumberStyle = numberStyle };
    }
    private static void ValidateNumberStyle(bool numbered, RichListNumberingStyle style)
    {
        if (!Enum.IsDefined(style) || !numbered && style != RichListNumberingStyle.Decimal)
            throw new ArgumentException("Number styles require a numbered list and a supported numbering format.", nameof(style));
    }
}
