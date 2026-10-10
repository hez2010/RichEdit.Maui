using TextEdit.Core;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Replaces a typed selection with semantic paragraphs and soft breaks, returning the resulting caret.</summary>
    /// <remarks>Text replacement across table cells or list items is rejected. Cell selections clear cell contents while retaining the grid.</remarks>
    public RichSelection ReplaceSelection(RichSelection selection, string text, RichCharacterStyle? typingFormat = null)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(text);
        if (typingFormat is not null) RichStyleSheet.Validate(typingFormat);
        var snapshot = Candidate(validate: false);
        selection = snapshot.NormalizeSelection(selection);
        RichTextPoint start;
        RichInline? seed = null;
        switch (selection)
        {
            case RichTextSelectionSet set:
                return ReplaceSelections(set, Enumerable.Repeat(text, set.Count).ToArray(), typingFormat);
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
        if (first.Parent != last.Parent) throw new ArgumentException("Use a structural selection when crossing table cells or list items.");
        var siblings = Blocks(Get<RichNode>(first.Parent!.Value));
        var firstIndex = siblings.IndexOf(first.Id);
        var lastIndex = siblings.IndexOf(last.Id);
        if (lastIndex <= firstIndex || siblings.Skip(firstIndex).Take(lastIndex - firstIndex + 1).Any(id => Get<RichNode>(id) is not RichParagraph))
            throw new ArgumentException("A text replacement cannot remove structural blocks.");
        ReplaceText(new(end.Story, end.Paragraph, 0), end.Offset, "");
        for (var index = lastIndex - 1; index > firstIndex; index--) DeleteBlock(siblings[index]);
        ReplaceText(start, Length(Get<RichParagraph>(start.Paragraph)) - start.Offset, "");
        JoinParagraphs(start.Paragraph, end.Paragraph);
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
