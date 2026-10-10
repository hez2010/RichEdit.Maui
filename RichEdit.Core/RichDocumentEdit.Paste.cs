namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Replaces a typed selection with rich content, joining boundary paragraphs and retaining tables and lists as blocks.</summary>
    /// <remarks>Joining into an existing paragraph carries copied character inheritance in imported character styles. The destination paragraph keeps its paragraph properties unless it is empty.</remarks>
    public RichSelection ReplaceSelection(RichSelection selection, RichDocumentFragment fragment)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(fragment);
        if (selection is RichTextSelectionSet set)
            return ReplaceSelections(set, Enumerable.Repeat(fragment, set.Count).ToArray());
        var caret = ((RichTextSelection)ReplaceSelection(selection, "")).Active;
        var left = Get<RichParagraph>(caret.Paragraph);
        var parent = left.Parent!.Value;
        var rightId = SplitParagraph(caret);
        // Paste does not execute the Enter key's next-style policy.
        _nodes[rightId] = Get<RichParagraph>(rightId) with { Style = left.Style };
        var index = Blocks(Get<RichNode>(parent)).IndexOf(left.Id) + 1;
        var imported = InsertFragment(parent, index, fragment);
        var first = imported[0];
        var last = imported[^1];
        if (Get<RichNode>(first) is RichParagraph firstParagraph)
        {
            if (Length(Get<RichParagraph>(left.Id)) == 0)
                _nodes[left.Id] = Get<RichParagraph>(left.Id) with
                { Style = firstParagraph.Style, Format = firstParagraph.Format, Character = firstParagraph.Character };
            CarryCharacterContext(first, left.Id);
            JoinParagraphs(left.Id, first);
            if (first == last) last = left.Id;
        }
        else if (Length(Get<RichParagraph>(left.Id)) == 0) DeleteBlock(left.Id);

        if (Get<RichNode>(last) is RichParagraph lastParagraph)
        {
            var offset = Length(lastParagraph);
            CarryCharacterContext(rightId, last);
            JoinParagraphs(last, rightId);
            var point = new RichTextPoint(_body, last, offset);
            return new RichTextSelection(point, point);
        }
        if (Length(Get<RichParagraph>(rightId)) == 0)
        {
            DeleteBlock(rightId);
            return new RichBoundarySelection(_body, last, RichBlockEdge.After);
        }
        var afterBlock = new RichTextPoint(_body, rightId, 0);
        return new RichTextSelection(afterBlock, afterBlock);
    }

    /// <summary>Pastes one fragment at each normalized text selection, retaining selection identities and one transaction.</summary>
    public RichTextSelectionSet ReplaceSelections(RichTextSelectionSet selections, IReadOnlyList<RichDocumentFragment> fragments)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(fragments);
        var normalized = new RichTextSelectionSet(Candidate(validate: false), selections, selections.PrimaryIndex);
        if (normalized.Count != selections.Count || fragments.Count != normalized.Count)
            throw new ArgumentException("Supply one fragment per normalized selection.", nameof(fragments));
        foreach (var fragment in fragments) ArgumentNullException.ThrowIfNull(fragment);
        var result = new RichTextSelectionItem[normalized.Count];
        for (var index = normalized.Count - 1; index >= 0; index--)
        {
            var before = _maps.Count;
            var selected = ReplaceSelection(normalized[index].Selection, fragments[index]);
            // A multi-caret set consists of text points. Keep an insertion point
            // after a pasted terminal block within its existing text container.
            if (selected is RichBoundarySelection boundary)
            {
                var paragraph = InsertParagraph(boundary);
                selected = new RichTextSelection(new(_body, paragraph, 0), new(_body, paragraph, 0));
            }
            var map = new RichAddressMap(_maps.Skip(before));
            for (var mapped = index + 1; mapped < result.Length; mapped++)
            {
                var previous = result[mapped];
                result[mapped] = previous with { Selection = new(map.Map(previous.Selection.Anchor)!.Value, map.Map(previous.Selection.Active)!.Value) };
            }
            result[index] = normalized[index] with { Selection = (RichTextSelection)selected, PreferredVisualX = null };
        }
        return new(Candidate(validate: false), result, normalized.PrimaryIndex);
    }

    private void CarryCharacterContext(RichNodeId sourceId, RichNodeId destinationId)
    {
        var source = Get<RichParagraph>(sourceId);
        if (source.Inlines.Count == 0) return;
        var destination = Get<RichParagraph>(destinationId);
        var context = _styles.ResolveCharacter(source.Style, null, new(), _defaultCharacter, source.Character);
        if (context == _styles.ResolveCharacter(destination.Style, null, new(), _defaultCharacter, destination.Character)) return;
        var definitions = _styles.Definitions.ToBuilder();
        var next = 1;
        RichStyleId AllocateStyle()
        {
            while (definitions.ContainsKey(new(next)) && next <= short.MaxValue) next++;
            if (next > short.MaxValue) throw new InvalidOperationException("The fragment exceeds the RTF stylesheet capacity.");
            return new(next++);
        }
        var root = AllocateStyle();
        definitions.Add(root, new(root, "Pasted character context", RichStyleKind.Character)
        { Character = Rtf.RtfStyleState.Restore(context, 0, 0, 0) });
        var styles = new Dictionary<RichStyleId, RichStyleId>();
        RichStyleId Rebase(RichStyleId? style)
        {
            var chain = new Stack<RichStyleDefinition>();
            var based = root;
            while (style is { } nextStyle)
            {
                if (styles.TryGetValue(nextStyle, out var existing)) { based = existing; break; }
                var original = _styles.Definitions[nextStyle];
                chain.Push(original);
                style = original.BasedOn;
            }
            while (chain.TryPop(out var original))
            {
                var id = AllocateStyle();
                definitions.Add(id, original with { Id = id, BasedOn = based });
                styles.Add(original.Id, id);
                based = id;
            }
            return based;
        }
        foreach (var id in source.Inlines)
        {
            var inline = Get<RichInline>(id);
            _nodes[id] = inline with { Style = Rebase(inline.Style) };
        }
        _styles = new(definitions.Values);
    }
}
