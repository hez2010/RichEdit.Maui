using TextEdit.Core;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Replaces a typed selection with semantic paragraphs and soft breaks, returning the resulting caret.</summary>
    /// <remarks>Text replacement joins paragraphs and list items within one text container and removes fully enclosed blocks. Use cell selections across table cells.</remarks>
    public RichSelection ReplaceSelection(RichSelection selection, string text, RichCharacterStyle? typingFormat = null)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(text);
        if (typingFormat is not null) RichStyleSheet.Validate(typingFormat);
        var snapshot = Candidate(validate: false);
        selection = snapshot.NormalizeSelection(selection);
        if (selection is RichTextSelection complete && WholeBlocks(snapshot, complete) is { } blocks)
            selection = blocks;
        RichTextPoint start;
        RichInline? seed = null;
        switch (selection)
        {
            case RichTextSelectionSet set:
                return ReplaceSelections(set, Enumerable.Repeat(text, set.Count).ToArray(), typingFormat);
            case RichBlockSelection blockSelection:
                var selectedBlocks = snapshot.GetSelectedBlocks(blockSelection);
                var firstParagraph = snapshot.Get<RichParagraph>(snapshot.EnumerateParagraphs(selectedBlocks[0]).First());
                seed = firstParagraph.Inlines.Count == 0 ? null : snapshot.Get<RichInline>(firstParagraph.Inlines[0]);
                var inserted = InsertParagraph(blockSelection.Parent, Blocks(Get<RichNode>(blockSelection.Parent)).IndexOf(selectedBlocks[0]));
                _nodes[inserted] = Get<RichParagraph>(inserted) with
                { Style = firstParagraph.Style, Format = firstParagraph.Format, Character = firstParagraph.Character };
                foreach (var block in selectedBlocks) DeleteBlock(block);
                start = new(_body, inserted, 0);
                break;
            case RichBoundarySelection boundary:
                start = new(boundary.Story, InsertParagraph(boundary), 0);
                break;
            case RichCellSelection cells:
                ClearCells(cells);
                var cell = Get<RichTableCell>(Candidate(validate: false).GetTableGrid(cells.Table)[cells.Row, cells.Column]);
                start = new(_body, cell.Blocks[0], 0);
                break;
            case RichObjectSelection selected:
                var inline = Get<RichInline>(selected.Object);
                seed = inline;
                var paragraph = Get<RichParagraph>(inline.Parent!.Value);
                var offset = paragraph.Inlines.TakeWhile(id => id != inline.Id).Sum(id => RichDocumentSnapshot.InlineLength(Get<RichInline>(id)));
                start = new(_body, paragraph.Id, offset);
                ReplaceText(start, 1, "");
                break;
            case RichTextSelection range:
                if (range.Anchor == range.Active && text is "\n" or "\r\n" or "\r" &&
                    snapshot.Get<RichParagraph>(range.Active.Paragraph).Parent is { } listParent && snapshot.Nodes[listParent] is RichListItem)
                {
                    var next = SplitListItem(range.Active);
                    return new RichTextSelection(next, next);
                }
                var reverse = snapshot.ComparePoints(range.Anchor, range.Active) > 0;
                start = reverse ? range.Active : range.Anchor;
                var end = reverse ? range.Anchor : range.Active;
                seed = InlineAt(Get<RichParagraph>(start.Paragraph), start.Offset, following: start != end);
                if (start.Paragraph == end.Paragraph && !text.AsSpan().ContainsAny('\r', '\n', '\u2028'))
                {
                    ReplaceText(start, end.Offset - start.Offset, text);
                    if (typingFormat is not null && typingFormat != new RichCharacterStyle() && text.Length > 0)
                        ChangeInlines(start, text.Length, inline => inline with { Format = inline.Format.Overlay(typingFormat) });
                    var resulting = start with { Offset = start.Offset + text.Length };
                    return new RichTextSelection(resulting, resulting);
                }
                RemoveTextSelection(start, end);
                break;
            default: throw new ArgumentException("Unsupported selection kind.", nameof(selection));
        }
        var caret = InsertPlainText(start, text, typingFormat, seed);
        return new RichTextSelection(caret, caret);
    }

    private static RichBlockSelection? WholeBlocks(RichDocumentSnapshot snapshot, RichTextSelection selection)
    {
        var reversed = snapshot.ComparePoints(selection.Anchor, selection.Active) > 0;
        var start = reversed ? selection.Active : selection.Anchor;
        var end = reversed ? selection.Anchor : selection.Active;
        if (start.Offset != 0 || end.Offset != snapshot.GetParagraphLength(end.Paragraph) ||
            snapshot.Get<RichParagraph>(start.Paragraph).Parent == snapshot.Get<RichParagraph>(end.Paragraph).Parent) return null;
        var endAncestors = new Dictionary<RichNodeId, RichBlock>();
        for (var node = snapshot.Get<RichNode>(end.Paragraph); node.Parent is { } parent; node = snapshot.Get<RichNode>(parent))
        {
            if (node is RichTableCell) break;
            if (node is RichBlock block && snapshot.Get<RichNode>(parent) is RichStory or RichTableCell or RichListItem)
                endAncestors[parent] = block;
        }
        for (var node = snapshot.Get<RichNode>(start.Paragraph); node.Parent is { } parent; node = snapshot.Get<RichNode>(parent))
        {
            if (node is RichTableCell) break;
            if (node is not RichBlock first || !endAncestors.TryGetValue(parent, out var last)) continue;
            // Entire lists and intervening blocks can be removed together while
            // partial items continue through the text-joining path below.
            return snapshot.EnumerateParagraphs(first.Id).First() == start.Paragraph &&
                snapshot.EnumerateParagraphs(last.Id).Last() == end.Paragraph
                ? new(parent, first.Id, last.Id) : null;
        }
        return null;
    }

    /// <summary>Plans one replacement per normalized selection against the same candidate and retains stable selection identities.</summary>
    public RichTextSelectionSet ReplaceSelections(RichTextSelectionSet selections, IReadOnlyList<string> text, RichCharacterStyle? typingFormat = null)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(text);
        var normalized = new RichTextSelectionSet(Candidate(validate: false), selections, selections.PrimaryIndex);
        if (normalized.Count != selections.Count || text.Count != normalized.Count) throw new ArgumentException("Supply one replacement per normalized selection.", nameof(text));
        foreach (var value in text) ArgumentNullException.ThrowIfNull(value);
        var result = new RichTextSelectionItem[normalized.Count];
        for (var index = normalized.Count - 1; index >= 0; index--)
        {
            var before = _maps.Count;
            var caret = (RichTextSelection)ReplaceSelection(normalized[index].Selection, text[index], typingFormat);
            var map = new RichAddressMap(_maps.Skip(before));
            for (var mapped = index + 1; mapped < result.Length; mapped++)
            {
                var previous = result[mapped];
                result[mapped] = previous with { Selection = new(map.Map(previous.Selection.Anchor)!.Value, map.Map(previous.Selection.Active)!.Value) };
            }
            result[index] = normalized[index] with { Selection = caret, PreferredVisualX = null };
        }
        return new(Candidate(validate: false), result, normalized.PrimaryIndex);
    }

    private void RemoveTextSelection(RichTextPoint start, RichTextPoint end)
    {
        if (start.Paragraph == end.Paragraph) { ReplaceText(start, end.Offset - start.Offset, ""); return; }
        var first = Get<RichParagraph>(start.Paragraph);
        var last = Get<RichParagraph>(end.Paragraph);
        if (first.Parent != last.Parent && RemoveAcrossListContainers(start, end, first, last)) return;
        if (first.Parent != last.Parent && Get<RichNode>(first.Parent!.Value) is RichListItem firstItem &&
            Get<RichNode>(last.Parent!.Value) is RichListItem lastItem && firstItem.Parent == lastItem.Parent)
        {
            var list = Get<RichList>(firstItem.Parent!.Value);
            var firstItemIndex = list.Items.IndexOf(firstItem.Id);
            var lastItemIndex = list.Items.IndexOf(lastItem.Id);
            for (var index = firstItemIndex; index < lastItemIndex; index++)
            {
                var next = Get<RichListItem>(Get<RichList>(list.Id).Items[firstItemIndex + 1]);
                var before = _maps.Count;
                JoinListItem(new(start.Story, next.Blocks[0], 0));
                end = new RichAddressMap(_maps.Skip(before)).Map(end)!.Value;
            }
            RemoveTextSelection(start, end);
            return;
        }
        if (first.Parent != last.Parent) throw new ArgumentException("Use a structural selection when crossing table cells or list items.");
        var siblings = Blocks(Get<RichNode>(first.Parent!.Value));
        var firstIndex = siblings.IndexOf(first.Id);
        var lastIndex = siblings.IndexOf(last.Id);
        if (lastIndex <= firstIndex) throw new ArgumentException("Text endpoints must follow document order.");
        ReplaceText(new(end.Story, end.Paragraph, 0), end.Offset, "");
        for (var index = lastIndex - 1; index > firstIndex; index--) DeleteBlock(siblings[index]);
        ReplaceText(start, Length(Get<RichParagraph>(start.Paragraph)) - start.Offset, "");
        JoinParagraphs(start.Paragraph, end.Paragraph);
    }

    private bool RemoveAcrossListContainers(RichTextPoint start, RichTextPoint end, RichParagraph first, RichParagraph last)
    {
        var firstItem = Get<RichNode>(first.Parent!.Value) as RichListItem;
        var lastItem = Get<RichNode>(last.Parent!.Value) as RichListItem;
        var firstList = firstItem is null ? null : Get<RichList>(firstItem.Parent!.Value);
        var lastList = lastItem is null ? null : Get<RichList>(lastItem.Parent!.Value);
        if (firstList is null && lastList is null || firstList?.Id == lastList?.Id) return false;
        var parent = firstList?.Parent ?? first.Parent;
        if (parent != (lastList?.Parent ?? last.Parent)) return false;
        var siblings = Blocks(Get<RichNode>(parent!.Value));
        var from = siblings.IndexOf(firstList?.Id ?? first.Id);
        var to = siblings.IndexOf(lastList?.Id ?? last.Id);
        if (to <= from) throw new ArgumentException("Text endpoints must follow document order.");

        ReplaceText(start, Length(first) - start.Offset, "");
        ReplaceText(new(end.Story, end.Paragraph, 0), end.Offset, "");
        if (firstItem is not null)
        {
            var keep = firstItem.Blocks.IndexOf(first.Id) + 1;
            foreach (var block in firstItem.Blocks.Skip(keep)) RemoveSubtree(block);
            _nodes[firstItem.Id] = firstItem with { Blocks = firstItem.Blocks.GetRange(0, keep) };
            var keepItems = firstList!.Items.IndexOf(firstItem.Id) + 1;
            foreach (var item in firstList.Items.Skip(keepItems)) RemoveSubtree(item);
            _nodes[firstList.Id] = firstList with { Items = firstList.Items.GetRange(0, keepItems) };
        }

        var suffix = lastItem is null ? new[] { last.Id } : lastItem.Blocks.Skip(lastItem.Blocks.IndexOf(last.Id)).ToArray();
        if (lastItem is not null)
        {
            foreach (var block in lastItem.Blocks.Take(lastItem.Blocks.IndexOf(last.Id))) RemoveSubtree(block);
            _nodes[lastItem.Id] = lastItem with { Blocks = lastItem.Blocks.RemoveRange(0, lastItem.Blocks.IndexOf(last.Id)) };
        }
        // Keep the ending item's unselected blocks and inline formatting with
        // the surviving paragraph. Other trailing list items retain their list.
        var destination = first.Parent!.Value;
        var index = Blocks(Get<RichNode>(destination)).IndexOf(first.Id) + 1;
        foreach (var block in suffix) MoveBlock(block, destination, index++);
        JoinParagraphs(first.Id, last.Id);
        if (lastItem is not null)
        {
            var removedItems = lastList!.Items.IndexOf(lastItem.Id) + 1;
            foreach (var item in lastList.Items.Take(removedItems)) RemoveSubtree(item);
            _nodes[lastList.Id] = lastList with { Items = lastList.Items.RemoveRange(0, removedItems) };
            if (removedItems == lastList.Items.Count) DeleteBlock(lastList.Id);
            else _nodes[lastList.Id] = Get<RichList>(lastList.Id) with { StartNumber = checked(lastList.StartNumber + removedItems) };
        }
        foreach (var block in siblings.Skip(from + 1).Take(to - from - 1)) DeleteBlock(block);
        return true;
    }

    private RichTextPoint InsertPlainText(RichTextPoint point, string text, RichCharacterStyle? typingFormat, RichInline? seed)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var start = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index != text.Length && text[index] is not ('\n' or '\u2028')) continue;
            var value = text[start..index];
            ReplaceText(point, 0, value);
            if (value.Length > 0 && (typingFormat is not null || seed is not null)) ChangeInlines(point, value.Length, ApplyTyping);
            point = point with { Offset = checked(point.Offset + value.Length) };
            if (index == text.Length) break;
            if (text[index] == '\n') point = new(point.Story, SplitParagraph(point), 0);
            else
            {
                InsertSoftBreak(point);
                if (typingFormat is not null || seed is not null) ChangeInlines(point, 1, ApplyTyping);
                point = point with { Offset = point.Offset + 1 };
            }
            start = index + 1;
        }
        return point;
        RichInline ApplyTyping(RichInline inline) => inline with
        {
            Style = seed is null ? inline.Style : seed.Style,
            Link = seed is null ? inline.Link : seed.Link,
            Format = (seed?.Format ?? inline.Format).Overlay(typingFormat ?? new()),
        };
    }
}
