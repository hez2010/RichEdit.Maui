using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>A self-contained immutable rich slice. Its interchange representation is ordinary RTF.</summary>
public sealed class RichDocumentFragment
{
    /// <summary>Adopts immutable content as a fragment; no history or view state is captured.</summary>
    public RichDocumentFragment(RichDocumentSnapshot content) => Content = content ?? throw new ArgumentNullException(nameof(content));
    /// <summary>Gets the fragment's immutable structured content.</summary>
    public RichDocumentSnapshot Content { get; }
    /// <summary>Creates semantic paragraphs and soft breaks from plain clipboard text.</summary>
    public static RichDocumentFragment FromPlainText(string text) => new(RichDocumentSnapshot.FromPlainText(text));

    /// <summary>Copies text, an object, or an expanded table-cell rectangle with its styles, referenced images, and fully contained annotations.</summary>
    public static RichDocumentFragment FromSelection(RichDocumentSnapshot snapshot, RichSelection selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        selection = snapshot.NormalizeSelection(selection);
        var nodes = ImmutableDictionary.CreateBuilder<RichNodeId, RichNode>();
        var blocks = ImmutableList.CreateBuilder<RichNodeId>();
        var intervals = new Dictionary<RichNodeId, TextSpan>();
        var body = snapshot.Body;
        switch (selection)
        {
            case RichBlockSelection selectedBlocks:
                foreach (var block in snapshot.GetSelectedBlocks(selectedBlocks))
                {
                    Copy(block);
                    nodes[block] = nodes[block] with { Parent = body };
                    blocks.Add(block);
                }
                break;
            case RichTextSelection text:
                var reverse = snapshot.ComparePoints(text.Anchor, text.Active) > 0;
                var start = reverse ? text.Active : text.Anchor;
                var end = reverse ? text.Anchor : text.Active;
                var first = snapshot.Get<RichParagraph>(start.Paragraph);
                var last = snapshot.Get<RichParagraph>(end.Paragraph);
                if (first.Parent != last.Parent) throw new ArgumentException("Use a table-cell selection to copy across cells or a structural block boundary.", nameof(selection));
                var siblings = RichDocumentSnapshot.Children(snapshot.Get<RichNode>(first.Parent!.Value)).ToArray();
                var firstIndex = Array.IndexOf(siblings, first.Id);
                var lastIndex = Array.IndexOf(siblings, last.Id);
                for (var index = firstIndex; index <= lastIndex; index++)
                {
                    if (snapshot.Get<RichNode>(siblings[index]) is not RichParagraph paragraph)
                    {
                        Copy(siblings[index]);
                        nodes[siblings[index]] = nodes[siblings[index]] with { Parent = body };
                        blocks.Add(siblings[index]);
                        continue;
                    }
                    var from = index == firstIndex ? start.Offset : 0;
                    var to = index == lastIndex ? end.Offset : snapshot.GetParagraphLength(paragraph.Id);
                    SliceParagraph(paragraph, from, to);
                    blocks.Add(paragraph.Id);
                }
                break;
            case RichObjectSelection selected:
                var inline = snapshot.Get<RichInline>(selected.Object);
                var parent = snapshot.Get<RichParagraph>(inline.Parent!.Value);
                var offset = parent.Inlines.TakeWhile(id => id != inline.Id).Sum(id => RichDocumentSnapshot.InlineLength(snapshot.Get<RichInline>(id)));
                SliceParagraph(parent, offset, offset + 1);
                blocks.Add(parent.Id);
                break;
            case RichCellSelection cells:
                var table = snapshot.Get<RichTable>(cells.Table);
                var rows = ImmutableList.CreateBuilder<RichNodeId>();
                for (var rowIndex = cells.Row; rowIndex < cells.Row + cells.RowCount; rowIndex++)
                {
                    var row = snapshot.Get<RichTableRow>(table.Rows[rowIndex]);
                    var rowCells = ImmutableList.CreateBuilder<RichNodeId>();
                    foreach (var id in row.Cells)
                    {
                        var cell = snapshot.Get<RichTableCell>(id);
                        if (cell.Column < cells.Column || cell.Column >= cells.Column + cells.ColumnCount) continue;
                        foreach (var block in cell.Blocks) Copy(block);
                        nodes.Add(id, cell with { Column = cell.Column - cells.Column });
                        rowCells.Add(id);
                    }
                    nodes.Add(row.Id, row with { Cells = rowCells.ToImmutable() }); rows.Add(row.Id);
                }
                nodes.Add(table.Id, table with { Parent = body, Rows = rows.ToImmutable(), Columns = table.Columns.Slice(cells.Column, cells.ColumnCount) });
                blocks.Add(table.Id);
                break;
            case RichBoundarySelection: return FromPlainText("");
            default: throw new ArgumentException("Copy multiple text selections as separate fragments before combining their RTF payload.", nameof(selection));
        }
        nodes.Add(body, new RichStory(body, blocks.ToImmutable()));
        var resources = nodes.Values.OfType<RichImage>().Select(image => image.Asset).Distinct().ToImmutableDictionary(id => id, id => snapshot.Assets[id]);
        var bookmarks = snapshot.Bookmarks.Values.Select(value => (Value: value, Range: Map(value.Range))).Where(item => item.Range is not null)
            .ToImmutableDictionary(item => item.Value.Id, item => item.Value with { Range = item.Range });
        var comments = snapshot.Comments.Values.Select(value => (Value: value, Range: Map(value.Range))).Where(item => item.Range is not null)
            .ToImmutableDictionary(item => item.Value.Id, item => item.Value with { Range = item.Range });
        var usedStyles = new HashSet<RichStyleId>();
        var pendingStyles = new Stack<RichStyleId>(nodes.Values.Select(node => node switch
        { RichParagraph paragraph => paragraph.Style, RichInline inline => inline.Style, _ => null }).OfType<RichStyleId>());
        while (pendingStyles.TryPop(out var styleId))
        {
            if (!usedStyles.Add(styleId)) continue;
            var style = snapshot.Styles.Definitions[styleId];
            if (style.BasedOn is { } based) pendingStyles.Push(based);
            if (style.NextStyle is { } next) pendingStyles.Push(next);
        }
        var styles = new RichStyleSheet(usedStyles.Select(id => snapshot.Styles.Definitions[id]));
        return new(new(body, nodes.ToImmutable(), styles, assets: resources, bookmarks: bookmarks, comments: comments,
            defaultCharacter: snapshot.DefaultCharacter, defaultParagraph: snapshot.DefaultParagraph));

        void Copy(RichNodeId id)
        {
            var node = snapshot.Get<RichNode>(id);
            nodes.Add(id, node);
            if (node is RichParagraph p) intervals.Add(id, new(0, snapshot.GetParagraphLength(id)));
            foreach (var child in RichDocumentSnapshot.Children(node)) Copy(child);
        }
        void SliceParagraph(RichParagraph paragraph, int from, int to)
        {
            var inlines = ImmutableList.CreateBuilder<RichNodeId>();
            var position = 0;
            foreach (var id in paragraph.Inlines)
            {
                var inline = snapshot.Get<RichInline>(id);
                var end = position + RichDocumentSnapshot.InlineLength(inline);
                var first = Math.Max(from, position); var last = Math.Min(to, end);
                if (first < last)
                {
                    nodes.Add(id, inline is RichText run ? run with { Content = run.Content.Slice(new(first - position, last - first)) } : inline);
                    inlines.Add(id);
                }
                position = end;
            }
            nodes.Add(paragraph.Id, paragraph with { Parent = body, Inlines = inlines.ToImmutable() });
            intervals.Add(paragraph.Id, new(from, to - from));
        }
        RichTextSelection? Map(RichTextSelection? range)
        {
            if (range is null || !intervals.TryGetValue(range.Anchor.Paragraph, out var anchor) || !intervals.TryGetValue(range.Active.Paragraph, out var active) ||
                range.Anchor.Offset < anchor.Start || range.Anchor.Offset > anchor.End || range.Active.Offset < active.Start || range.Active.Offset > active.End) return null;
            return new(range.Anchor with { Offset = range.Anchor.Offset - anchor.Start }, range.Active with { Offset = range.Active.Offset - active.Start });
        }
    }
}
