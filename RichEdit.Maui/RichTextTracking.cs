namespace RichEdit.Maui;

/// <summary>Chooses which side of text inserted at a boundary retains the anchor.</summary>
public enum RichTextTrackingAffinity
{
    /// <summary>The anchor precedes inserted text.</summary>
    BeforeInsertion,
    /// <summary>The anchor follows inserted text.</summary>
    AfterInsertion,
}

/// <summary>Controls tracking when source at an anchor is replaced or removed.</summary>
public enum RichTextTrackingDeletionBehavior
{
    /// <summary>Invalidate the handle when its source is removed.</summary>
    Invalidate,
    /// <summary>Preserve the handle at the replacement boundary according to its affinity.</summary>
    Preserve,
}

/// <summary>Creates document-owned UTF-16 anchors. Access is serialized with document mutation.</summary>
public sealed class RichTextTracking
{
    private readonly RichTextDocument _document;
    private readonly List<IRichTextTracker> _items = [];
    internal RichTextTracking(RichTextDocument document) => _document = document;
    internal void VerifyAccess() => _document.VerifyAccess();

    /// <summary>Tracks a position until disposed, reset, or invalidated by deletion.</summary>
    /// <param name="position">A bounded UTF-16 source position.</param>
    /// <param name="affinity">Behavior at an insertion boundary.</param>
    /// <param name="deletion">Behavior when the position's source is removed.</param>
    /// <returns>A disposable position handle.</returns>
    public RichTextTrackedPosition TrackPosition(int position,
        RichTextTrackingAffinity affinity = RichTextTrackingAffinity.AfterInsertion,
        RichTextTrackingDeletionBehavior deletion = RichTextTrackingDeletionBehavior.Invalidate)
    {
        VerifyAccess();
        new RichTextRange(position, 0).Validate(_document.Length, nameof(position));
        Validate(affinity, deletion);
        var item = new RichTextTrackedPosition(this, position, affinity, deletion);
        _items.Add(item);
        return item;
    }

    /// <summary>Tracks a range. Default affinities exclude insertions at both boundaries.</summary>
    /// <param name="range">A bounded UTF-16 source range, including an empty range.</param>
    /// <param name="startAffinity">Insertion behavior at the start.</param>
    /// <param name="endAffinity">Insertion behavior at the end.</param>
    /// <param name="deletion">Whether any removed content invalidates the range.</param>
    /// <returns>A disposable range handle.</returns>
    public RichTextTrackedRange TrackRange(RichTextRange range,
        RichTextTrackingAffinity startAffinity = RichTextTrackingAffinity.AfterInsertion,
        RichTextTrackingAffinity endAffinity = RichTextTrackingAffinity.BeforeInsertion,
        RichTextTrackingDeletionBehavior deletion = RichTextTrackingDeletionBehavior.Invalidate)
    {
        VerifyAccess();
        range.Validate(_document.Length, nameof(range));
        Validate(startAffinity, deletion);
        Validate(endAffinity, deletion);
        var item = new RichTextTrackedRange(this, range, startAffinity, endAffinity, deletion);
        _items.Add(item);
        return item;
    }

    internal static void Validate(RichTextTrackingAffinity affinity, RichTextTrackingDeletionBehavior deletion)
    {
        if (!Enum.IsDefined(affinity)) throw new ArgumentOutOfRangeException(nameof(affinity));
        if (!Enum.IsDefined(deletion)) throw new ArgumentOutOfRangeException(nameof(deletion));
    }

    internal RichTextTrackedRange TrackWeakRange(RichTextRange range)
    {
        VerifyAccess();
        range.Validate(_document.Length, nameof(range));
        var item = new RichTextTrackedRange(this, range, RichTextTrackingAffinity.AfterInsertion,
            RichTextTrackingAffinity.BeforeInsertion, RichTextTrackingDeletionBehavior.Preserve);
        _items.Add(new WeakTracker(item));
        return item;
    }

    private sealed class WeakTracker(IRichTextTracker item) : IRichTextTracker
    {
        private readonly WeakReference<IRichTextTracker> _target = new(item);
        internal bool RefersTo(IRichTextTracker item) => _target.TryGetTarget(out var target) && ReferenceEquals(target, item);
        public bool Map(IEnumerable<RichTextChange> changes) => _target.TryGetTarget(out var target) && target.Map(changes);
        public void Invalidate() { if (_target.TryGetTarget(out var target)) target.Invalidate(); }
    }

    internal void Remove(IRichTextTracker item)
    {
        VerifyAccess();
        _items.RemoveAll(candidate => ReferenceEquals(candidate, item) || candidate is WeakTracker weak && weak.RefersTo(item));
    }

