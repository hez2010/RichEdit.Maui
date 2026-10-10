namespace TextEdit.Core;

/// <summary>Whether deleting tracked source preserves its boundary or permanently invalidates the handle.</summary>
public enum TrackingDeletionBehavior
{
    /// <summary>Invalidate the handle when covered source is deleted.</summary>
    Invalidate,
    /// <summary>Keep the handle at the mapped replacement boundary.</summary>
    Preserve,
}

/// <summary>Creates disposable source anchors in a balanced interval index with lazy suffix shifts.</summary>
public sealed class TextTracking
{
    private readonly TextDocument _document;
    private Node? _root;
    private long _nextId;
    internal TextTracking(TextDocument document) => _document = document;
    internal long LastChangeVisitCount { get; private set; }
    internal void VerifyAccess() => _document.VerifyAccess();

    /// <summary>Tracks one UTF-16 boundary. Deleted interior positions invalidate by default.</summary>
    public TextTrackedPosition TrackPosition(int position, PositionAffinity affinity = PositionAffinity.AfterInsertion,
        TrackingDeletionBehavior deletion = TrackingDeletionBehavior.Invalidate)
    {
        Validate(new(position, 0), affinity, affinity, deletion);
        var node = Add(new(position, 0), affinity, affinity, deletion, point: true);
        return new(this, node);
    }

    /// <summary>Tracks a range, excluding insertions at both boundaries by default.</summary>
    public TextTrackedRange TrackRange(TextSpan range, PositionAffinity startAffinity = PositionAffinity.AfterInsertion,
        PositionAffinity endAffinity = PositionAffinity.BeforeInsertion, TrackingDeletionBehavior deletion = TrackingDeletionBehavior.Invalidate)
    {
        Validate(range, startAffinity, endAffinity, deletion);
        return new(this, Add(range, startAffinity, endAffinity, deletion, point: false));
    }

    private void Validate(TextSpan range, PositionAffinity start, PositionAffinity end, TrackingDeletionBehavior deletion)
    {
        VerifyAccess();
        range.Validate(_document.Length);
        if (!Enum.IsDefined(start) || !Enum.IsDefined(end) || !Enum.IsDefined(deletion))
            throw new ArgumentOutOfRangeException(nameof(start));
    }
    private Node Add(TextSpan range, PositionAffinity start, PositionAffinity end, TrackingDeletionBehavior deletion, bool point)
    {
        var node = new Node(++_nextId, range, start, end, deletion, point);
        SetRoot(Insert(_root, node));
        return node;
    }
    internal void Remove(Node node)
    {
        VerifyAccess();
        if (!node.Alive)
            return;
        var position = node.CurrentRange.Start;
        SetRoot(Delete(_root, position, node.Id));
        node.Alive = false;
    }

    internal void MapThrough(PositionMap map)
    {
        LastChangeVisitCount = 0;
        var length = map.OldLength;
        foreach (var change in map.Changes)
        {
            var single = new PositionMap(length, [change]);
            var (prefix, suffix) = SplitByStart(_root, change.OldSpan.End);
            SetRoot(prefix);
            if (suffix is not null)
                suffix.Parent = null;
            var affected = new List<(Node Node, TextSpan Range)>();
            Collect(_root, change.OldSpan.Start, affected);
            foreach (var item in affected)
                SetRoot(Delete(_root, item.Range.Start, item.Node.Id));
            foreach (var item in affected)
            {
                var node = item.Node;
                TextSpan? mapped = node.Point
                    ? single.Map(item.Range.Start, node.StartAffinity, node.Deletion == TrackingDeletionBehavior.Invalidate) is { } point
                        ? new(point, 0) : null
                    : single.Map(item.Range, node.StartAffinity, node.EndAffinity, node.Deletion == TrackingDeletionBehavior.Invalidate);
                if (mapped is not { } range)
                {
                    node.Alive = false;
                    continue;
                }
                node.Start = range.Start;
                node.End = range.End;
                node.MaxEnd = range.End;
                node.Lazy = 0;
                SetRoot(Insert(_root, node));
            }
            Shift(suffix, change.NewLength - change.OldSpan.Length);
            SetRoot(Merge(_root, suffix));
            length = single.NewLength;
        }
    }

