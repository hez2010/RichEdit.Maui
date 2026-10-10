using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private WeakReference<NativeEditorView>? _structuredInputSurface;

    internal bool TryCompleteNativeComposition()
    {
        if (_structuredInputSurface?.TryGetTarget(out var surface) == true && surface.HasCurrentStructuredCoordinator && surface.Composition.IsActive)
            return surface.TryCompleteNativeComposition();
        return Handler is IRichEditorHandler handler && handler.TryCompleteComposition();
    }

    private bool HasCurrentStructuredCoordinator => StructuredInputCoordinator is { } coordinator &&
        ReferenceEquals(coordinator.StructuredSession, StructuredSession);

    private void ForwardStructuredInput(RichTextDocumentSnapshot snapshot, TextSelectionState selection)
    {
        var coordinator = StructuredInputCoordinator!;
        if (!HasCurrentStructuredCoordinator || Document.StructuredProjection is null)
        {
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
            return;
        }
        if (coordinator.Composition.IsActive && coordinator._structuredInputSurface?.TryGetTarget(out var composingSurface) == true &&
            !ReferenceEquals(composingSurface, this))
        {
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
            return;
        }
        var session = StructuredSession!;
        coordinator.RememberStructuredInput(this);
        var before = session.Composition?.Snapshot ?? session.Document.CurrentSnapshot;
        var scopedBefore = RichTextDocumentSnapshot.CreateStructuredProjection(before.ProjectText(Document.StructuredRoot));
        var changes = RichTextDocument.CreateDelta(scopedBefore, snapshot).ToArray();
        var builder = new RichDocumentEdit(before, session.Composition?.NextNodeId ?? session.Document.NextNodeId);
        RichDocumentSnapshot candidate;
        RichEdit.Core.RichTextSelection selected;
        try
        {
            RichTextDocument.ApplyStructuredText(builder, changes, session.TypingCharacterFormat, Document.StructuredRoot);
            if (!Composition.IsActive && !changes.Any(change => change.Kind == RichTextChangeKind.Text))
                RichTextDocument.ApplyStructuredFormatting(builder, snapshot, changes, Document.StructuredRoot);
            candidate = builder.Finish().Snapshot;
            selected = RichTextDocument.ToStructuredSelection(candidate.ProjectText(Document.StructuredRoot), selection);
        }
        finally { builder.Close(); }
        var full = candidate.ProjectText();
        var mapped = new TextSelectionState(full.GetOffset(selected.Anchor), full.GetOffset(selected.Active));
        coordinator.UpdateDocumentFromPlatform(RichTextDocumentSnapshot.CreateStructuredProjection(full), mapped.Range.Start, mapped.Range.Length,
            coordinator.EditSourceToken, selectionState: mapped);
        if (Composition.IsActive)
        {
            _compositionDocument ??= Document;
            _compositionPreviewDocument ??= new RichTextDocument(Document.CurrentSnapshot);
            var local = _compositionPreviewDocument.ReplaceSnapshotFromNative(snapshot, _previewSourceToken, nativeUndoOwned: true);
            _compositionPositionChanges.AddRange(local.Changes.Where(change => change.Kind == RichTextChangeKind.Text));
            _compositionSnapshot = _compositionPreviewDocument.CurrentSnapshot.With();
            _compositionSelection = selection;
            NotifyCompositionSnapshot();
            TextLayout.Invalidate();
        }
        else
        {
            // A rejected input or a no-op has no document publication to repair
            // the native surface. Restore the committed scoped render description.
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
        }
    }

    private void SetScopedCompositionState(RichTextCompositionState state)
    {
        var previous = _composition;
        if (!HasCurrentStructuredCoordinator) { _composition = state; ClearCompositionSnapshot(); return; }
        var coordinator = StructuredInputCoordinator!;
        if (state.IsActive && !previous.IsActive)
        {
            if (coordinator._structuredInputSurface?.TryGetTarget(out var other) == true && !ReferenceEquals(other, this) &&
                other.Composition.IsActive && !other.TryCompleteNativeComposition())
                throw new InvalidOperationException("Complete marked input in the current surface before starting another composition.");
            coordinator.RememberStructuredInput(this);
            _compositionDocument = Document;
            _compositionPreviewDocument = new RichTextDocument(Document.CurrentSnapshot);
        }
        _composition = state;
        if (!state.IsActive) ClearCompositionSnapshot(notify: false);
        TextSpan? range = null;
        if (state.Range is { } local)
        {
            var snapshot = StructuredSession!.Composition?.Snapshot ?? StructuredSession.Document.CurrentSnapshot;
            var scoped = snapshot.ProjectText(Document.StructuredRoot);
            if (local.End <= scoped.Text.Length && scoped.GetPoint(local.Start) is { } start && scoped.GetPoint(local.End) is { } end)
            {
                var full = snapshot.ProjectText();
                var first = full.GetOffset(start);
                range = new(first, full.GetOffset(end) - first);
            }
        }
        coordinator.SetCompositionState(new(state.IsActive, range));
        if (!state.IsActive)
        {
            (Handler as RichEditorHandler)?.CompleteCompositionTracking();
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
            NotifyCompositionSnapshot();
        }
        Commands.Refresh();
        CompositionChanged?.Invoke(this, new(previous, state));
        TextLayout.Invalidate();
    }

    private void RememberStructuredInput(NativeEditorView surface)
    {
        if (_structuredInputSurface is null) _structuredInputSurface = new(surface);
        else _structuredInputSurface.SetTarget(surface);
    }
}
