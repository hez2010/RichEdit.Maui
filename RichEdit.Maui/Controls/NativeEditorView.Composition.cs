namespace RichEdit.Maui;

public partial class NativeEditorView
{
    /// <summary>Requests completion of native composition in any view of this document without moving focus.</summary>
    /// <returns>True when all input is committed. False when native input must finish through the platform's own focus or IME interaction.</returns>
    public bool TryCompleteComposition() => Document.TryCompleteComposition();

    private RichTextDocument? _compositionDocument;
    private RichTextDocumentSnapshot? _compositionSnapshot;
    private TextSelectionState? _compositionSelection;
    private object? _compositionSourceToken;
    private bool _committingComposition;
    private bool _structuredCompositionPrepared;
    private RichTextDocument? _compositionPreviewDocument;
    private RichTextDocument? _completingPreviewDocument;
    private readonly object _previewSourceToken = new();
    private readonly List<RichTextChange> _compositionPositionChanges = [];

    /// <summary>Gets the provisional native composition snapshot, or null when no provisional text exists.</summary>
    /// <remarks>The snapshot has no valid document revision and cannot authorize checked edits or decorations.</remarks>
    public RichTextDocumentSnapshot? CompositionSnapshot
    {
        get
        {
            VerifyAccess();
            return _compositionSnapshot;
        }
    }

    /// <summary>Gets the native selection within the provisional snapshot, or null outside composition.</summary>
    public TextSelectionState? CompositionSelection
    {
        get
        {
            VerifyAccess();
            return Composition.IsActive ? InputSelection : null;
        }
    }

    internal RichTextDocument InputDocument => _compositionPreviewDocument ?? Document;

    internal RichTextDocumentSnapshot InputSnapshot => InputDocument.CurrentSnapshot;

    internal TextSelectionState InputSelection
    {
        get => _compositionSelection ?? SelectionState;
        set
        {
            VerifyAccess();
            value.Range.Validate(InputSnapshot.Length, nameof(value));
            if (!Composition.IsActive)
            {
                SelectionState = value;
                return;
            }
            _compositionSelection = value;
            OnPropertyChanged(nameof(CompositionSelection));
            (Handler as IRichEditorHandler)?.SetSelection(value);
            CompositionChanged?.Invoke(this, new(_composition, _composition));
        }
    }

    private void StageComposition(RichTextDocumentSnapshot snapshot, TextSelectionState selection, object sourceToken)
    {
        _compositionDocument ??= Document;
        _compositionPreviewDocument ??= new RichTextDocument(Document.CurrentSnapshot);
        var changes = _compositionPreviewDocument.ReplaceSnapshotFromNative(snapshot, _previewSourceToken, nativeUndoOwned: true);
        if (SourceSession?.Composition is { } composition)
            composition.Update(edit =>
            {
                foreach (var change in changes.Changes.OfType<RichTextTextChange>()) edit.Replace(change.OldRange, change.InsertedText);
            }, new(selection.Anchor, selection.Active));
        if (StructuredSession?.Composition is { } richComposition)
            richComposition.Update(edit =>
            {
                RichTextDocument.ApplyStructuredText(edit, changes.Changes, StructuredSession.TypingCharacterFormat);
                edit.SetSelection(RichTextDocument.ToStructuredSelection(edit.Snapshot.ProjectText(), selection));
            });
        _compositionPositionChanges.AddRange(changes.Changes.Where(change => change.Kind == RichTextChangeKind.Text));
        _compositionSnapshot = _compositionPreviewDocument.CurrentSnapshot.With();
        _compositionSelection = selection;
        _compositionSourceToken = sourceToken;
        OnPropertyChanged(nameof(CompositionSnapshot));
        OnPropertyChanged(nameof(CompositionSelection));
        CompositionChanged?.Invoke(this, new(_composition, _composition));
        TextLayout.Invalidate();
    }

