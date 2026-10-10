using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private WeakReference<NativeEditorView>? _structuredInputSurface;
    private RichTextViewScope? _coordinatedCompositionScope;
    private RichTextViewScope? StructuredInputScope => _coordinatedCompositionScope ?? Document.StructuredScope;

    private RichTextProjection ProjectStructuredInput(RichDocumentSnapshot snapshot) => StructuredInputScope is { } scope
        ? scope.Project(snapshot) ?? throw new InvalidOperationException("The native input scope was removed.") : snapshot.ProjectText();
    internal RichTextProjection? ScopedInputProjection => Composition.IsActive && StructuredSession?.Composition is { } composition
        ? ProjectStructuredInput(composition.Snapshot) : Document.StructuredProjection;

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
        var scopedBefore = RichTextDocumentSnapshot.CreateStructuredProjection(ProjectStructuredInput(before));
        var changes = RichTextDocument.CreateDelta(scopedBefore, snapshot).ToArray();
        if (!Composition.IsActive && !changes.Any(change => change.Kind == RichTextChangeKind.Text))
        {
            if (changes.Length != 0)
                session.Edit(edit => RichTextDocument.ApplyStructuredFormatting(edit, snapshot, changes, StructuredInputScope), new(Origin: TextChangeOrigin.User));
            // Focus and native format observations are not selection gestures.
            // Preserve the parent's structural selection and primary caret.
            return;
        }
        if (!Composition.IsActive && session.Selection is RichCellSelection or RichBlockSelection or RichBoundarySelection &&
            changes.OfType<RichTextTextChange>().FirstOrDefault() is { } structural)
        {
            coordinator.ApplyStructuredNativeInput(session.Selection, structural.InsertedText,
                new(Math.Max(0, selection.Anchor - structural.NewRange.Start), Math.Max(0, selection.Active - structural.NewRange.Start)));
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
            return;
        }
        var builder = new RichDocumentEdit(before, session.Composition?.NextNodeId ?? session.Document.NextNodeId);
        RichDocumentSnapshot candidate;
        RichEdit.Core.RichTextSelection selected;
        try
        {
            RichTextDocument.ApplyStructuredText(builder, changes, session.TypingCharacterFormat, StructuredInputScope);
            if (!Composition.IsActive && !changes.Any(change => change.Kind == RichTextChangeKind.Text))
                RichTextDocument.ApplyStructuredFormatting(builder, snapshot, changes, StructuredInputScope);
            candidate = builder.Finish().Snapshot;
            selected = RichTextDocument.ToStructuredSelection(ProjectStructuredInput(candidate), selection);
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
        if (state.IsActive && !previous.IsActive)
        {
            coordinator.SetCompositionState(new(true, null));
            if (coordinator.PrepareStructuredComposition())
            {
                var composition = StructuredSession!.Composition!;
                var caret = ((RichEdit.Core.RichTextSelection)composition.Selection).Active;
                var projected = Document.ProjectStructuredSnapshot(composition.Snapshot);
                if (projected is null || !projected.ContainsParagraph(caret.Paragraph))
                {
                    _coordinatedCompositionScope = RichTextViewScope.AroundParagraph(composition.Snapshot, caret.Paragraph);
                    projected = _coordinatedCompositionScope.Project(composition.Snapshot)!;
                }
                _compositionPreviewDocument = new RichTextDocument(RichTextDocumentSnapshot.CreateStructuredProjection(projected));
                _compositionSnapshot = _compositionPreviewDocument.CurrentSnapshot.With();
                var position = projected.GetOffset(caret);
                _compositionSelection = new(position, position);
                RefreshTypingFormats();
                (Handler as RichEditorHandler)?.ApplyStructuredCompositionPreview(_compositionSnapshot, _compositionSelection.Value);
                NotifyCompositionSnapshot();
            }
        }
        if (!state.IsActive) ClearCompositionSnapshot(notify: false);
        TextSpan? range = null;
        if (state.Range is { } local)
        {
            var snapshot = StructuredSession!.Composition?.Snapshot ?? StructuredSession.Document.CurrentSnapshot;
            var scoped = ProjectStructuredInput(snapshot);
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
