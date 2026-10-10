using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    internal bool MoveToAdjacentCell(bool forward, bool appendRow)
    {
        VerifyAccess();
        if (StructuredInputCoordinator is { } coordinator) return coordinator.MoveToAdjacentCell(forward, appendRow);
        if (StructuredSession is not { } session || Composition.IsActive || session.Composition is not null ||
            session.Selection is RichTextSelectionSet { Count: > 1 }) return false;
        var snapshot = session.Document.CurrentSnapshot;
        RichNodeId cellId;
        RichNodeId tableId;
        if (session.Selection is RichCellSelection selected)
        {
            var grid = snapshot.GetTableGrid(selected.Table);
            var extent = grid.Expand(selected);
            cellId = forward ? grid[extent.Row + extent.RowCount - 1, extent.Column + extent.ColumnCount - 1] : grid[extent.Row, extent.Column];
            tableId = selected.Table;
        }
        else
        {
            RichNode? node = StructuredPrimaryText is { } text ? snapshot.Get<RichParagraph>(text.Active.Paragraph) :
                session.Selection is RichObjectSelection atom ? snapshot.Get<RichInline>(atom.Object) : null;
            while (node is not null && node is not RichTableCell)
                node = node.Parent is { } parent ? snapshot.Get<RichNode>(parent) : null;
            if (node is not RichTableCell cell) return false;
            cellId = cell.Id;
            tableId = snapshot.Get<RichTableRow>(cell.Parent!.Value).Parent!.Value;
        }
        var table = snapshot.Get<RichTable>(tableId);
        using var selectionUpdate = BeginStructuredSurfaceUpdate();
        var cells = table.Rows.SelectMany(row => snapshot.Get<RichTableRow>(row).Cells).ToArray();
        var next = Array.IndexOf(cells, cellId) + (forward ? 1 : -1);
        if (next < 0) return false;
        if (next >= cells.Length)
        {
            if (!appendRow || IsReadOnly || session.IsReadOnly) return false;
            try
            {
                if (!session.Edit(edit =>
                {
                    edit.InsertRows(tableId, table.Rows.Count);
                    var after = edit.Snapshot;
                    if (!AcceptsStructuredInputLength(after)) throw new StructuredInputLimitException();
                    var point = FirstPoint(after, after.GetTableGrid(tableId)[table.Rows.Count, 0]);
                    edit.SetSelection(new RichEdit.Core.RichTextSelection(point, point));
                }, new("Insert table row", Origin: TextChangeOrigin.User)).Accepted) return false;
            }
            catch (StructuredInputLimitException) { return false; }
        }
        else
        {
            var point = FirstPoint(snapshot, cells[next]);
            session.Selection = new RichEdit.Core.RichTextSelection(point, point);
        }
        RequestStructuredFocus();
        if (StructuredPrimaryText is { } caret)
        {
            var surface = FindStructuredSurface(caret) ?? this;
            if (surface.Document.StructuredProjection is { } projection && projection.ContainsParagraph(caret.Active.Paragraph))
                surface.ScrollIntoView(new(projection.GetOffset(caret.Active), 0));
        }
        return true;

        static RichTextPoint FirstPoint(RichDocumentSnapshot source, RichNodeId cell) => new(source.Body, source.EnumerateParagraphs(cell).First(), 0);
    }
}
