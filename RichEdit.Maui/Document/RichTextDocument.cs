using System.ComponentModel;
using System.Runtime.CompilerServices;
using TextEdit.Core;

namespace RichEdit.Maui;

/// <summary>
/// Represents a stable, observable rich-text document that supports atomic range edits.
/// </summary>
public sealed partial class RichTextDocument : INotifyPropertyChanged
{
    /// <summary>
    /// The object-replacement character used to reserve a logical text position for an
    /// inline object.
    /// </summary>
    public const char ObjectReplacementCharacter = '\uFFFC';

    /// <summary>
    /// The Unicode line-separator character used for a soft line break.
    /// </summary>
    public const char SoftLineBreakCharacter = '\u2028';

    private readonly object _identity = new();
    private long _nextStateId;
    private long _stateId;
    private long _savedStateId;
    private long _lastSavePointStateId = -1;
    private long _undoGroupStateId;
    private RichTextSelectionState? _undoGroupSelection;
    private RichTextSelectionState? _undoGroupAfterSelection;
    private TextSelectionSet? _undoGroupSelections;
    private TextSelectionSet? _undoGroupAfterSelections;
    private readonly Stack<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();
    private readonly Stack<UndoGroup> _undoGroups = new();
    private RichTextDocumentSnapshot? _undoGroupBefore;
    private readonly List<RichTextChange> _undoGroupForward = [];
    private readonly List<RichTextChange> _undoGroupReverseInverse = [];
    private string? _undoGroupDescription;
    private RichTextDocumentSnapshot _snapshot;
    private string? _cachedRtf;
    private long _cachedRtfVersion = -1;
    private bool _editInProgress;
    private bool _notificationInProgress;
    private long _characterSummaryVersion = -1;
    private RichTextRange _characterSummaryRange;
    private RichTextCharacterFormatSummary? _characterSummary;
    private long _paragraphSummaryVersion = -1;
    private RichTextRange _paragraphSummaryRange;
    private RichTextParagraphFormatSummary? _paragraphSummary;
    private long _lastNativeEditTick;
    private object? _lastNativeSourceToken;
    private RichTextTextChange? _lastNativeTextChange;

    /// <summary>
    /// Initializes an empty rich-text document.
    /// </summary>
    public RichTextDocument()
        : this(new RichTextDocumentSnapshot(string.Empty))
    {
    }

    internal RichTextDocument(RichTextDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot.WithVersion(0, _identity);
        Tracking = new(this);
    }

    /// <summary>
    /// Occurs after an atomic document transaction is committed.
    /// </summary>
    public event EventHandler<RichTextDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? UndoStateChanged;

    /// <summary>
    /// Gets the monotonically increasing document version.
    /// </summary>
    public long Version => _snapshot.Version;

    /// <summary>Gets the current document identity and version as an opaque token.</summary>
    public RichTextRevision Revision => _snapshot.Revision;

    /// <summary>Gets the document's position and range tracking service.</summary>
    public RichTextTracking Tracking { get; }

    /// <summary>
    /// Gets the number of UTF-16 code units in the logical text.
    /// </summary>
    public int Length => _snapshot.Text.Length;

    /// <summary>
    /// Gets the plain-text projection of the document.
    /// </summary>
    public string Text => _snapshot.Text;

    /// <summary>
    /// Gets the canonical RTF representation of the complete document.
    /// </summary>
    /// <remarks>
    /// The value is serialized lazily and cached by <see cref="Version"/>. View
    /// appearance and native theme defaults are never serialized.
    /// </remarks>
    public string RtfText
    {
        get
        {
            if (_cachedRtfVersion != Version)
            {
                _cachedRtf = RtfCodec.Serialize(_snapshot);
                _cachedRtfVersion = Version;
            }

            return _cachedRtf!;
        }
    }

    /// <summary>Creates a plain-text document with normalized line endings and empty undo history.</summary>
    /// <param name="text">The initial text, or null for an empty document.</param>
    /// <returns>A new live document.</returns>
    public static RichTextDocument FromPlainText(string? text) => new(new RichTextDocumentSnapshot(text));

