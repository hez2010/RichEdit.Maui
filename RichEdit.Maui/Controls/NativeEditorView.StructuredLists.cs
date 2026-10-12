using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private RichEdit.Core.RichTextSelection? StructuredPrimaryText => StructuredSession?.Selection switch
    {
        RichEdit.Core.RichTextSelection text => text,
        RichTextSelectionSet set => set.Primary.Selection,
        _ => null,
    };

    internal bool TryHandleStructuredListKey(EditorKey key)
    {
        if (StructuredInputCoordinator is { } coordinator) return coordinator.TryHandleStructuredListKey(key);
        if (StructuredSession is not { } session || IsReadOnly || Composition.IsActive || session.Composition is not null ||
            StructuredPrimaryText is not { } selection || selection.Anchor != selection.Active) return false;
        var snapshot = session.Document.CurrentSnapshot;
        var point = selection.Active;
        var paragraph = snapshot.Get<RichParagraph>(point.Paragraph);
        if (paragraph.Parent is not { } parent || snapshot.Nodes[parent] is not RichListItem item) return false;
        if (key == EditorKey.Enter)
        {
            // The native control continues nonempty items itself. Readback maps
            // that paragraph break into a list-item split without replacing the story.
            if (snapshot.GetParagraphLength(point.Paragraph) > 0 &&
                session.Selection is not RichTextSelectionSet { Count: > 1 } &&
                Handler is RichEditorHandler { HasStructuredNativeDocument: true }) return false;
            return ApplyStructuredNativeInput(session.Selection, "\n");
        }
        if (key != EditorKey.Backspace || point.Offset != 0 || item.Blocks[0] != point.Paragraph ||
            session.Selection is RichTextSelectionSet { Count: > 1 }) return false;
        var result = session.Edit(edit =>
        {
            var caret = edit.JoinListItem(point);
            edit.SetSelection(new RichEdit.Core.RichTextSelection(caret, caret));
        }, new(Origin: TextChangeOrigin.User));
        if (result.Accepted) FocusStructuredSelection();
        return result.Accepted;
    }

    internal bool TryHandleStructuredListReplacement(RichSelection replaced, string text)
    {
        if (StructuredPrimaryText is not { } current || current.Anchor != current.Active || Composition.IsActive) return false;
        if (text is "\n" or "\r" or "\r\n" && replaced is RichEdit.Core.RichTextSelection insertion && insertion.Anchor == current.Active && insertion.Active == current.Active)
            return TryHandleStructuredListKey(EditorKey.Enter);
        if (text.Length == 0 && replaced is RichEdit.Core.RichTextSelection removal && removal.Active == current.Active && removal.Anchor.Paragraph != current.Active.Paragraph)
            return TryHandleStructuredListKey(EditorKey.Backspace);
        return false;
    }

    private bool TryHandleStructuredListNativeText(RichTextDocumentSnapshot native)
    {
        if (StructuredSession is null || StructuredPrimaryText is not { } selection || selection.Anchor != selection.Active || Composition.IsActive ||
            Document.StructuredProjection is not { } projection || !projection.ContainsParagraph(selection.Active.Paragraph)) return false;
        var position = projection.GetOffset(selection.Active);
        var before = Document.CurrentSnapshot.Text;
        if (RichTextDocumentSnapshot.TryGetReplacement(before, native.Text, new(position, 0), out var inserted) && inserted is "\n" or "\r" or "\r\n")
            return TryHandleStructuredListKey(EditorKey.Enter);
        if (selection.Active.Offset == 0 && position > 0 && before[position - 1] == '\n' &&
            RichTextDocumentSnapshot.TryGetReplacement(before, native.Text, new(position - 1, 1), out var removed) && removed.Length == 0)
            return TryHandleStructuredListKey(EditorKey.Backspace);
        return false;
    }
}