    internal void MapThrough(RichTextChangeSet changes)
    {
        var reset = changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset);
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (reset || !item.Map(changes.Changes))
            {
                item.Invalidate();
                _items.RemoveAt(i);
            }
        }
    }
}

internal interface IRichTextTracker
{
    bool Map(IEnumerable<RichTextChange> changes);
    void Invalidate();
}

/// <summary>A document position; invalidated or disposed handles return null and are never resurrected by undo.</summary>
public sealed partial class RichTextTrackedPosition : IDisposable, IRichTextTracker
{
    private RichTextTracking? _owner;
    private int? _position;
    private readonly RichTextTrackingAffinity _affinity;
    private readonly RichTextTrackingDeletionBehavior _deletion;
    internal RichTextTrackedPosition(RichTextTracking owner, int position, RichTextTrackingAffinity affinity, RichTextTrackingDeletionBehavior deletion)
    { _owner = owner; _position = position; _affinity = affinity; _deletion = deletion; }
    /// <summary>Gets the current source position or null after invalidation or disposal.</summary>
    public int? Position { get { _owner?.VerifyAccess(); return _position; } }
    /// <summary>Releases the tracking registration. Repeated disposal has no effect.</summary>
    public void Dispose() { _owner?.Remove(this); ((IRichTextTracker)this).Invalidate(); }
    void IRichTextTracker.Invalidate() { _position = null; _owner = null; }
    bool IRichTextTracker.Map(IEnumerable<RichTextChange> changes)
    {
        foreach (var change in changes.OfType<RichTextTextChange>())
            if (_position is { } position) _position = RichTextPositionMap.Map(position, change, _affinity, _deletion);
        return _position is not null;
    }
}

/// <summary>A source range with independent boundary affinities and explicit deletion behavior.</summary>
public sealed partial class RichTextTrackedRange : IDisposable, IRichTextTracker
{
    private RichTextTracking? _owner;
    private RichTextRange? _range;
    private readonly RichTextTrackingAffinity _startAffinity;
    private readonly RichTextTrackingAffinity _endAffinity;
    private readonly RichTextTrackingDeletionBehavior _deletion;
    internal RichTextTrackedRange(RichTextTracking owner, RichTextRange range, RichTextTrackingAffinity startAffinity,
        RichTextTrackingAffinity endAffinity, RichTextTrackingDeletionBehavior deletion)
    { _owner = owner; _range = range; _startAffinity = startAffinity; _endAffinity = endAffinity; _deletion = deletion; }
    /// <summary>Gets the current range or null after invalidation or disposal.</summary>
    public RichTextRange? Range { get { _owner?.VerifyAccess(); return _range; } }
    /// <summary>Releases the tracking registration. Repeated disposal has no effect.</summary>
    public void Dispose() { _owner?.Remove(this); ((IRichTextTracker)this).Invalidate(); }
    void IRichTextTracker.Invalidate() { _range = null; _owner = null; }
    bool IRichTextTracker.Map(IEnumerable<RichTextChange> changes)
    {
        foreach (var change in changes.OfType<RichTextTextChange>())
            if (_range is { } range) _range = RichTextPositionMap.Map(range, change, _startAffinity, _endAffinity, _deletion);
        return _range is not null;
    }
}

internal static class RichTextPositionMap
{
    internal static int? Map(int position, RichTextTextChange change, RichTextTrackingAffinity affinity,
        RichTextTrackingDeletionBehavior deletion = RichTextTrackingDeletionBehavior.Preserve)
    {
        var old = change.OldRange;
        if (position < old.Start) return position;
        if (position > old.End || !old.IsEmpty && position == old.End)
            return checked(position + change.NewRange.Length - old.Length);
        if (!old.IsEmpty && deletion == RichTextTrackingDeletionBehavior.Invalidate) return null;
        return affinity == RichTextTrackingAffinity.BeforeInsertion ? old.Start : change.NewRange.End;
    }

    internal static RichTextRange? Map(RichTextRange range, RichTextTextChange change, RichTextTrackingAffinity startAffinity,
        RichTextTrackingAffinity endAffinity, RichTextTrackingDeletionBehavior deletion)
    {
        if (deletion == RichTextTrackingDeletionBehavior.Invalidate && !change.OldRange.IsEmpty &&
            (range.IsEmpty ? change.OldRange.Start <= range.Start && range.Start < change.OldRange.End :
                change.OldRange.Start < range.End && range.Start < change.OldRange.End)) return null;
        var start = Map(range.Start, change, startAffinity)!.Value;
        var end = Map(range.End, change, endAffinity)!.Value;
        return new(start, Math.Max(0, end - start));
    }
}
