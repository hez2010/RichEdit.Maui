using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Wraps contiguous blocks in a semantic list, retaining their identities and rich text addresses.</summary>
    /// <remarks>Each item starts with a paragraph. A nonparagraph block receives an empty marker paragraph.</remarks>
    public RichNodeId WrapInList(RichNodeId parent, int index, int count, bool numbered = false, int startNumber = 1)
    {
        VerifyOpen();
        var blocks = Blocks(Get<RichNode>(parent));
        ValidateRemoval(index, count, blocks.Count);
        if (startNumber < 0 || (long)startNumber + count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startNumber));
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
        _nodes.Add(list, new RichList(list, parent, items.ToImmutable()) { Numbered = numbered, StartNumber = startNumber });
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
    public void SetListNumbering(RichNodeId listId, bool numbered, int startNumber = 1)
    {
        VerifyOpen();
        var list = Get<RichList>(listId);
        if (startNumber < 0 || (long)startNumber + list.Items.Count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startNumber));
        _nodes[listId] = list with { Numbered = numbered, StartNumber = startNumber };
    }
}
