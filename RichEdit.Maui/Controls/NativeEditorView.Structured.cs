using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    internal RichEditorSession? StructuredSession { get; private set; }
    private bool _synchronizingStructured;
    private readonly Guid _structuredPrimaryId = Guid.NewGuid();

    private void ReleaseStructuredBinding(RichTextDocument next)
    {
        if (StructuredSession is not { } previous || ReferenceEquals(next.StructuredDocument, previous.Document)) return;
        DiscardComposition();
        previous.SelectionChanged -= OnStructuredSelectionChanged;
        previous.TypingFormatChanged -= OnStructuredTypingFormatChanged;
        StructuredSession = null;
        previous.Dispose();
    }

    internal void BindStructuredDocument(RichDocument document)
    {
        VerifyAccess();
        if (ReferenceEquals(StructuredSession?.Document, document)) return;
        if (SourceSession is not null) throw new InvalidOperationException("A source view cannot bind a rich document.");
        Document.VerifyAttachmentChange();
        document.VerifyIdle();
        var next = document.CreateSession();
        next.IsReadOnly = IsReadOnly;
        var projection = RichTextDocument.ProjectStructured(document);
        var previous = StructuredSession;
        DiscardComposition();
        StructuredSession = next;
        _synchronizingStructured = true;
        try
        {
            Document = projection;
            Selections = GetStructuredSelections()!;
            next.SelectionChanged += OnStructuredSelectionChanged;
            next.TypingFormatChanged += OnStructuredTypingFormatChanged;
        }
        catch
        {
            if (!ReferenceEquals(Document, projection)) { StructuredSession = previous; next.Dispose(); }
            throw;
        }
        finally { _synchronizingStructured = false; }
        if (previous is not null)
        {
            previous.SelectionChanged -= OnStructuredSelectionChanged;
            previous.TypingFormatChanged -= OnStructuredTypingFormatChanged;
            previous.Dispose();
        }
    }

    private void OnStructuredTypingFormatChanged(object? sender, EventArgs args)
    {
        RefreshTypingFormats();
        ApplyTypingFormatToHandler();
        RaiseSelectionFormatChanged();
    }

    private void OnStructuredSelectionChanged(object? sender, EventArgs args)
    {
        if (_deferNotifications || _synchronizingStructured) return;
        _synchronizingStructured = true;
        try { SetSelectionsFromDocument(GetStructuredSelections()!); }
        finally { _synchronizingStructured = false; }
    }

    private void SynchronizeStructuredSelection()
    {
        if (_synchronizingStructured || StructuredSession is not { } session || session.Composition is not null) return;
        var projection = Document.StructuredProjection!;
        var selection = new RichTextSelectionSet(projection.Snapshot, Selections.Select(item =>
            new RichTextSelectionItem(RichTextDocument.ToStructuredSelection(projection, new(item.Anchor, item.Active)))
            { Id = item.Id, PreferredVisualX = item.PreferredVisualX }), Selections.PrimaryIndex);
        if (selection != session.Selection) session.Selection = selection;
    }

    private TextSelectionSet? GetStructuredSelections()
    {
        if (StructuredSession is not { } session) return null;
        var projection = Document.StructuredProjection!;
        if (session.Selection is RichTextSelectionSet set)
            return new(set.Select(item => new TextSelection(projection.GetOffset(item.Selection.Anchor), projection.GetOffset(item.Selection.Active))
            { Id = item.Id, Affinity = item.Selection.Active.Affinity, PreferredVisualX = item.PreferredVisualX }), set.PrimaryIndex);
        TextSpan range;
        if (session.Selection is RichEdit.Core.RichTextSelection text)
            return new((TextSelection[])[new(projection.GetOffset(text.Anchor), projection.GetOffset(text.Active)) { Id = _structuredPrimaryId }]);
        if (session.Selection is RichObjectSelection selected)
            range = new(projection.Objects.Single(item => item.Object == selected.Object).Position, 1);
        else if (session.Selection is RichCellSelection cells)
        {
            var snapshot = projection.Snapshot;
            var grid = snapshot.GetTableGrid(cells.Table);
            var selectedCells = Enumerable.Range(cells.Row, cells.RowCount).SelectMany(row => Enumerable.Range(cells.Column, cells.ColumnCount).Select(column => grid[row, column])).ToHashSet();
            var paragraphs = projection.Paragraphs.Where(paragraph => InSelectedCell(paragraph.Paragraph)).ToArray();
            range = new(paragraphs[0].Range.Start, paragraphs[^1].Range.End - paragraphs[0].Range.Start);
            bool InSelectedCell(RichNodeId paragraph)
            {
                for (var node = snapshot.Get<RichNode>(paragraph); node.Parent is { } parent; node = snapshot.Get<RichNode>(parent))
                    if (selectedCells.Contains(parent)) return true;
                return false;
            }
        }
        else
        {
            var boundary = (RichBoundarySelection)session.Selection;
            var snapshot = projection.Snapshot;
            var paragraphs = projection.Paragraphs.Where(paragraph => Within(paragraph.Paragraph)).ToArray();
            range = new(boundary.Edge == RichBlockEdge.Before ? paragraphs[0].Range.Start : paragraphs[^1].Range.End, 0);
            bool Within(RichNodeId id)
            {
                for (var node = snapshot.Get<RichNode>(id); ; node = snapshot.Get<RichNode>(node.Parent!.Value))
                { if (node.Id == boundary.Block) return true; if (node.Parent is null) return false; }
            }
        }
        return new((TextSelection[])[new(range.Start, range.End) { Id = _structuredPrimaryId }]);
    }
}
