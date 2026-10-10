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
        previous.ReadOnlyChanged -= OnStructuredReadOnlyChanged;
        StructuredSession = null;
        previous.Dispose();
    }

    internal void BindStructuredDocument(RichDocument document, RichNodeId? root = null)
    {
        VerifyAccess();
        if (ReferenceEquals(StructuredSession?.Document, document) && Document.StructuredRoot == root) return;
        if (SourceSession is not null) throw new InvalidOperationException("A source view cannot bind a rich document.");
        Document.VerifyAttachmentChange();
        document.VerifyIdle();
        var projection = RichTextDocument.ProjectStructured(document, root);
        var next = document.CreateSession();
        next.IsReadOnly = IsReadOnly;
        if (root is not null)
        {
            var point = projection.StructuredProjection!.GetPoint(0)!.Value;
            next.Selection = new RichEdit.Core.RichTextSelection(point, point);
        }
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
            next.ReadOnlyChanged += OnStructuredReadOnlyChanged;
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
            previous.ReadOnlyChanged -= OnStructuredReadOnlyChanged;
            previous.Dispose();
        }
    }

    private void OnStructuredTypingFormatChanged(object? sender, EventArgs args)
    {
        RefreshTypingFormats();
        ApplyTypingFormatToHandler();
        RaiseSelectionFormatChanged();
    }

    private void OnStructuredReadOnlyChanged(object? sender, EventArgs args) => IsReadOnly = StructuredSession!.IsReadOnly;

    private void OnStructuredSelectionChanged(object? sender, EventArgs args)
    {
        if (_deferNotifications || _synchronizingStructured) return;
        _synchronizingStructured = true;
        try
        {
            SetSelectionsFromDocument(GetStructuredSelections()!);
            (Handler as RichEditorHandler)?.RefreshStructuredSelection();
            Commands.Refresh();
        }
        finally { _synchronizingStructured = false; }
    }

    private void SynchronizeStructuredSelection()
    {
        if (_synchronizingStructured || StructuredSession is not { } session || session.Composition is not null) return;
        if (Document.StructuredProjection is not { } projection) return;
        var selection = new RichTextSelectionSet(projection.Snapshot, Selections.Select(item =>
            new RichTextSelectionItem(RichTextDocument.ToStructuredSelection(projection, new(item.Anchor, item.Active)))
            { Id = item.Id, PreferredVisualX = item.PreferredVisualX }), Selections.PrimaryIndex);
        if (selection != session.Selection) session.Selection = selection;
    }

    private bool ReplaceStructuredSelections(string text)
    {
        if (IsReadOnly || Document.HasActiveComposition) return false;
        return StructuredSession!.ReplaceSelection(text).Accepted;
    }

    internal bool DeleteStructuredCells()
    {
        if (StructuredSession?.Selection is not (RichCellSelection or RichBlockSelection) || Composition.IsActive) return false;
        ReplaceStructuredSelections("");
        return true;
    }

    private TextSelectionSet? GetStructuredSelections()
    {
        if (StructuredSession is not { } session) return null;
        if (Document.StructuredProjection is not { } projection) return TextSelectionSet.Caret(0);
        var paragraphsInScope = Document.StructuredRoot is null ? null : projection.Paragraphs.Select(paragraph => paragraph.Paragraph).ToHashSet();
        bool InScope(RichNodeId paragraph) => paragraphsInScope?.Contains(paragraph) ?? true;
        if (session.Selection is RichTextSelectionSet set)
        {
            var scopedSelections = set.Where(item => InScope(item.Selection.Anchor.Paragraph) && InScope(item.Selection.Active.Paragraph))
                .Select(item => new TextSelection(projection.GetOffset(item.Selection.Anchor), projection.GetOffset(item.Selection.Active))
                { Id = item.Id, Affinity = item.Selection.Active.Affinity, PreferredVisualX = item.PreferredVisualX }).ToArray();
            return scopedSelections.Length == 0 ? TextSelectionSet.Caret(0) : new(scopedSelections, Math.Max(0, Array.FindIndex(scopedSelections, item => item.Id == set.Primary.Id)));
        }
        TextSpan range;
        if (session.Selection is RichEdit.Core.RichTextSelection text)
            return InScope(text.Anchor.Paragraph) && InScope(text.Active.Paragraph)
                ? new((TextSelection[])[new(projection.GetOffset(text.Anchor), projection.GetOffset(text.Active)) { Id = _structuredPrimaryId }]) : TextSelectionSet.Caret(0);
        if (session.Selection is RichObjectSelection selected)
            range = projection.Objects.FirstOrDefault(item => item.Object == selected.Object) is { } item ? new(item.Position, 1) : TextSpan.Empty;
        else if (session.Selection is RichBlockSelection blocks)
        {
            var selectedText = projection.Snapshot.GetSelectedText(blocks).Where(range => InScope(range.Start.Paragraph)).ToArray();
            if (selectedText.Length == 0) return TextSelectionSet.Caret(0);
            var first = projection.GetOffset(selectedText[0].Start);
            var last = selectedText[^1];
            range = new(first, projection.GetOffset(last.Start) + last.Length - first);
        }
        else if (session.Selection is RichCellSelection cells)
        {
            var snapshot = projection.Snapshot;
            var grid = snapshot.GetTableGrid(cells.Table);
            var selectedCells = Enumerable.Range(cells.Row, cells.RowCount).SelectMany(row => Enumerable.Range(cells.Column, cells.ColumnCount).Select(column => grid[row, column])).ToHashSet();
            var paragraphs = projection.Paragraphs.Where(paragraph => InSelectedCell(paragraph.Paragraph)).ToArray();
            if (paragraphs.Length == 0) return TextSelectionSet.Caret(0);
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
            if (paragraphs.Length == 0) return TextSelectionSet.Caret(0);
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
