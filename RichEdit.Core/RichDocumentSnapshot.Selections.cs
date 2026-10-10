using System.Collections.Immutable;

namespace RichEdit.Core;

/// <summary>A paragraph-local part of a typed selection, including whether its paragraph end is selected.</summary>
public readonly record struct RichSelectedText(RichTextPoint Start, int Length, bool IncludesParagraphEnd);

public sealed partial class RichDocumentSnapshot
{
    /// <summary>Enumerates selected paragraph-local ranges without flattening tables, lists, or object addresses.</summary>
    /// <remarks>Cell rectangles include only their cells, including nested content. A boundary has no text ranges.</remarks>
    public ImmutableArray<RichSelectedText> GetSelectedText(RichSelection selection)
    {
        selection = NormalizeSelection(selection);
        var result = ImmutableArray.CreateBuilder<RichSelectedText>();
        Add(selection);
        return result.ToImmutable();

        void Add(RichSelection value)
        {
            switch (value)
            {
                case RichTextSelectionSet set:
                    foreach (var item in set) Add(item.Selection);
                    break;
                case RichTextSelection text:
                    var reverse = ComparePoints(text.Anchor, text.Active) > 0;
                    var start = reverse ? text.Active : text.Anchor;
                    var end = reverse ? text.Anchor : text.Active;
                    if (start.Paragraph == end.Paragraph)
                    {
                        result.Add(new(start, end.Offset - start.Offset, false));
                        break;
                    }
                    var active = false;
                    foreach (var paragraph in EnumerateParagraphs(start.Story))
                    {
                        if (paragraph == start.Paragraph) active = true;
                        if (!active) continue;
                        var offset = paragraph == start.Paragraph ? start.Offset : 0;
                        if (paragraph == end.Paragraph)
                        {
                            if (end.Offset > 0) result.Add(new(new(start.Story, paragraph, 0), end.Offset, false));
                            break;
                        }
                        result.Add(new(new(start.Story, paragraph, offset), GetParagraphLength(paragraph) - offset, true));
                    }
                    break;
                case RichObjectSelection selected:
                    var node = Get<RichInline>(selected.Object);
                    var parent = Get<RichParagraph>(node.Parent!.Value);
                    var position = parent.Inlines.TakeWhile(id => id != node.Id).Sum(id => InlineLength(Get<RichInline>(id)));
                    result.Add(new(new(Body, parent.Id, position), 1, false));
                    break;
                case RichCellSelection cells:
                    var grid = GetTableGrid(cells.Table);
                    var visited = new HashSet<RichNodeId>();
                    for (var row = cells.Row; row < cells.Row + cells.RowCount; row++)
                        for (var column = cells.Column; column < cells.Column + cells.ColumnCount; column++)
                            if (visited.Add(grid[row, column]))
                                foreach (var paragraph in EnumerateParagraphs(grid[row, column]))
                                    result.Add(new(new(Body, paragraph, 0), GetParagraphLength(paragraph), true));
                    break;
            }
        }
    }

    internal IEnumerable<RichNodeId> EnumerateParagraphs(RichNodeId root)
    {
        var node = Get<RichNode>(root);
        if (node is RichParagraph) { yield return root; yield break; }
        IEnumerable<RichNodeId> children = node switch
        {
            RichStory story => story.Blocks, RichList list => list.Items, RichListItem item => item.Blocks,
            RichTable table => table.Rows, RichTableRow row => row.Cells, RichTableCell cell => cell.Blocks,
            _ => [],
        };
        foreach (var child in children)
            foreach (var paragraph in EnumerateParagraphs(child)) yield return paragraph;
    }

    /// <summary>Returns resolved character values for selected runs and empty paragraphs, suitable for mixed-format queries.</summary>
    public ImmutableArray<ResolvedCharacterStyle> GetSelectedCharacterFormats(RichSelection selection)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedCharacterStyle>();
        foreach (var range in GetSelectedText(selection))
        {
            var paragraph = Get<RichParagraph>(range.Start.Paragraph);
            var offset = 0;
            var found = false;
            foreach (var id in paragraph.Inlines)
            {
                var inline = Get<RichInline>(id);
                var end = offset + InlineLength(inline);
                if (range.Length == 0 ? end >= range.Start.Offset : end > range.Start.Offset && offset < range.Start.Offset + range.Length)
                {
                    result.Add(ResolveCharacter(id));
                    found = true;
                    if (range.Length == 0) break;
                }
                offset = end;
                if (offset >= range.Start.Offset + range.Length) break;
            }
            if (!found || range.IncludesParagraphEnd)
                result.Add(Styles.ResolveCharacter(paragraph.Style, null, new(), DefaultCharacter, paragraph.Character));
        }
        if (result.Count == 0) result.Add(DefaultCharacter.Apply(new()));
        return result.ToImmutable();
    }
}