    /// <summary>
    /// Creates a document by parsing a complete RTF value.
    /// </summary>
    /// <param name="rtfText">The RTF value to parse.</param>
    /// <returns>A new rich-text document containing the parsed content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rtfText"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="rtfText"/> is not valid supported RTF.</exception>
    public static RichTextDocument FromRtf(string rtfText)
    {
        ArgumentNullException.ThrowIfNull(rtfText);
        return new RichTextDocument(RtfCodec.Parse(rtfText));
    }

    /// <summary>
    /// Gets the declared default character format.
    /// </summary>
    public RichTextCharacterFormat DefaultCharacterFormat => _snapshot.DefaultCharacterFormat;

    /// <summary>
    /// Gets the declared default paragraph format.
    /// </summary>
    public RichTextParagraphFormat DefaultParagraphFormat => _snapshot.DefaultParagraphFormat;

    /// <summary>
    /// Gets the immutable snapshot for the current document version.
    /// </summary>
    public RichTextDocumentSnapshot CurrentSnapshot => _snapshot;

    /// <summary>Gets whether a recorded edit can be undone.</summary>
    public bool CanUndo => _undo.Count != 0 && !IsUndoGroupOpen;

    /// <summary>Gets whether an undone edit can be reapplied.</summary>
    public bool CanRedo => _redo.Count != 0 && !IsUndoGroupOpen;

    /// <summary>Gets the description of the next undo operation.</summary>
    public string? UndoDescription => CanUndo ? _undo.Peek().Description : null;

    /// <summary>Gets the description of the next redo operation.</summary>
    public string? RedoDescription => CanRedo ? _redo.Peek().Description : null;

    /// <summary>Gets whether the current content differs from the last marked saved state.</summary>
    public bool IsModified => _stateId != _savedStateId;

    /// <summary>Captures the current content state before saving its snapshot.</summary>
    /// <returns>A save point belonging to this document.</returns>
    public RichTextSavePoint CreateSavePoint()
    {
        VerifyAccess();
        VerifyCompositionAccess(null);
        _lastSavePointStateId = _stateId;
        return new RichTextSavePoint(_identity, _stateId);
    }

    /// <summary>Marks the current document state as saved.</summary>
    public void MarkSaved() => MarkSaved(CreateSavePoint());

    /// <summary>Marks a captured state as saved without marking intervening edits as saved.</summary>
    /// <param name="savePoint">The state whose corresponding snapshot was successfully saved.</param>
    public void MarkSaved(RichTextSavePoint savePoint)
    {
        VerifyNoActiveEdit();
        if (!ReferenceEquals(savePoint.Owner, _identity))
            throw new ArgumentException("The save point belongs to a different document.", nameof(savePoint));
        if (_savedStateId == savePoint.StateId)
            return;

        _savedStateId = savePoint.StateId;
        _notificationInProgress = true;
        try
        {
            OnPropertyChanged(nameof(IsModified));
        }
        finally
        {
            _notificationInProgress = false;
        }
    }

    /// <summary>Gets whether an explicit undo group is open.</summary>
    public bool IsUndoGroupOpen => _undoGroups.Count != 0;

    /// <summary>Groups subsequent recorded edits into one undo unit.</summary>
    /// <remarks>
    /// Groups can nest and must be disposed in reverse order. Each edit still commits
    /// and notifies independently; this scope is not a rollback transaction. Undo and
    /// redo are unavailable until the outermost group closes. History-clearing edits
    /// and explicit history clearing are not allowed inside a group.
    /// </remarks>
    /// <param name="description">An optional description for the outermost undo unit.</param>
    /// <returns>A scope to dispose after the grouped edits.</returns>
    public IDisposable BeginUndoGroup(string? description = null)
    {
        VerifyNoActiveEdit();
        var group = new UndoGroup(this);
        if (!IsUndoGroupOpen)
        {
            _undoGroupBefore = null;
            _undoGroupForward.Clear();
            _undoGroupReverseInverse.Clear();
            _undoGroupDescription = description;
            _undoGroupStateId = _stateId;
            _undoGroupSelection = null;
            _undoGroupAfterSelection = _undoGroupSelection;
            ResetNativeEditCoalescing();
        }

        _undoGroups.Push(group);
        try
        {
            RaiseUndoStateChanged();
        }
        catch
        {
            _undoGroups.Pop();
            if (!IsUndoGroupOpen)
                _undoGroupDescription = null;

            throw;
        }

        return group;
    }

