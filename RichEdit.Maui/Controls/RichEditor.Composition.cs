namespace RichEdit.Maui;

public sealed partial class RichEditor
{
    /// <summary>Requests completion of native composition in any view of this document without moving focus.</summary>
    /// <returns>True when all input is committed. False when native input must finish through the platform's own focus or IME interaction.</returns>
    public bool TryCompleteComposition() => Document.TryCompleteComposition();

    private RichTextDocument? _compositionDocument;
    private RichTextDocumentSnapshot? _compositionSnapshot;
    private RichTextSelectionState? _compositionSelection;
    private object? _compositionSourceToken;
    private bool _committingComposition;
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
    public RichTextSelectionState? CompositionSelection
    {
        get
        {
            VerifyAccess();
            return Composition.IsActive ? InputSelection : null;
        }
    }

    internal RichTextDocument InputDocument => _compositionPreviewDocument ?? Document;

    internal RichTextDocumentSnapshot InputSnapshot => InputDocument.CurrentSnapshot;

    internal RichTextSelectionState InputSelection
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

    private void StageComposition(RichTextDocumentSnapshot snapshot, RichTextSelectionState selection, object sourceToken)
    {
        _compositionDocument ??= Document;
        _compositionPreviewDocument ??= new RichTextDocument(Document.CurrentSnapshot);
        var changes = _compositionPreviewDocument.ReplaceSnapshotFromNative(snapshot, _previewSourceToken, nativeUndoOwned: true);
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
        _compositionSnapshot = null;
        _compositionSelection = null;
        _compositionDocument = null;
        _compositionSourceToken = null;
        _compositionPreviewDocument = null;
        _compositionPositionChanges.Clear();
        if (notify)
            NotifyCompositionSnapshot();
    }

    private void NotifyCompositionSnapshot()
    {
        OnPropertyChanged(nameof(CompositionSnapshot));
        OnPropertyChanged(nameof(CompositionSelection));
    }

    internal void DiscardComposition()
    {
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

    internal IReadOnlyList<RichTextRange> InputFoldRanges => Composition.IsActive
        ? Folding.EffectiveRanges.Select(MapRangeToInput).Where(range => !range.IsEmpty).ToArray()
        : Folding.EffectiveRanges;

    internal RichTextRange MapRangeToInput(RichTextRange range)
    {
        foreach (var change in _compositionPositionChanges.OfType<RichTextTextChange>())
            range = RichTextPositionMap.Map(range, change, RichTextTrackingAffinity.AfterInsertion,
                RichTextTrackingAffinity.BeforeInsertion, RichTextTrackingDeletionBehavior.Preserve)!.Value;
        return range.Clamp(InputSnapshot.Length);
    }

    private void VerifySelectionMutation()
    {
        if (Composition.IsActive && !_synchronizingSelection && !_deferNotifications && !_updatingSelections)
            throw new InvalidOperationException("Complete native composition before changing the document selection.");
    }
}
