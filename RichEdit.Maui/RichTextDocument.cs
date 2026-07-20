using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RichEdit.Maui;

/// <summary>
/// Represents a stable, observable rich-text document that supports atomic range edits.
/// </summary>
public sealed class RichTextDocument : INotifyPropertyChanged
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

    private readonly Stack<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();
    private RichTextDocumentSnapshot _snapshot;
    private string? _cachedRtf;
    private long _cachedRtfVersion = -1;
    private WeakReference<object>? _attachedEditor;
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
        _snapshot = snapshot.WithVersion(0);
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

    internal bool CanUndo => _undo.Count != 0;

    internal bool CanRedo => _redo.Count != 0;

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
        bool nativeUndoOwned)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sourceToken);
        VerifyNoActiveEdit();
        var changes = CreateDelta(_snapshot, snapshot);
        var undoBehavior = nativeUndoOwned
            ? RichTextUndoBehavior.ClearHistory
            : CanMergeNativeEdit(changes, sourceToken)
                ? RichTextUndoBehavior.MergeWithPrevious
                : RichTextUndoBehavior.CreateUnit;
        var result = Commit(
            snapshot,
            changes,
            RichTextChangeOrigin.User,
            new RichTextEditOptions(undoBehavior),
            sourceToken);
        if (!result.IsEmpty)
        {
            _lastNativeSourceToken = sourceToken;
            _lastNativeEditTick = Environment.TickCount64;
            _lastNativeTextChange = GetMergeableTextChange(changes);
        }

        return result;
    }

    internal void Undo()
    {
        VerifyNoActiveEdit();
        if (!_undo.TryPop(out var entry))
        {
            return;
        }

        _redo.Push(entry);
        TransitionTo(entry.Before, RichTextChangeOrigin.Undo);
    }

    internal void Redo()
    {
        VerifyNoActiveEdit();
        if (!_redo.TryPop(out var entry))
        {
            return;
        }

        _undo.Push(entry);
        TransitionTo(entry.After, RichTextChangeOrigin.Redo);
    }

    internal void ClearUndoHistory()
    {
        VerifyNoActiveEdit();
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        RaiseUndoStateChanged();
    }

    internal void AttachEditor(object editor)
    {
        VerifyCanAttachEditor(editor);
        _attachedEditor = new WeakReference<object>(editor);
    }

    internal void VerifyCanAttachEditor(object editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!CanAttachEditor(editor))
        {
            throw new InvalidOperationException(
                "A rich-text document can be attached to only one editor at a time because native undo history is editor-local.");
        }
    }

    internal bool CanAttachEditor(object editor) =>
        _attachedEditor is null ||
        !_attachedEditor.TryGetTarget(out var owner) ||
        ReferenceEquals(owner, editor);

    internal void DetachEditor(object editor)
    {
        if (_attachedEditor is not null &&
            (!_attachedEditor.TryGetTarget(out var owner) || ReferenceEquals(owner, editor)))
        {
            _attachedEditor = null;
        }
    }

    private RichTextChangeSet ExecuteEdit(
        Action<RichTextDocumentEdit> edit,
        RichTextEditOptions options,
        RichTextChangeOrigin origin,
        object? sourceToken)
    {
        VerifyNoActiveEdit();

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
        object? sourceToken)
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

        if (origin != RichTextChangeOrigin.User)
        {
            ResetNativeEditCoalescing();
        }

        var before = _snapshot;
        var versionBefore = Version;
        var after = snapshot.WithVersion(checked(versionBefore + 1));

        switch (options.UndoBehavior)
        {
            case RichTextUndoBehavior.CreateUnit:
                _undo.Push(new UndoEntry(before, after, options.UndoDescription));
                _redo.Clear();
                break;
            case RichTextUndoBehavior.MergeWithPrevious:
                if (_undo.TryPop(out var preceding))
                {
                    _undo.Push(new UndoEntry(preceding.Before, after, options.UndoDescription));
                }
                else
                {
                    _undo.Push(new UndoEntry(before, after, options.UndoDescription));
                }

                _redo.Clear();
                break;
            case RichTextUndoBehavior.DoNotRecord:
                _undo.Clear();
                _redo.Clear();
                break;
            case RichTextUndoBehavior.ClearHistory:
                _undo.Clear();
                _redo.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        _snapshot = after;
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
        RaiseChanged(changeSet, before, undoStateChanged: true);
        return changeSet;
    }

    internal RichTextChangeSet RestoreSnapshotFromNativeUndo(
        RichTextDocumentSnapshot snapshot,
        RichTextChangeOrigin origin)
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
                undoBehavior: RichTextUndoBehavior.DoNotRecord);
        }

        return TransitionTo(
            snapshot,
            origin,
            RichTextUndoBehavior.DoNotRecord);
    }

    private RichTextChangeSet TransitionTo(
        RichTextDocumentSnapshot snapshot,
        RichTextChangeOrigin origin,
        RichTextUndoBehavior undoBehavior = RichTextUndoBehavior.CreateUnit)
    {
        ResetNativeEditCoalescing();
        var before = _snapshot;
        var versionBefore = Version;
        _snapshot = snapshot.WithVersion(checked(versionBefore + 1));
        InvalidateRtfCache();
        var changes = CreateDelta(before, _snapshot);

        var changeSet = new RichTextChangeSet(
            versionBefore,
            Version,
            origin,
            [.. changes],
            tag: null,
            beforeSnapshot: before,
            afterSnapshot: _snapshot,
            undoBehavior: undoBehavior);
        RaiseChanged(changeSet, before, undoStateChanged: true);
        return changeSet;
    }

    internal static IReadOnlyList<RichTextChange> CreateDelta(
        RichTextDocumentSnapshot before,
        RichTextDocumentSnapshot after)
    {
        if (before.ContentEquals(after))
        {
            return [];
        }

        var changes = new List<RichTextChange>();
        RichTextRange? textOldRange = null;
        RichTextRange? textNewRange = null;
        if (!string.Equals(before.Text, after.Text, StringComparison.Ordinal))
        {
            var prefixLength = before.Text.AsSpan().CommonPrefixLength(after.Text);
            var suffixLength = 0;
            var maximumSuffixLength = Math.Min(before.Length, after.Length) - prefixLength;
            while (suffixLength < maximumSuffixLength &&
                   before.Text[^(suffixLength + 1)] == after.Text[^(suffixLength + 1)])
            {
                suffixLength++;
            }

            textOldRange = new RichTextRange(
                prefixLength,
                before.Length - prefixLength - suffixLength);
            textNewRange = new RichTextRange(
                prefixLength,
                after.Length - prefixLength - suffixLength);
            changes.Add(new RichTextTextChange(
                textOldRange.Value,
                after.Text.Substring(textNewRange.Value.Start, textNewRange.Value.Length)));
        }

        if (!before.Runs.AsSpan().SequenceEqual(after.Runs.AsSpan()))
        {
            var range = textNewRange ?? FindRunDifference(after, before);
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.CharacterFormat,
                textOldRange ?? range,
                range));
        }

        if (!before.Paragraphs.AsSpan().SequenceEqual(after.Paragraphs.AsSpan()) ||
            before.DefaultParagraphFormat != after.DefaultParagraphFormat)
        {
            var newRange = ExpandToParagraphs(after.Text, textNewRange ??
                FindParagraphDifference(after, before));
            var oldRange = ExpandToParagraphs(before.Text, textOldRange ?? newRange.Clamp(before.Length));
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.ParagraphFormat,
                oldRange,
                newRange));
        }

        if (before.DefaultCharacterFormat != after.DefaultCharacterFormat ||
            before.DefaultParagraphFormat != after.DefaultParagraphFormat)
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.DefaultFormat,
                new RichTextRange(0, before.Length),
                new RichTextRange(0, after.Length)));
        }

        AddSemanticDelta(changes, RichTextChangeKind.Link, before.Links, after.Links,
            static link => link.Range, before.Length, after.Length);
        AddSemanticDelta(changes, RichTextChangeKind.Field, before.Fields, after.Fields,
            static field => field.Range, before.Length, after.Length);
        AddSemanticDelta(changes, RichTextChangeKind.Image, before.Images, after.Images,
            static image => new RichTextRange(image.Position, 1), before.Length, after.Length,
            ImagesEqual);

        if (!DictionaryEqual(before.Lists, after.Lists) ||
            !ListPicturesEqual(before.ListPictures, after.ListPictures))
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.List,
                new RichTextRange(0, before.Length),
                new RichTextRange(0, after.Length)));
        }

        if (!DictionaryEqual(before.Metadata, after.Metadata))
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.Metadata,
                new RichTextRange(0, before.Length),
                new RichTextRange(0, after.Length)));
        }

        if (changes.Count == 1 && changes[0] is RichTextTextChange && textNewRange is { } inserted)
        {
            var paragraphRange = ExpandToParagraphs(after.Text, inserted);
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.CharacterFormat,
                textOldRange!.Value,
                inserted));
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.ParagraphFormat,
                ExpandToParagraphs(before.Text, textOldRange.Value),
                paragraphRange));
        }

        return changes;
    }

    private static RichTextRange FindRunDifference(
        RichTextDocumentSnapshot first,
        RichTextDocumentSnapshot second)
    {
        var start = first.Runs
            .Where(run => second.GetCharacterFormat(Math.Min(run.Start, second.Length)) != run.Format)
            .Select(static run => run.Start)
            .DefaultIfEmpty(0)
            .Min();
        var end = first.Runs
            .Where(run => second.GetCharacterFormat(Math.Min(run.Start, second.Length)) != run.Format)
            .Select(static run => run.End)
            .DefaultIfEmpty(first.Length)
            .Max();
        return new RichTextRange(start, Math.Max(end - start, 0));
    }

    private static RichTextRange FindParagraphDifference(
        RichTextDocumentSnapshot first,
        RichTextDocumentSnapshot second)
    {
        var changed = first.Paragraphs
            .Where(paragraph =>
                second.GetParagraphFormat(Math.Min(paragraph.Start, second.Length)) != paragraph.Format)
            .ToArray();
        return changed.Length == 0
            ? new RichTextRange(0, first.Length)
            : new RichTextRange(changed[0].Range.Start, changed[^1].Range.End - changed[0].Range.Start);
    }

    private static RichTextRange ExpandToParagraphs(string text, RichTextRange range)
    {
        var start = range.Start == 0 ? 0 : text.LastIndexOf('\n', range.Start - 1) + 1;
        var inspectedEnd = Math.Clamp(range.End, 0, text.Length);
        var newline = text.IndexOf('\n', inspectedEnd);
        var end = newline < 0 ? text.Length : newline + 1;
        return new RichTextRange(start, end - start);
    }

    private static void AddSemanticDelta<T>(
        List<RichTextChange> changes,
        RichTextChangeKind kind,
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        Func<T, RichTextRange> getRange,
        int beforeLength,
        int afterLength,
        Func<IReadOnlyList<T>, IReadOnlyList<T>, bool>? equals = null)
    {
        if (equals?.Invoke(before, after) ?? before.SequenceEqual(after))
        {
            return;
        }

        changes.Add(new RichTextRangeChange(
            kind,
            UnionRanges(before, getRange, beforeLength),
            UnionRanges(after, getRange, afterLength)));
    }

    private static RichTextRange UnionRanges<T>(
        IReadOnlyList<T> values,
        Func<T, RichTextRange> getRange,
        int documentLength)
    {
        if (values.Count == 0)
        {
            return new RichTextRange(0, 0);
        }

        var start = values.Min(value => getRange(value).Start);
        var end = values.Max(value => getRange(value).End);
        return new RichTextRange(start, Math.Min(end, documentLength) - start);
    }

    private static bool DictionaryEqual<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> first,
        IReadOnlyDictionary<TKey, TValue> second)
        where TKey : notnull =>
        first.Count == second.Count &&
        first.All(pair => second.TryGetValue(pair.Key, out var value) &&
            EqualityComparer<TValue>.Default.Equals(pair.Value, value));

    private static bool ImagesEqual(
        IReadOnlyList<RichTextImage> first,
        IReadOnlyList<RichTextImage> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (first[index] with { Data = [] } != second[index] with { Data = [] } ||
                !first[index].Data.AsSpan().SequenceEqual(second[index].Data.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ListPicturesEqual(
        IReadOnlyDictionary<string, RichTextListPicture> first,
        IReadOnlyDictionary<string, RichTextListPicture> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach (var pair in first)
        {
            if (!second.TryGetValue(pair.Key, out var value) ||
                pair.Value with { Data = [] } != value with { Data = [] } ||
                !pair.Value.Data.AsSpan().SequenceEqual(value.Data.AsSpan()))
            {
                return false;
            }
        }

        return true;
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

    private void RaiseChanged(
        RichTextChangeSet changeSet,
        RichTextDocumentSnapshot previousSnapshot,
        bool undoStateChanged = false)
    {
        _notificationInProgress = true;
        try
        {
            if (undoStateChanged)
            {
                UndoStateChanged?.Invoke(this, EventArgs.Empty);
            }

            OnPropertyChanged(nameof(Version));
            OnPropertyChanged(nameof(CurrentSnapshot));
            OnPropertyChanged(nameof(RtfText));
            if (!string.Equals(previousSnapshot.Text, Text, StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(Length));
                OnPropertyChanged(nameof(Text));
            }

            if (previousSnapshot.DefaultCharacterFormat != DefaultCharacterFormat)
            {
                OnPropertyChanged(nameof(DefaultCharacterFormat));
            }

            if (previousSnapshot.DefaultParagraphFormat != DefaultParagraphFormat)
            {
                OnPropertyChanged(nameof(DefaultParagraphFormat));
            }

            Changed?.Invoke(this, new RichTextDocumentChangedEventArgs(changeSet));
        }
        finally
        {
            _notificationInProgress = false;
        }
    }

    private void RaiseUndoStateChanged()
    {
        _notificationInProgress = true;
        try
        {
            UndoStateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _notificationInProgress = false;
        }
    }

    private void InvalidateRtfCache()
    {
        _cachedRtf = null;
        _cachedRtfVersion = -1;
    }

    private void VerifyNoActiveEdit()
    {
        if (_editInProgress || _notificationInProgress)
        {
            throw new InvalidOperationException(
                "A rich-text document cannot be mutated recursively from an edit callback or change notification.");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record UndoEntry(
        RichTextDocumentSnapshot Before,
        RichTextDocumentSnapshot After,
        string? Description);
}
