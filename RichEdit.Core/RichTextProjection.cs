using System.Collections.Immutable;
using System.Text;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>The semantic origin of a separator in an explicitly requested text projection.</summary>
public enum RichSeparatorKind
{
    /// <summary>A boundary between blocks.</summary>
    Block,
    /// <summary>A boundary between table columns.</summary>
    Cell,
    /// <summary>A boundary between table rows.</summary>
    Row,
    /// <summary>A boundary between list items.</summary>
    ListItem,
}

/// <summary>A paragraph's range in a snapshot-bound plain-text projection.</summary>
public sealed record RichProjectedParagraph(RichNodeId Paragraph, TextSpan Range);

/// <summary>A structural separator; its text position is not an address for structural mutation.</summary>
public sealed record RichProjectedSeparator(int Position, RichSeparatorKind Kind, RichNodeId Owner);

/// <summary>An authored object represented by one placeholder in the explicit text projection.</summary>
public sealed record RichProjectedObject(int Position, RichNodeId Object);

/// <summary>An explicit plain-text projection with mappings tied to exactly one immutable rich snapshot.</summary>
public sealed class RichTextProjection
{
    private readonly ImmutableDictionary<RichNodeId, RichProjectedParagraph> _byParagraph;
    internal RichTextProjection(RichDocumentSnapshot snapshot, RichNodeId story)
    {
        Snapshot = snapshot;
        Story = story;
        var text = new StringBuilder();
        var paragraphs = ImmutableArray.CreateBuilder<RichProjectedParagraph>();
        var separators = ImmutableArray.CreateBuilder<RichProjectedSeparator>();
        var objects = ImmutableArray.CreateBuilder<RichProjectedObject>();
        EmitBlocks(snapshot.Get<RichStory>(story).Blocks, story);
        Text = TextSnapshot.FromText(text.ToString());
        Paragraphs = paragraphs.ToImmutable();
        Separators = separators.ToImmutable();
        Objects = objects.ToImmutable();
        _byParagraph = Paragraphs.ToImmutableDictionary(item => item.Paragraph);

        void Separator(char value, RichSeparatorKind kind, RichNodeId owner)
        {
            separators.Add(new(text.Length, kind, owner));
            text.Append(value);
        }
        void EmitBlocks(IEnumerable<RichNodeId> blocks, RichNodeId owner)
        {
            var first = true;
            foreach (var id in blocks)
            {
                if (!first) Separator('\n', RichSeparatorKind.Block, owner);
                first = false;
                Emit(snapshot.Get<RichBlock>(id));
            }
        }
        void Emit(RichBlock block)
        {
            switch (block)
            {
                case RichParagraph paragraph:
                    var start = text.Length;
                    foreach (var id in paragraph.Inlines)
                        switch (snapshot.Get<RichInline>(id))
                        {
                            case RichText run:
                                foreach (var chunk in run.Content.GetChunks(new(0, run.Content.Length))) text.Append(chunk.Span);
                                break;
                            case RichSoftBreak: text.Append('\u2028'); break;
                            case RichImage image:
                                objects.Add(new(text.Length, image.Id));
                                text.Append('\uFFFC');
                                break;
                        }
                    paragraphs.Add(new(paragraph.Id, new(start, text.Length - start)));
                    break;
                case RichList list:
                    for (var index = 0; index < list.Items.Count; index++)
                    {
                        if (index != 0) Separator('\n', RichSeparatorKind.ListItem, list.Id);
                        var item = snapshot.Get<RichListItem>(list.Items[index]);
                        EmitBlocks(item.Blocks, item.Id);
                    }
                    break;
                case RichTable table:
                    var grid = snapshot.GetTableGrid(table.Id);
                    for (var row = 0; row < grid.Rows; row++)
                    {
                        if (row != 0) Separator('\n', RichSeparatorKind.Row, table.Id);
                        for (var column = 0; column < grid.Columns; column++)
                        {
                            if (column != 0) Separator('\t', RichSeparatorKind.Cell, table.Id);
                            var cell = grid[row, column];
                            var extent = grid.GetExtent(cell);
                            if (extent.Row == row && extent.Column == column)
                                EmitBlocks(snapshot.Get<RichTableCell>(cell).Blocks, cell);
                        }
                    }
                    break;
                default: throw new ArgumentException("Unsupported block kind.");
            }
        }
    }

    /// <summary>Gets the immutable snapshot described by this mapping.</summary>
    public RichDocumentSnapshot Snapshot { get; }
    /// <summary>Gets the projected story identity.</summary>
    public RichNodeId Story { get; }
    /// <summary>Gets explicit plain text, including paragraph, cell, row, and soft-break separators.</summary>
    public TextSnapshot Text { get; }
    /// <summary>Gets paragraphs in projection order.</summary>
    public ImmutableArray<RichProjectedParagraph> Paragraphs { get; }
    /// <summary>Gets structural separator positions and kinds.</summary>
    public ImmutableArray<RichProjectedSeparator> Separators { get; }
    /// <summary>Gets authored object positions, distinguishing them from literal placeholder characters in text.</summary>
    public ImmutableArray<RichProjectedObject> Objects { get; }

    /// <summary>Maps a validated structural point into this snapshot's explicit text projection.</summary>
    public int GetOffset(RichTextPoint point)
    {
        Snapshot.ValidatePoint(point);
        if (point.Story != Story || !_byParagraph.TryGetValue(point.Paragraph, out var paragraph))
            throw new ArgumentException("The point does not belong to this projected story.", nameof(point));
        return checked(paragraph.Range.Start + point.Offset);
    }

    /// <summary>Maps a projection boundary to a paragraph point. Structural-only gaps return null.</summary>
    public RichTextPoint? GetPoint(int offset, CaretAffinity affinity = CaretAffinity.Downstream)
    {
        new TextSpan(offset, 0).Validate(Text.Length);
        if (!Enum.IsDefined(affinity)) throw new ArgumentOutOfRangeException(nameof(affinity));
        var low = 0;
        var high = Paragraphs.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Paragraphs[middle].Range.Start <= offset) low = middle + 1; else high = middle;
        }
        if (low == 0) return null;
        var index = low - 1;
        if (affinity == CaretAffinity.Upstream)
            while (index > 0 && Paragraphs[index - 1].Range.End == offset) index--;
        var paragraph = Paragraphs[index];
        return offset <= paragraph.Range.End ? new(Story, paragraph.Paragraph, offset - paragraph.Range.Start, affinity) : null;
    }
}