    private void CommitComposition()
    {
        if (_structuredCompositionPrepared && StructuredSession?.Composition is { } structural)
        {
            ClearCompositionSnapshot(notify: false);
            try { structural.Commit(); }
            finally
            {
                (Handler as RichEditorHandler)?.CompleteCompositionTracking();
                NotifyCompositionSnapshot();
            }
            return;
        }
        SourceSession?.Composition?.Cancel();
        StructuredSession?.Composition?.Cancel();
        var snapshot = _compositionSnapshot;
        var selection = _compositionSelection ?? SelectionState;
        var token = _compositionSourceToken;
        var document = _compositionDocument;
        _completingPreviewDocument = _compositionPreviewDocument;
        ClearCompositionSnapshot(notify: false);
        if (snapshot is not null && token is not null && ReferenceEquals(Document, document))
        {
            _committingComposition = true;
            try
            {
                UpdateDocumentFromPlatform(snapshot, selection.Range.Start, selection.Range.Length, token,
                    RichTextChangeOrigin.User, selectionState: selection);
            }
            finally
            {
                _committingComposition = false;
                (Handler as RichEditorHandler)?.CompleteCompositionTracking();
                _completingPreviewDocument = null;
                NotifyCompositionSnapshot();
            }
        }
        else
        {
            (Handler as RichEditorHandler)?.CompleteCompositionTracking();
            if (ReferenceEquals(Document, document))
                SetSelectionFromPlatform(selection.Clamp(Document.Length));
            _completingPreviewDocument = null;
            NotifyCompositionSnapshot();
        }
    }

    private void ClearCompositionSnapshot(bool notify = true)
    {
        _structuredCompositionPrepared = false;
        _compositionSnapshot = null;
        _compositionSelection = null;
        _compositionDocument = null;
        _compositionSourceToken = null;
        _compositionPreviewDocument = null;
        _compositionPositionChanges.Clear();
        if (notify)
            NotifyCompositionSnapshot();
    }

    internal bool PrepareStructuredComposition()
    {
        if (_structuredCompositionPrepared || StructuredSession?.Composition is not { } composition ||
            composition.Selection is not (RichEdit.Core.RichCellSelection or RichEdit.Core.RichBoundarySelection)) return false;
        composition.Update(edit => edit.SetSelection(edit.ReplaceSelection(composition.Selection, "", StructuredSession.TypingCharacterFormat)));
        var projection = composition.Snapshot.ProjectText();
        var selected = (RichEdit.Core.RichTextSelection)composition.Selection;
        _compositionDocument = Document;
        _compositionPreviewDocument = new RichTextDocument(RichTextDocumentSnapshot.CreateStructuredProjection(projection));
        _compositionSnapshot = _compositionPreviewDocument.CurrentSnapshot.With();
        _compositionSelection = new(projection.GetOffset(selected.Anchor), projection.GetOffset(selected.Active));
        _structuredCompositionPrepared = true;
        NotifyCompositionSnapshot();
        TextLayout.Invalidate();
        return true;
    }

    private void NotifyCompositionSnapshot()
    {
        OnPropertyChanged(nameof(CompositionSnapshot));
        OnPropertyChanged(nameof(CompositionSelection));
    }

    internal void DiscardComposition()
    {
        SourceSession?.Composition?.Cancel();
        StructuredSession?.Composition?.Cancel();
        var preview = _compositionPreviewDocument;
        if (preview is not null && ReferenceEquals(_compositionDocument, Document))
            preview.ReplaceSnapshotFromNative(Document.CurrentSnapshot, _previewSourceToken, nativeUndoOwned: true);
        ClearCompositionSnapshot(notify: false);
        (Handler as RichEditorHandler)?.CompleteCompositionTracking();
        SetCompositionState(default);
        NotifyCompositionSnapshot();
    }

    private void SynchronizeCompositionTracking(RichTextDocumentSnapshot snapshot, IReadOnlyList<RichTextChange>? changes)
    {
        _completingPreviewDocument?.ReplaceSnapshotFromNative(snapshot, _previewSourceToken, nativeUndoOwned: true,
            recordedChanges: changes);
    }

    internal IReadOnlyList<TextSpan> InputFoldRanges => Composition.IsActive
        ? Folding.EffectiveRanges.Select(MapRangeToInput).Where(range => !range.IsEmpty).ToArray()
        : Folding.EffectiveRanges;

    internal TextSpan MapRangeToInput(TextSpan range)
    {
        foreach (var change in _compositionPositionChanges.OfType<RichTextTextChange>())
            range = RichTextPositionMap.Map(range, change, PositionAffinity.AfterInsertion,
                PositionAffinity.BeforeInsertion, TrackingDeletionBehavior.Preserve)!.Value;
        return range.Clamp(InputSnapshot.Length);
    }

    private void VerifySelectionMutation()
    {
        if (!_deferNotifications) SourceSession?.Document.VerifyIdle();
        if (!_deferNotifications && !_synchronizingStructured) StructuredSession?.Document.VerifyIdle();
        if (_planningInput)
            throw new InvalidOperationException("Input rules must propose selections through their transaction builder.");
        if (Composition.IsActive && !_synchronizingSelection && !_deferNotifications && !_updatingSelections)
            throw new InvalidOperationException("Complete native composition before changing the document selection.");
    }
}
