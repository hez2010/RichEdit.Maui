using RichEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private double StructuredWidgetWidth(RichTextAdornment item, double viewportWidth)
    {
        if (_structuredNativeMap is not { } map || item.Position > map.TextProjection.Text.Length ||
            map.TextProjection.GetPoint(item.Position) is not { } point) return viewportWidth;
        var snapshot = map.TextProjection.Snapshot;
        var paragraph = snapshot.ResolveParagraph(point.Paragraph);
#if WINDOWS
        const double unitsPerPoint = 96d / 72;
#else
        const double unitsPerPoint = 1;
#endif
        var width = viewportWidth;
        // Scoped Android controls already have the native cell's content width.
        // Whole-story RTF surfaces use the authored fixed column grid, the same
        // widths supplied to the native table engine, including merged cells.
        if (VirtualView.Document.StructuredRoot is null)
            for (RichNode node = snapshot.Get<RichParagraph>(point.Paragraph); node.Parent is { } parent; node = snapshot.Get<RichNode>(parent))
                if (node is RichTableCell cell)
                {
                    var row = snapshot.Get<RichTableRow>(cell.Parent!.Value);
                    var table = snapshot.Get<RichTable>(row.Parent!.Value);
                    width = table.Columns.Skip(cell.Column).Take(cell.ColumnSpan).Sum(column => column.Points) * unitsPerPoint;
                    width -= (cell.Format.Padding.Left.Points + cell.Format.Padding.Right.Points) * unitsPerPoint;
                    break;
                }
        return Math.Max(1, width - (paragraph.LeadingIndent.Points + paragraph.TrailingIndent.Points) * unitsPerPoint - 1);
    }
}
