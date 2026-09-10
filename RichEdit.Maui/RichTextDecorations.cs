using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>Nonpersistent character appearance. Null properties leave the underlying appearance unchanged.</summary>
public sealed record RichTextDecorationStyle
{
    // Used only by the Windows folding projection, never authored formatting.
    internal bool? Hidden { get; init; }
    /// <summary>Gets the foreground override.</summary>
    public Color? ForegroundColor { get; init; }
    /// <summary>Gets the background override.</summary>
    public Color? BackgroundColor { get; init; }
    /// <summary>Gets the underline override.</summary>
    public RichTextUnderlineStyle? Underline { get; init; }
    /// <summary>Gets the underline-color override, subject to native rendering support.</summary>
    public Color? UnderlineColor { get; init; }

    internal RichTextCharacterFormat Apply(RichTextCharacterFormat format) => format with
    {
        Hidden = Hidden ?? format.Hidden,
        ForegroundColor = ForegroundColor ?? format.ForegroundColor,
        BackgroundColor = BackgroundColor ?? format.BackgroundColor,
        Underline = Underline ?? format.Underline,
        UnderlineColor = UnderlineColor ?? format.UnderlineColor,
    };
}

/// <summary>A nonempty UTF-16 range with presentation-only appearance.</summary>
/// <param name="Range">The range to decorate.</param>
/// <param name="Style">The appearance overrides.</param>
public sealed record RichTextDecoration(RichTextRange Range, RichTextDecorationStyle Style);

/// <summary>Owns one independently replaceable set of decorations for an editor.</summary>
/// <remarks>Layers compose in creation order. Later layers override earlier layers only for properties they specify.</remarks>
public sealed partial class RichTextDecorationLayer : IDisposable
{
    private RichTextDecorations? _owner;
    internal ImmutableArray<RichTextDecoration> Items { get; set; } = [];
    internal ImmutableArray<RichTextDecoration> PendingItems { get; set; }
    internal ImmutableArray<RichTextDecoration> PaintedItems { get; set; }
    internal RichTextRevision PendingRevision { get; set; }
    internal bool RemovalPending { get; set; }
    internal RichTextDecorationLayer(RichTextDecorations owner) => _owner = owner;

    /// <summary>Replaces the layer atomically with ordered, non-overlapping ranges in the current document.</summary>
    /// <param name="revision">The source revision used to calculate these decorations.</param>
    /// <param name="decorations">The new decorations. Ranges are validated before any appearance changes.</param>
    /// <returns>False when the revision is stale, foreign, or invalid.</returns>
    public bool TrySet(RichTextRevision revision, IEnumerable<RichTextDecoration> decorations)
    {
        ObjectDisposedException.ThrowIf(_owner is null, this);
        ArgumentNullException.ThrowIfNull(decorations);
        return _owner.TrySet(this, revision, [.. decorations]);
    }

    /// <summary>Removes all decorations in this layer.</summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_owner is null, this);
        _owner.Set(this, []);
    }

    /// <summary>Removes this layer and restores the remaining presentation.</summary>
    public void Dispose()
    {
        if (_owner is not { } owner) return;
        owner.Remove(this);
        _owner = null;
    }
}

/// <summary>Manages view-owned decorations without changing document versions, events, persistence, or undo history.</summary>
/// <remarks>Use on the editor's UI thread. Ranges move with document edits; replacing the document clears all layers.</remarks>
public sealed class RichTextDecorations
{
    private readonly RichEditor _editor;
    private readonly List<RichTextDecorationLayer> _layers = [];
    private RichTextDocumentSnapshot? _projectionSource;
    private RichTextDocumentSnapshot? _projection;
    private RichTextRange? _dirtyRange;
    private bool _refreshQueued;
    private long _notifiedVersion;

    internal RichTextDecorations(RichEditor editor) => _editor = editor;

    /// <summary>Occurs when layers are changed, remapped by an edit, or cleared by document replacement.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the presentation revision, independent of the document version.</summary>
    public long Version { get; private set; }

    /// <summary>Creates an empty layer above the existing layers.</summary>
    /// <returns>A layer that can be replaced, cleared, or disposed independently.</returns>
    public RichTextDecorationLayer CreateLayer()
    {
        _editor.VerifyAccess();
        var layer = new RichTextDecorationLayer(this);
        _layers.Add(layer);
        return layer;
    }