    internal void BreakUndoGroup() => ResetNativeEditCoalescing();

    internal void BreakUndoGroupForSelection(RichTextRange selection)
    {
        if (!selection.IsEmpty || _lastNativeTextChange?.NewRange.End != selection.Start)
        {
            ResetNativeEditCoalescing();
        }
    }

    /// <summary>
    /// Applies an atomic set of incremental document edits.
    /// </summary>
    /// <param name="edit">A callback that describes the edits in transaction order.</param>
    /// <param name="options">Undo and application metadata for the transaction.</param>
    /// <returns>The committed change set, or an empty change set when no edit was made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The callback attempts to start another mutation on the same document.
    /// </exception>
    public RichTextChangeSet Edit(
        Action<RichTextDocumentEdit> edit,
        RichTextEditOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return ExecuteEdit(
            edit,
            options,
            RichTextChangeOrigin.Programmatic,
            sourceToken: null);
    }

    /// <summary>
    /// Returns the plain text in a bounded document range.
    /// </summary>
    /// <param name="range">The range to read.</param>
    /// <returns>The selected plain text.</returns>
    public string GetText(RichTextRange range)
    {
        range.Validate(Length, nameof(range));
        return Text.Substring(range.Start, range.Length);
    }

    /// <summary>
    /// Summarizes declared character formatting in a document range.
    /// </summary>
    /// <param name="range">The range to inspect.</param>
    /// <returns>A character-format summary.</returns>
    public RichTextCharacterFormatSummary GetCharacterFormat(RichTextRange range)
    {
        range.Validate(Length, nameof(range));
        if (_characterSummaryVersion != Version || _characterSummaryRange != range)
        {
            _characterSummary = RichTextCharacterFormatSummary.Create(_snapshot, range);
            _characterSummaryVersion = Version;
            _characterSummaryRange = range;
        }

        return _characterSummary!;
    }

    /// <summary>
    /// Summarizes declared paragraph formatting in a document range.
    /// </summary>
    /// <param name="range">The range to inspect.</param>
    /// <returns>A paragraph-format summary.</returns>
    public RichTextParagraphFormatSummary GetParagraphFormat(RichTextRange range)
    {
        range.Validate(Length, nameof(range));
        if (_paragraphSummaryVersion != Version || _paragraphSummaryRange != range)
        {
            _paragraphSummary = RichTextParagraphFormatSummary.Create(_snapshot, range);
            _paragraphSummaryVersion = Version;
            _paragraphSummaryRange = range;
        }

        return _paragraphSummary!;
    }

    internal RichTextChangeSet Edit(
        Action<RichTextDocumentEdit> edit,
        RichTextEditOptions options,
        RichTextChangeOrigin origin,
        object? sourceToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return ExecuteEdit(edit, options, origin, sourceToken);
    }