    private void SetRoot(Node? node)
    {
        _root = node;
        if (node is not null)
            node.Parent = null;
    }
    private static void Shift(Node? node, int delta)
    {
        if (node is null || delta == 0)
            return;
        node.Start += delta;
        node.End += delta;
        node.MaxEnd += delta;
        node.Lazy += delta;
    }
    private static void Push(Node node)
    {
        Shift(node.Left, node.Lazy);
        Shift(node.Right, node.Lazy);
        node.Lazy = 0;
    }
    private static Node Update(Node node)
    {
        node.MaxEnd = Math.Max(node.End, Math.Max(node.Left?.MaxEnd ?? 0, node.Right?.MaxEnd ?? 0));
        if (node.Left is not null) node.Left.Parent = node;
        if (node.Right is not null) node.Right.Parent = node;
        return node;
    }
    private void Collect(Node? node, int start, List<(Node Node, TextSpan Range)> result)
    {
        LastChangeVisitCount++;
        if (node is null || node.MaxEnd < start)
            return;
        Push(node);
        Collect(node.Left, start, result);
        if (node.End >= start)
            result.Add((node, new(node.Start, node.End - node.Start)));
        Collect(node.Right, start, result);
    }
    private (Node? Left, Node? Right) SplitByStart(Node? node, int boundary)
    {
        LastChangeVisitCount++;
        if (node is null)
            return (null, null);
        Push(node);
        if (node.Start <= boundary)
        {
            var split = SplitByStart(node.Right, boundary);
            node.Right = split.Left;
            return (Update(node), split.Right);
        }
        else
        {
            var split = SplitByStart(node.Left, boundary);
            node.Left = split.Right;
            return (split.Left, Update(node));
        }
    }
    private Node? Merge(Node? left, Node? right)
    {
        LastChangeVisitCount++;
        if (left is null) return right;
        if (right is null) return left;
        if (left.Priority < right.Priority)
        {
            Push(left);
            left.Right = Merge(left.Right, right);
            return Update(left);
        }
        Push(right);
        right.Left = Merge(left, right.Left);
        return Update(right);
    }
    private Node Insert(Node? root, Node node)
    {
        LastChangeVisitCount++;
        if (root is null)
            return node;
        Push(root);
        if (Compare(node.Start, node.Id, root) < 0)
        {
            root.Left = Insert(root.Left, node);
            if (root.Left.Priority < root.Priority)
            {
                var left = root.Left;
                Push(left);
                root.Left = left.Right;
                left.Right = Update(root);
                return Update(left);
            }
        }
        else
        {
            root.Right = Insert(root.Right, node);
            if (root.Right.Priority < root.Priority)
            {
                var right = root.Right;
                Push(right);
                root.Right = right.Left;
                right.Left = Update(root);
                return Update(right);
            }
        }
        return Update(root);
    }
    private Node? Delete(Node? root, int start, long id)
    {
        LastChangeVisitCount++;
        if (root is null)
            return null;
        Push(root);
        var compare = Compare(start, id, root);
        if (compare == 0)
        {
            var result = Merge(root.Left, root.Right);
            root.Left = root.Right = root.Parent = null;
            return result;
        }
        if (compare < 0) root.Left = Delete(root.Left, start, id);
        else root.Right = Delete(root.Right, start, id);
        return Update(root);
    }
    private static int Compare(int start, long id, Node node) => start == node.Start ? id.CompareTo(node.Id) : start.CompareTo(node.Start);

    internal sealed class Node
    {
        internal Node(long id, TextSpan range, PositionAffinity start, PositionAffinity end, TrackingDeletionBehavior deletion, bool point)
        {
            Id = id;
            Start = range.Start;
            End = MaxEnd = range.End;
            StartAffinity = start;
            EndAffinity = end;
            Deletion = deletion;
            Point = point;
            unchecked
            {
                var hash = (ulong)id + 0x9e3779b97f4a7c15UL;
                hash = (hash ^ (hash >> 30)) * 0xbf58476d1ce4e5b9UL;
                hash = (hash ^ (hash >> 27)) * 0x94d049bb133111ebUL;
                Priority = hash ^ (hash >> 31);
            }
        }
        internal readonly long Id;
        internal readonly ulong Priority;
        internal readonly PositionAffinity StartAffinity;
        internal readonly PositionAffinity EndAffinity;
        internal readonly TrackingDeletionBehavior Deletion;
        internal readonly bool Point;
        internal bool Alive = true;
        internal int Start, End, MaxEnd, Lazy;
        internal Node? Left, Right, Parent;
        internal TextSpan CurrentRange
        {
            get
            {
                var shift = 0;
                for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
                    shift += ancestor.Lazy;
                return new(Start + shift, End - Start);
            }
        }
    }
}

/// <summary>A disposable live source anchor. Invalidated handles do not revive on undo.</summary>
public abstract class TextTrackingHandle : IDisposable
{
    private TextTracking? _owner;
    private readonly TextTracking.Node _node;
    private protected TextTrackingHandle(TextTracking owner, TextTracking.Node node) { _owner = owner; _node = node; }
    private protected TextSpan? CurrentRange
    {
        get
        {
            if (_owner is null || !_node.Alive) return null;
            _owner.VerifyAccess();
            return _node.CurrentRange;
        }
    }
    /// <summary>Gets whether this handle is still attached and valid.</summary>
    public bool IsValid => CurrentRange.HasValue;
    /// <summary>Removes this handle from its document's interval index.</summary>
    public void Dispose() { _owner?.Remove(_node); _owner = null; }
}

/// <summary>A tracked exact UTF-16 position.</summary>
public sealed class TextTrackedPosition : TextTrackingHandle
{
    internal TextTrackedPosition(TextTracking owner, TextTracking.Node node) : base(owner, node) { }
    /// <summary>Gets the current position, or null after deletion invalidation or disposal.</summary>
    public int? Position => CurrentRange?.Start;
}

/// <summary>A tracked exact UTF-16 range.</summary>
public sealed class TextTrackedRange : TextTrackingHandle
{
    internal TextTrackedRange(TextTracking owner, TextTracking.Node node) : base(owner, node) { }
    /// <summary>Gets the current range, or null after deletion invalidation or disposal.</summary>
    public TextSpan? Range => CurrentRange;
}