    internal void Set(RichTextDecorationLayer layer, ImmutableArray<RichTextDecoration> items)
    {
        _editor.VerifyAccess();
        var end = 0;
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Style);
            item.Range.Validate(_editor.Document.Length, nameof(items));
            if (item.Range.IsEmpty || item.Range.Start < end)
                throw new ArgumentException("Decorations must be nonempty, ordered, and non-overlapping within a layer.", nameof(items));
            RichTextDocumentSnapshot.Validate(item.Style.Apply(RichTextCharacterFormat.Default));
            end = item.Range.End;
        }
        if ((_editor.Handler as IRichEditorHandler)?.IsComposing == true)
        {
            if (layer.PendingItems.IsDefault) layer.PaintedItems = layer.Items;
            layer.Items = items;
            layer.PendingItems = items;
            layer.PendingRevision = _editor.Document.Revision;
            Invalidate();
            NotifyChanged();
            QueueRefresh();
            return;
        }
        var before = ProjectPainted(_editor.Document.CurrentSnapshot);
        var hadPending = !layer.PendingItems.IsDefault;
        layer.PendingItems = default;
        layer.PaintedItems = default;
        if (!hadPending && _dirtyRange is null && layer.Items.AsSpan().SequenceEqual(items.AsSpan())) return;
        layer.Items = items;
        Invalidate();
        _editor.ApplyDecorationChanges(before, Project(_editor.Document.CurrentSnapshot), _dirtyRange);
        _dirtyRange = null;
        NotifyChanged();
    }

    internal bool TrySet(RichTextDecorationLayer layer, RichTextRevision revision, ImmutableArray<RichTextDecoration> items)
    {
        _editor.VerifyAccess();
        if (revision != _editor.Document.Revision) return false;
        Set(layer, items);
        return true;
    }

    internal void Remove(RichTextDecorationLayer layer)
    {
        _editor.VerifyAccess();
        Set(layer, []);
        if (!layer.PendingItems.IsDefault) layer.RemovalPending = true;
        else _layers.Remove(layer);
    }

    private RichTextDocumentSnapshot ProjectPainted(RichTextDocumentSnapshot snapshot) => Project(snapshot, painted: true);

    internal RichTextDocumentSnapshot Project(RichTextDocumentSnapshot snapshot, bool painted = false)
    {
        if (painted && _layers.All(static layer => layer.PendingItems.IsDefault)) painted = false;
        if (ReferenceEquals(snapshot, _projection)) return snapshot;
        if (!painted && ReferenceEquals(snapshot, _projectionSource)) return _projection!;
        var projected = snapshot;
        foreach (var layer in _layers)
        {
            var items = painted && !layer.PendingItems.IsDefault ? layer.PaintedItems : layer.Items;
            if (items.IsEmpty) continue;
            var runs = new List<RichTextRun>();
            var index = 0;
            foreach (var run in projected.Runs)
            {
                for (var position = run.Start; position < run.End;)
                {
                    while (index < items.Length && items[index].Range.End <= position) index++;
                    var end = run.End;
                    var format = run.Format;
                    if (index < items.Length)
                    {
                        var decoration = items[index];
                        if (decoration.Range.Start <= position)
                        {
                            end = Math.Min(end, decoration.Range.End);
                            format = decoration.Style.Apply(format);
                        }
                        else end = Math.Min(end, decoration.Range.Start);
                    }
                    runs.Add(new RichTextRun(position, end - position, format));
                    position = end;
                }
            }
            projected = projected.With(runs: runs);
        }
        if (painted) return projected;
        _projectionSource = snapshot;
        return _projection = projected;
    }

    internal RichTextDocumentSnapshot RestoreAuthoredSnapshot(RichTextDocumentSnapshot observed, RichTextRange replacement)
    {
        var source = _editor.Document.CurrentSnapshot;
        var projected = ProjectPainted(source);
        if (ReferenceEquals(source, projected)) return observed;
        var authored = source.RemapText(observed.Text, replacement);
        projected = projected.RemapText(observed.Text, replacement);
        var runs = new List<RichTextRun>();
        var authoredIndex = 0;
        var projectedIndex = 0;
        foreach (var run in observed.Runs)
        {
            for (var position = run.Start; position < run.End;)
            {
                while (authored.Runs[authoredIndex].End <= position) authoredIndex++;
                while (projected.Runs[projectedIndex].End <= position) projectedIndex++;
                var original = authored.Runs[authoredIndex];
                var display = projected.Runs[projectedIndex];
                var end = Math.Min(run.End, Math.Min(original.End, display.End));
                runs.Add(new RichTextRun(position, end - position, Restore(run.Format, original.Format, display.Format)));
                position = end;
            }
        }
        return observed.With(runs: runs);
    }

    internal RichTextCharacterFormat RestoreTypingFormat(RichTextCharacterFormat observed)
    {
        var source = _editor.Document.CurrentSnapshot;
        var position = _editor.SelectionState.Active;
        var projected = ProjectPainted(source);
        if (ReferenceEquals(source, projected)) return observed;
        var original = source.GetCaretFormat(position);
        var display = projected.GetCaretFormat(position);
        var restored = Restore(observed, original, display);
        var typing = _editor.TypingCharacterFormat;
        return restored with
        {
            ForegroundColor = !Equals(restored.ForegroundColor, observed.ForegroundColor) ? typing.ForegroundColor : restored.ForegroundColor,
            BackgroundColor = !Equals(restored.BackgroundColor, observed.BackgroundColor) ? typing.BackgroundColor : restored.BackgroundColor,
            Underline = restored.Underline != observed.Underline ? typing.Underline : restored.Underline,
            UnderlineColor = !Equals(restored.UnderlineColor, observed.UnderlineColor) ? typing.UnderlineColor : restored.UnderlineColor,
            Hidden = restored.Hidden != observed.Hidden ? typing.Hidden : restored.Hidden,
        };
    }

    private static RichTextCharacterFormat Restore(RichTextCharacterFormat observed, RichTextCharacterFormat authored, RichTextCharacterFormat display) => observed with
    {
        Hidden = authored.Hidden != display.Hidden && observed.Hidden == display.Hidden ? authored.Hidden : observed.Hidden,
        ForegroundColor = !Equals(authored.ForegroundColor, display.ForegroundColor) && SameColor(observed.ForegroundColor, display.ForegroundColor) ? authored.ForegroundColor : observed.ForegroundColor,
        BackgroundColor = !Equals(authored.BackgroundColor, display.BackgroundColor) && SameColor(observed.BackgroundColor, display.BackgroundColor) ? authored.BackgroundColor : observed.BackgroundColor,
        Underline = authored.Underline != display.Underline && observed.Underline == display.Underline ? authored.Underline : observed.Underline,
        UnderlineColor = !Equals(authored.UnderlineColor, display.UnderlineColor) && SameColor(observed.UnderlineColor, display.UnderlineColor) ? authored.UnderlineColor : observed.UnderlineColor,
    };

    private static bool SameColor(Color? first, Color? second) => Equals(first, second) || first is not null && second is not null &&
        Math.Abs(first.Red - second.Red) <= 1f / 255 && Math.Abs(first.Green - second.Green) <= 1f / 255 &&
        Math.Abs(first.Blue - second.Blue) <= 1f / 255 && Math.Abs(first.Alpha - second.Alpha) <= 1f / 255;

    internal void MapThrough(RichTextChangeSet changes)
    {
        if (!changes.IsTextChanged || _layers.All(static layer => layer.Items.IsEmpty && layer.PendingItems.IsDefaultOrEmpty)) return;
        ImmutableArray<RichTextDecoration> Map(ImmutableArray<RichTextDecoration> items) =>
            [.. items.Select(item => item with
            {
                Range = RichTextSelectionState.FromRange(item.Range).Map(changes.Changes, _editor.Document.Length).Range,
            }).Where(static item => !item.Range.IsEmpty)];
        foreach (var layer in _layers)
        {
            if (!layer.PendingItems.IsDefault)
            {
                layer.PaintedItems = Map(layer.PaintedItems);
                if (layer.PendingRevision != _editor.Document.Revision)
                {
                    layer.Items = layer.RemovalPending ? [] : layer.PaintedItems;
                    layer.PendingItems = default;
                    layer.PaintedItems = default;
                }
            }
            else layer.Items = Map(layer.Items);
        }
        if (changes.SourceToken is not null && ReferenceEquals(changes.SourceToken, (_editor.Handler as IRichEditorHandler)?.SourceToken))
        {
            var affected = changes.GetAffectedRange(_editor.Document.Length);
            if (_dirtyRange is { } previous)
            {
                previous = RichTextSelectionState.FromRange(previous).Map(changes.Changes, _editor.Document.Length).Range;
                var start = Math.Min(previous.Start, affected.Start);
                affected = new RichTextRange(start, Math.Max(previous.End, affected.End) - start);
            }
            _dirtyRange = affected;
        }
        Invalidate();
    }

    internal void Reset()
    {
        foreach (var layer in _layers) { layer.Items = []; layer.PendingItems = default; layer.PaintedItems = default; }
        _dirtyRange = null;
        Invalidate();
    }

    internal void NotifyChanged()
    {
        if (_dirtyRange is not null) QueueRefresh();
        if (_notifiedVersion == Version) return;
        _notifiedVersion = Version;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        if (!_editor.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(40), () =>
        {
            _refreshQueued = false;
            if ((_editor.Handler as IRichEditorHandler)?.IsComposing == true) { QueueRefresh(); return; }
            var before = ProjectPainted(_editor.Document.CurrentSnapshot);
            var pending = false;
            foreach (var layer in _layers.ToArray())
            {
                if (layer.RemovalPending) { _layers.Remove(layer); pending = true; }
                else if (!layer.PendingItems.IsDefault)
                {
                    if (layer.PendingRevision != _editor.Document.Revision) layer.Items = layer.PaintedItems;
                    layer.PendingItems = default;
                    layer.PaintedItems = default;
                    pending = true;
                }
            }
            if (pending || _dirtyRange is not null)
            {
                if (pending) Invalidate();
                var snapshot = Project(_editor.Document.CurrentSnapshot);
                _editor.ApplyDecorationChanges(before, snapshot, _dirtyRange);
                _dirtyRange = null;
                NotifyChanged();
            }
        })) _refreshQueued = false;
    }

    internal void Invalidate()
    {
        Version++;
        _projectionSource = null;
        _projection = null;
    }
}