    internal RichTextChangeSet ReplaceSnapshotFromNative(
        RichTextDocumentSnapshot snapshot,
        object sourceToken,
        bool nativeUndoOwned,
        RichTextChangeOrigin origin = RichTextChangeOrigin.User,
        bool mergeWithPrevious = false,
        long? projectedVersion = null,
        IReadOnlyList<RichTextChange>? recordedChanges = null,
        RichTextChangeSet? nativeCorrections = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sourceToken);
        if (origin is not (RichTextChangeOrigin.User or
            RichTextChangeOrigin.Undo or
            RichTextChangeOrigin.Redo))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        VerifyNoActiveEdit();
        VerifyCompositionAccess(sourceToken);
        var changes = recordedChanges ?? CreateDelta(_snapshot, snapshot);
        var completesProjection = projectedVersion == Version && _undo.TryPeek(out var projectedEdit) && projectedEdit.After.Version == Version;
        var undoBehavior = nativeUndoOwned
            ? RichTextUndoBehavior.ClearHistory
            : origin == RichTextChangeOrigin.User &&
              (completesProjection || mergeWithPrevious && ReferenceEquals(_lastNativeSourceToken, sourceToken) || CanMergeNativeEdit(changes, sourceToken))
                ? RichTextUndoBehavior.MergeWithPrevious
                : RichTextUndoBehavior.CreateUnit;
        var result = Commit(
            snapshot,
            changes,
            origin,
            new RichTextEditOptions(undoBehavior),
            sourceToken,
            nativeCorrections);
        if (!result.IsEmpty && origin == RichTextChangeOrigin.User)
        {
            _lastNativeSourceToken = sourceToken;
            _lastNativeEditTick = Environment.TickCount64;
            _lastNativeTextChange = GetMergeableTextChange(changes);
        }

