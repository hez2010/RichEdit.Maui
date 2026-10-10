namespace TextEdit.Core;

public sealed partial class TextSnapshot
{
    // Exact comparison skips shared persistent subtrees, without flattening text
    // or trusting hashes. Returns one minimal enclosing UTF-16 replacement.
    internal (TextSpan Before, TextSpan After)? FindDifference(TextSnapshot other, out int visitedNodes)
    {
        ArgumentNullException.ThrowIfNull(other);
        visitedNodes = 0;
        if (ReferenceEquals(_root, other._root)) return null;
        var limit = Math.Min(Length, other.Length);
        var prefix = CommonEdge(other, limit, false, out var firstVisits);
        visitedNodes = firstVisits;
        if (prefix == Length && prefix == other.Length) return null;
        var suffix = CommonEdge(other, limit - prefix, true, out var lastVisits);
        visitedNodes += lastVisits;
        return (new(prefix, Length - prefix - suffix), new(prefix, other.Length - prefix - suffix));
    }

    private int CommonEdge(TextSnapshot other, int limit, bool reverse, out int visitedNodes)
    {
        visitedNodes = 0;
        if (limit == 0) return 0;
        var left = new ComparisonCursor(_root!, reverse);
        var right = new ComparisonCursor(other._root!, reverse);
        var compared = 0;
        while (compared < limit)
        {
            visitedNodes++;
            var a = left.Current;
            var b = right.Current;
            if (ReferenceEquals(a, b) && left.Offset == right.Offset)
            {
                var shared = Math.Min(limit - compared, a.Length - left.Offset);
                left.Advance(shared); right.Advance(shared); compared += shared;
                continue;
            }
            if (a is Branch && (b is Leaf || a.Length >= b.Length)) { left.Expand(); continue; }
            if (b is Branch) { right.Expand(); continue; }
            var count = Math.Min(limit - compared, Math.Min(a.Length - left.Offset, b.Length - right.Offset));
            var first = ((Leaf)a).Text.AsSpan(reverse ? a.Length - left.Offset - count : left.Offset, count);
            var second = ((Leaf)b).Text.AsSpan(reverse ? b.Length - right.Offset - count : right.Offset, count);
            var equal = 0;
            if (reverse)
                while (equal < count && first[^(equal + 1)] == second[^(equal + 1)]) equal++;
            else equal = first.CommonPrefixLength(second);
            compared += equal;
            if (equal != count) return compared;
            left.Advance(count); right.Advance(count);
        }
        return compared;
    }

    private sealed class ComparisonCursor
    {
        private readonly Stack<Node> _nodes = new();
        private readonly bool _reverse;
        internal ComparisonCursor(Node root, bool reverse) { _nodes.Push(root); _reverse = reverse; }
        internal Node Current => _nodes.Peek();
        internal int Offset { get; private set; }
        internal void Expand()
        {
            var branch = (Branch)_nodes.Pop();
            _nodes.Push(_reverse ? branch.Left : branch.Right);
            _nodes.Push(_reverse ? branch.Right : branch.Left);
        }
        internal void Advance(int count)
        {
            Offset += count;
            if (Offset == Current.Length) { _nodes.Pop(); Offset = 0; }
        }
    }
}