        return result;
    }

    /// <summary>Restores preceding content and maps every attached view's selection through the change.</summary>
    public void Undo() => Undo(null);

    internal void Undo(RichEditor? invokingEditor)
    {
        VerifyNoActiveEdit();
        VerifyCompositionAccess(null);
        if (IsUndoGroupOpen)
            return;
        if (!_undo.TryPop(out var entry))
        {
            return;
        }

        _redo.Push(entry);
        _stateId = entry.BeforeStateId;
        TransitionTo(entry.Before, RichTextChangeOrigin.Undo, selection: entry.BeforeSelection,
            positionChanges: (RichTextChange[])[.. entry.ReverseInverse.AsEnumerable().Reverse()], selectionOwner: invokingEditor,
            selections: entry.BeforeSelections);
    }

    /// <summary>Reapplies the next content state and maps every attached view's selection through the change.</summary>
    public void Redo() => Redo(null);

    internal void Redo(RichEditor? invokingEditor)
    {
        VerifyNoActiveEdit();
        VerifyCompositionAccess(null);
        if (IsUndoGroupOpen)
            return;
        if (!_redo.TryPop(out var entry))
        {
            return;
        }

        _undo.Push(entry);
        _stateId = entry.AfterStateId;
        TransitionTo(entry.After, RichTextChangeOrigin.Redo, selection: entry.AfterSelection,
            positionChanges: entry.Forward, selectionOwner: invokingEditor, selections: entry.AfterSelections);
    }

    /// <summary>Clears undo and redo without changing content or its saved state.</summary>
    public void ClearUndoHistory()
    {
        VerifyNoActiveEdit();
        VerifyNoOpenUndoGroup();
        ResetNativeEditCoalescing();
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        RaiseUndoStateChanged();
    }

    internal void VerifyAttachmentChange() => VerifyNoActiveEdit();

    private RichTextChangeSet ExecuteEdit(
        Action<RichTextDocumentEdit> edit,
        RichTextEditOptions options,
        RichTextChangeOrigin origin,
        object? sourceToken)
    {
        VerifyNoActiveEdit();
        VerifyCompositionAccess(sourceToken);

        var transaction = new RichTextDocumentEdit(_snapshot);
        _editInProgress = true;
        try
        {
            edit(transaction);
        }
        finally
        {
            _editInProgress = false;
        }

        return Commit(transaction.Snapshot, transaction.Changes, origin, options, sourceToken);
    }

    private RichTextChangeSet Commit(
        RichTextDocumentSnapshot snapshot,
        IReadOnlyList<RichTextChange> changes,
        RichTextChangeOrigin origin,
        RichTextEditOptions options,
        object? sourceToken,
        RichTextChangeSet? nativeCorrections = null)
    {
        if (_snapshot.ContentEquals(snapshot))
        {
            return new RichTextChangeSet(
                Version,
                Version,
                origin,
                [],
                options.Tag,
                sourceToken,
                _snapshot,
                _snapshot,
                options.UndoBehavior,
                options.UndoDescription);
        }

        if (options.UndoBehavior == RichTextUndoBehavior.PreserveHistory && changes.Any(static change =>
            change.Kind is not (RichTextChangeKind.CharacterFormat or RichTextChangeKind.ParagraphFormat or RichTextChangeKind.DefaultFormat)))
        {
            throw new InvalidOperationException("Preserving undo history is supported only for character, paragraph, and default formatting.");
        }
        if (options.UndoBehavior == RichTextUndoBehavior.ClearHistory)
            VerifyNoOpenUndoGroup();

        if (origin != RichTextChangeOrigin.User && options.UndoBehavior != RichTextUndoBehavior.PreserveHistory)
        {
            ResetNativeEditCoalescing();
        }

        var before = _snapshot;
        var versionBefore = Version;
        var after = snapshot.WithVersion(checked(versionBefore + 1), _identity);
        var beforeStateId = _stateId;
        var afterStateId = checked(++_nextStateId);
        var editingView = FindEditingView(sourceToken);
        var beforeSelection = editingView?.SelectionState;
        var afterSelection = editingView?.GetSelectionAfterEdit(changes, after.Length);
        var beforeSelections = editingView?.Selections;
        var afterSelections = editingView?.GetSelectionsAfterEdit(changes, after.Length);
        var forward = changes.Where(static change => change.Kind is RichTextChangeKind.Text or RichTextChangeKind.Reset).ToArray();
        var inverse = InvertPositionChanges(before.Text, forward);

        if (IsUndoGroupOpen && options.UndoBehavior is RichTextUndoBehavior.CreateUnit or RichTextUndoBehavior.MergeWithPrevious)
        {
            if (_undoGroupBefore is null)
            {
                // PreserveHistory edits may precede the first recorded edit in this group.
                // Its snapshot and saved-state identity must describe the same content.
                _undoGroupBefore = before;
                _undoGroupStateId = beforeStateId;
                _undoGroupSelection = beforeSelection;
                _undoGroupSelections = beforeSelections;
            }
            _undoGroupAfterSelection = afterSelection;
            _undoGroupAfterSelections = afterSelections;
            _undoGroupForward.AddRange(forward);
            _undoGroupReverseInverse.AddRange(inverse.Reverse());
        }
        else
            switch (options.UndoBehavior)
            {
                case RichTextUndoBehavior.CreateUnit:
                    _undo.Push(new UndoEntry(before, after, options.UndoDescription, beforeStateId, afterStateId, beforeSelection, afterSelection, [.. forward], [.. inverse.Reverse()])
                    { BeforeSelections = beforeSelections, AfterSelections = afterSelections });
                    _redo.Clear();
                    break;
                case RichTextUndoBehavior.MergeWithPrevious:
                    if (_undo.TryPeek(out var preceding) && preceding.AfterStateId != _lastSavePointStateId)
                    {
                        _undo.Pop();
                        preceding.Forward.AddRange(forward);
                        preceding.ReverseInverse.AddRange(inverse.Reverse());
                        _undo.Push(new UndoEntry(preceding.Before, after, options.UndoDescription ?? preceding.Description, preceding.BeforeStateId, afterStateId, preceding.BeforeSelection, afterSelection,
                            preceding.Forward, preceding.ReverseInverse)
                        { BeforeSelections = preceding.BeforeSelections, AfterSelections = afterSelections });
                    }
                    else
                    {
                        _undo.Push(new UndoEntry(before, after, options.UndoDescription, beforeStateId, afterStateId, beforeSelection, afterSelection, [.. forward], [.. inverse.Reverse()])
                        { BeforeSelections = beforeSelections, AfterSelections = afterSelections });
                    }

                    _redo.Clear();
                    break;
                case RichTextUndoBehavior.ClearHistory:
                    _undo.Clear();
                    _redo.Clear();
                    break;
                case RichTextUndoBehavior.PreserveHistory:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options));
            }

        _snapshot = after;
        _stateId = afterStateId;
        InvalidateRtfCache();
        var changeSet = new RichTextChangeSet(
            versionBefore,
            after.Version,
            origin,
            [.. changes],
            options.Tag,
            sourceToken,
            before,
            after,
            options.UndoBehavior,
            options.UndoDescription);
        changeSet.SelectionAfter = afterSelection;
        changeSet.SelectionsAfter = afterSelections;
        changeSet.SelectionOwnerToken = editingView?.EditSourceToken;
        changeSet.NativeCorrections = nativeCorrections;
        RaiseChanged(changeSet, before, undoStateChanged: true);
        return changeSet;
    }

    internal RichTextChangeSet RestoreSnapshotFromNativeUndo(
        RichTextDocumentSnapshot snapshot,
        RichTextChangeOrigin origin,
        object? sourceToken = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (origin is not (RichTextChangeOrigin.Undo or RichTextChangeOrigin.Redo))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        VerifyNoActiveEdit();
        _undo.Clear();
        _redo.Clear();
        if (_snapshot.ContentEquals(snapshot))
        {
            RaiseUndoStateChanged();
            return new RichTextChangeSet(
                Version,
                Version,
                origin,
                [],
                tag: null,
                beforeSnapshot: _snapshot,
                afterSnapshot: _snapshot,
                undoBehavior: RichTextUndoBehavior.ClearHistory);
        }

        _stateId = checked(++_nextStateId);
        return TransitionTo(
            snapshot,
            origin,
            RichTextUndoBehavior.ClearHistory,
            sourceToken);
    }

    private RichTextChangeSet TransitionTo(
        RichTextDocumentSnapshot snapshot,
        RichTextChangeOrigin origin,
        RichTextUndoBehavior undoBehavior = RichTextUndoBehavior.CreateUnit,
        object? sourceToken = null,
        RichTextSelectionState? selection = null,
        IReadOnlyList<RichTextChange>? positionChanges = null,
        RichEditor? selectionOwner = null,
        TextSelectionSet? selections = null)
    {
        ResetNativeEditCoalescing();
        var before = _snapshot;
        var versionBefore = Version;
        _snapshot = snapshot.WithVersion(checked(versionBefore + 1), _identity);
        InvalidateRtfCache();
        var delta = CreateDelta(before, _snapshot);
        var changes = positionChanges is null ? delta :
            (RichTextChange[])[.. positionChanges, .. delta.Where(static change => change.Kind is not (RichTextChangeKind.Text or RichTextChangeKind.Reset))];

        var changeSet = new RichTextChangeSet(
            versionBefore,
            Version,
            origin,
            [.. changes],
            tag: null,
            sourceToken: sourceToken,
            beforeSnapshot: before,
            afterSnapshot: _snapshot,
            undoBehavior: undoBehavior);
        changeSet.SelectionAfter = selection;
        changeSet.SelectionsAfter = selections;
        changeSet.SelectionOwnerToken = selectionOwner?.EditSourceToken;
        RaiseChanged(changeSet, before, undoStateChanged: true);
        return changeSet;
    }

    private bool CanMergeNativeEdit(
        IReadOnlyList<RichTextChange> changes,
        object sourceToken)
    {
        var current = GetMergeableTextChange(changes);
        var previous = _lastNativeTextChange;
        if (current is null || previous is null ||
            !ReferenceEquals(sourceToken, _lastNativeSourceToken) ||
            unchecked(Environment.TickCount64 - _lastNativeEditTick) > 1_000)
        {
            return false;
        }

        var bothInsertions = previous.RemovedLength == 0 && current.RemovedLength == 0 &&
            previous.InsertedText.Length != 0 && current.InsertedText.Length != 0 &&
            current.OldRange.Start == previous.NewRange.End;
        var bothBackspaces = previous.InsertedText.Length == 0 &&
            current.InsertedText.Length == 0 &&
            previous.RemovedLength != 0 && current.RemovedLength != 0 &&
            current.OldRange.End == previous.OldRange.Start;
        var bothForwardDeletes = previous.InsertedText.Length == 0 &&
            current.InsertedText.Length == 0 &&
            previous.RemovedLength != 0 && current.RemovedLength != 0 &&
            current.OldRange.Start == previous.OldRange.Start;
        return bothInsertions || bothBackspaces || bothForwardDeletes;
    }

    private static RichTextTextChange? GetMergeableTextChange(
        IReadOnlyList<RichTextChange> changes)
    {
        if (changes.Any(static change => change.Kind is not (
                RichTextChangeKind.Text or
                RichTextChangeKind.CharacterFormat or
                RichTextChangeKind.ParagraphFormat)))
        {
            return null;
        }

        var textChanges = changes.OfType<RichTextTextChange>().ToArray();
        return textChanges.Length == 1 ? textChanges[0] : null;
    }

    private void ResetNativeEditCoalescing()
    {
        _lastNativeEditTick = 0;
        _lastNativeSourceToken = null;
        _lastNativeTextChange = null;
    }

    internal void BreakTypingUndoGroup()
    {
        VerifyAccess();
        ResetNativeEditCoalescing();
    }

    private void RaiseChanged(
        RichTextChangeSet changeSet,
        RichTextDocumentSnapshot previousSnapshot,
        bool undoStateChanged = false)
    {
        _notificationInProgress = true;
        var editors = GetAttachedEditors();
        List<Exception>? failures = null;
        void Notify(Action action)
        {
            try { action(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try
        {
            // Complete internal synchronization before application callbacks, regardless
            // of when those callbacks subscribed to the document.
            Tracking.MapThrough(changeSet);
            foreach (var editor in editors)
                Notify(() => editor.SynchronizeDocumentChange(changeSet));
            Notify(() => Changed?.Invoke(this, new RichTextDocumentChangedEventArgs(changeSet)));
            foreach (var editor in editors)
                Notify(() => editor.PublishContentChange(changeSet));
            Notify(() => OnPropertyChanged(nameof(Version)));
            Notify(() => OnPropertyChanged(nameof(Revision)));
            Notify(() => OnPropertyChanged(nameof(CurrentSnapshot)));
            Notify(() => OnPropertyChanged(nameof(RtfText)));
            Notify(() => OnPropertyChanged(nameof(IsModified)));
            if (!string.Equals(previousSnapshot.Text, Text, StringComparison.Ordinal))
            {
                Notify(() => OnPropertyChanged(nameof(Length)));
                Notify(() => OnPropertyChanged(nameof(Text)));
            }
            if (previousSnapshot.DefaultCharacterFormat != DefaultCharacterFormat)
                Notify(() => OnPropertyChanged(nameof(DefaultCharacterFormat)));
            if (previousSnapshot.DefaultParagraphFormat != DefaultParagraphFormat)
                Notify(() => OnPropertyChanged(nameof(DefaultParagraphFormat)));
            if (undoStateChanged)
                Notify(PublishUndoState);

            foreach (var editor in editors)
                Notify(editor.PublishSelectionChange);
        }
        finally
        {
            foreach (var editor in editors)
                editor.EndDocumentChange();
            _notificationInProgress = false;
        }
        if (failures is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 })
            throw new AggregateException("Document observers failed after the shared edit committed.", failures);
    }

    private void RaiseUndoStateChanged()
    {
        _notificationInProgress = true;
        try
        {
            foreach (var editor in GetAttachedEditors())
                editor.RefreshUndoState();
            OnPropertyChanged(nameof(IsUndoGroupOpen));
            OnPropertyChanged(nameof(IsModified));
            PublishUndoState();
        }
        finally
        {
            _notificationInProgress = false;
        }
    }

    private void PublishUndoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
        UndoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateRtfCache()
    {
        _cachedRtf = null;
        _cachedRtfVersion = -1;
    }

    private void VerifyNoActiveEdit()
    {
        VerifyAccess();
        if (_editInProgress || _notificationInProgress)
        {
            throw new InvalidOperationException(
                "A rich-text document cannot be mutated recursively from an edit callback or change notification.");
        }
    }

    private void VerifyNoOpenUndoGroup()
    {
        if (IsUndoGroupOpen)
            throw new InvalidOperationException("Undo history cannot be cleared while an undo group is open.");
    }

    private void EndUndoGroup(UndoGroup group)
    {
        VerifyNoActiveEdit();
        if (!_undoGroups.TryPeek(out var current) || !ReferenceEquals(current, group))
            throw new InvalidOperationException("Undo groups must be disposed in reverse order.");

        _undoGroups.Pop();
        group.MarkClosed();
        if (!IsUndoGroupOpen)
        {
            if (_undoGroupBefore is { } before && !before.ContentEquals(_snapshot))
            {
                _undo.Push(new UndoEntry(before, _snapshot, _undoGroupDescription, _undoGroupStateId, _stateId, _undoGroupSelection, _undoGroupAfterSelection,
                    [.. _undoGroupForward], [.. _undoGroupReverseInverse])
                { BeforeSelections = _undoGroupSelections, AfterSelections = _undoGroupAfterSelections });
                _redo.Clear();
            }
            if (_undoGroupBefore is { } unchanged && unchanged.ContentEquals(_snapshot))
                _stateId = _undoGroupStateId;

            _undoGroupBefore = null;
            _undoGroupDescription = null;
            _undoGroupForward.Clear();
            _undoGroupReverseInverse.Clear();
            ResetNativeEditCoalescing();
        }

        RaiseUndoStateChanged();
    }

    private sealed partial class UndoGroup(RichTextDocument document) : IDisposable
    {
        private RichTextDocument? _document = document;

        public void Dispose()
        {
            if (_document is not { } owner)
                return;

            owner.EndUndoGroup(this);
        }

        internal void MarkClosed() => _document = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record UndoEntry(
        RichTextDocumentSnapshot Before,
        RichTextDocumentSnapshot After,
        string? Description,
        long BeforeStateId,
        long AfterStateId,
        RichTextSelectionState? BeforeSelection,
        RichTextSelectionState? AfterSelection,
        List<RichTextChange> Forward,
        List<RichTextChange> ReverseInverse)
    {
        internal TextSelectionSet? BeforeSelections { get; init; }
        internal TextSelectionSet? AfterSelections { get; init; }
    }

    private static RichTextChange[] InvertPositionChanges(string text, IReadOnlyList<RichTextChange> changes)
    {
        if (changes.Count == 0)
            return [];
        if (changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
            return [.. changes.Reverse().Select(static change => new RichTextRangeChange(RichTextChangeKind.Reset, change.NewRange, change.OldRange))];

        var descending = true;
        for (var index = 1; index < changes.Count; index++)
            descending &= changes[index].OldRange.Start < changes[index - 1].OldRange.Start &&
                changes[index].OldRange.End <= changes[index - 1].OldRange.Start;
        if (descending)
            return [.. changes.Cast<RichTextTextChange>().Reverse().Select(change => new RichTextTextChange(change.NewRange,
                text.Substring(change.OldRange.Start, change.OldRange.Length)))];

        var source = new System.Text.StringBuilder(text);
        var inverse = new RichTextChange[changes.Count];
        for (var i = 0; i < changes.Count; i++)
        {
            var change = (RichTextTextChange)changes[i];
            inverse[changes.Count - i - 1] = new RichTextTextChange(change.NewRange, source.ToString(change.OldRange.Start, change.OldRange.Length));
            source.Remove(change.OldRange.Start, change.OldRange.Length).Insert(change.OldRange.Start, change.InsertedText);
        }

        return inverse;
    }
}
