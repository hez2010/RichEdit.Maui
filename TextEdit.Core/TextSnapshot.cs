using System.Text;

namespace TextEdit.Core;

/// <summary>A zero-based logical line and UTF-16 column. Columns can address the interior of a CRLF terminator.</summary>
public readonly record struct TextPosition(int Line, int Column);

/// <summary>A source line excluding its terminator, together with the exact terminator length.</summary>
public readonly record struct TextLine(TextSpan Span, int LineBreakLength);

/// <summary>An immutable text snapshot backed by a persistent balanced tree of bounded chunks.</summary>
/// <remarks>CRLF, CR, LF, and all other UTF-16 data are preserved exactly. Ordinary replacement shares unchanged tree nodes.</remarks>
public sealed class TextSnapshot
{
    private const int ChunkSize = 2048;
    private readonly Node? _root;
    private WeakReference<string>? _materialized;

    private TextSnapshot(Node? root, DocumentRevision revision = default)
    {
        _root = root;
        Revision = revision;
    }

    /// <summary>Gets the owning document revision, or an invalid revision for standalone text.</summary>
    public DocumentRevision Revision { get; }

    internal TextSnapshot WithRevision(DocumentRevision revision) => new(_root, revision) { _materialized = _materialized };

    /// <summary>Gets an empty snapshot with one empty line.</summary>
    public static TextSnapshot Empty { get; } = new(null);

    /// <summary>Gets the number of UTF-16 code units.</summary>
    public int Length => _root?.Length ?? 0;

    /// <summary>Gets the logical line count, including an empty final line after a terminator.</summary>
    public int LineCount => checked((_root?.Breaks ?? 0) + 1);

    /// <summary>Reads one UTF-16 code unit.</summary>
    public char this[int position]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Length);
            var node = _root!;
            while (node is Branch branch)
            {
                if (position < branch.Left.Length)
                    node = branch.Left;
                else
                {
                    position -= branch.Left.Length;
                    node = branch.Right;
                }
            }
            return ((Leaf)node).Text[position];
        }
    }

    /// <summary>Creates a snapshot without normalizing text or line endings.</summary>
    public static TextSnapshot FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return Empty;
        var leaves = new List<Node>((text.Length - 1) / ChunkSize + 1);
        for (var start = 0; start < text.Length;)
        {
            var length = Math.Min(ChunkSize, text.Length - start);
            leaves.Add(new Leaf(text.Substring(start, length)));
            start += length;
        }
        return new(Build(leaves, 0, leaves.Count));
    }

    /// <summary>Returns a new snapshot with the bounded range replaced. An identical replacement returns this snapshot.</summary>
    public TextSnapshot Replace(TextSpan range, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        range.Validate(Length);
        _ = checked(Length - range.Length + text.Length);
        if (range.Length == text.Length && EqualsText(range, text))
            return this;
        var (left, tail) = Split(_root, range.Start);
        var (_, right) = Split(tail, range.Length);
        return new(Join(Join(left, FromText(text)._root), right));
    }

    /// <summary>Shares the tree covering a bounded range.</summary>
    public TextSnapshot Slice(TextSpan range)
    {
        range.Validate(Length);
        if (range.Start == 0 && range.Length == Length)
            return this;
        var (_, tail) = Split(_root, range.Start);
        var (middle, _) = Split(tail, range.Length);
        return middle is null ? Empty : new(middle);
    }

    /// <summary>Materializes only the requested range.</summary>
    public string GetText(TextSpan range)
    {
        range.Validate(Length);
        return range.Start == 0 && range.Length == Length ? ToString() : Materialize(range);
    }

    /// <summary>Enumerates immutable memory segments without materializing the requested text.</summary>
    public IEnumerable<ReadOnlyMemory<char>> GetChunks(TextSpan range)
    {
        range.Validate(Length);
        return Chunks(_root, range.Start, range.Length);
    }

    /// <summary>Gets a zero-based line and its original line terminator length.</summary>
    public TextLine GetLine(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, LineCount);
        var start = line == 0 ? 0 : BreakEnd(_root!, line);
        var end = line == LineCount - 1 ? Length : BreakEnd(_root!, line + 1);
        var terminator = 0;
        if (line < LineCount - 1)
            terminator = end >= 2 && this[end - 2] == '\r' && this[end - 1] == '\n' ? 2 : 1;
        return new(new(start, end - start - terminator), terminator);
    }

    /// <summary>Converts a bounded UTF-16 offset to a zero-based logical position.</summary>
    public TextPosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);
        var stats = Prefix(_root, offset);
        var breaks = stats.Breaks;
        if (offset > 0 && offset < Length && stats.EndsCr && this[offset] == '\n')
            breaks--;
        var start = breaks == 0 ? 0 : BreakEnd(_root!, breaks);
        return new(breaks, offset - start);
    }

    /// <summary>Converts a logical position to an exact UTF-16 offset, including positions within CRLF.</summary>
    public int GetOffset(TextPosition position)
    {
        var line = GetLine(position.Line);
        ArgumentOutOfRangeException.ThrowIfNegative(position.Column);
        var maximum = line.Span.Length + Math.Max(0, line.LineBreakLength - 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Column, maximum);
        return line.Span.Start + position.Column;
    }

    /// <summary>Streams the snapshot to a caller-owned writer.</summary>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var chunk in GetChunks(new(0, Length)))
            writer.Write(chunk.Span);
    }

    /// <summary>Materializes the complete text. The optional weak cache does not retain full strings through history.</summary>
    public override string ToString()
    {
        if (_root is null)
            return string.Empty;
        if (_root is Leaf leaf)
            return leaf.Text;
        if (_materialized?.TryGetTarget(out var cached) == true)
            return cached;
        var text = Materialize(new(0, Length));
        _materialized = new(text);
        return text;
    }

    private bool EqualsText(TextSpan range, string text)
    {
        var offset = 0;
        foreach (var chunk in GetChunks(range))
        {
            if (!chunk.Span.SequenceEqual(text.AsSpan(offset, chunk.Length)))
                return false;
            offset += chunk.Length;
        }
        return true;
    }

    private string Materialize(TextSpan range)
    {
        if (range.IsEmpty)
            return string.Empty;
        var builder = new StringBuilder(range.Length);
        foreach (var chunk in GetChunks(range))
            builder.Append(chunk.Span);
        return builder.ToString();
    }

    private static IEnumerable<ReadOnlyMemory<char>> Chunks(Node? node, int start, int length)
    {
        if (node is null || length == 0)
            yield break;
        if (node is Leaf leaf)
        {
            yield return leaf.Text.AsMemory(start, length);
            yield break;
        }
        var branch = (Branch)node;
        if (start < branch.Left.Length)
        {
            var count = Math.Min(length, branch.Left.Length - start);
            foreach (var chunk in Chunks(branch.Left, start, count))
                yield return chunk;
            length -= count;
            start = 0;
        }
        else
            start -= branch.Left.Length;
        foreach (var chunk in Chunks(branch.Right, start, length))
            yield return chunk;
    }

    private static Node Build(IReadOnlyList<Node> nodes, int start, int count)
    {
        if (count == 1)
            return nodes[start];
        var left = count / 2;
        return new Branch(Build(nodes, start, left), Build(nodes, start + left, count - left));
    }

    private static (Node? Left, Node? Right) Split(Node? node, int position)
    {
        if (node is null || position == 0)
            return (null, node);
        if (position == node.Length)
            return (node, null);
        if (node is Leaf leaf)
            return (new Leaf(leaf.Text[..position]), new Leaf(leaf.Text[position..]));
        var branch = (Branch)node;
        if (position < branch.Left.Length)
        {
            var (first, second) = Split(branch.Left, position);
            return (first, Join(second, branch.Right));
        }
        else
        {
            var (first, second) = Split(branch.Right, position - branch.Left.Length);
            return (Join(branch.Left, first), second);
        }
    }

    private static Node? Join(Node? left, Node? right)
    {
        if (left is null)
            return right;
        if (right is null)
            return left;
        if (left is Leaf a && right is Leaf b && a.Length + b.Length <= ChunkSize)
            return new Leaf(a.Text + b.Text);
        if (left.Height > right.Height + 1)
        {
            var branch = (Branch)left;
            return Balance(branch.Left, Join(branch.Right, right)!);
        }
        if (right.Height > left.Height + 1)
        {
            var branch = (Branch)right;
            return Balance(Join(left, branch.Left)!, branch.Right);
        }
        return new Branch(left, right);
    }

    private static Node Balance(Node left, Node right)
    {
        if (left.Height > right.Height + 1)
        {
            var branch = (Branch)left;
            if (branch.Left.Height >= branch.Right.Height)
                return new Branch(branch.Left, new Branch(branch.Right, right));
            var middle = (Branch)branch.Right;
            return new Branch(new Branch(branch.Left, middle.Left), new Branch(middle.Right, right));
        }
        if (right.Height > left.Height + 1)
        {
            var branch = (Branch)right;
            if (branch.Right.Height >= branch.Left.Height)
                return new Branch(new Branch(left, branch.Left), branch.Right);
            var middle = (Branch)branch.Left;
            return new Branch(new Branch(left, middle.Left), new Branch(middle.Right, branch.Right));
        }
        return new Branch(left, right);
    }

    private static int BreakEnd(Node node, int ordinal)
    {
        if (node is Leaf leaf)
        {
            for (var i = 0; i < leaf.Text.Length; i++)
                if (leaf.Text[i] is '\r' or '\n')
                {
                    if (leaf.Text[i] == '\r' && i + 1 < leaf.Text.Length && leaf.Text[i + 1] == '\n')
                        i++;
                    if (--ordinal == 0)
                        return i + 1;
                }
            throw new InvalidOperationException("The line index is inconsistent with the text.");
        }
        var branch = (Branch)node;
        var pair = branch.Left.EndsCr && branch.Right.StartsLf;
        if (ordinal <= branch.Left.Breaks)
        {
            var end = BreakEnd(branch.Left, ordinal);
            return end == branch.Left.Length && pair ? end + 1 : end;
        }
        return branch.Left.Length + BreakEnd(branch.Right, ordinal - branch.Left.Breaks + (pair ? 1 : 0));
    }

    private readonly record struct Stats(int Length, int Breaks, bool StartsLf, bool EndsCr)
    {
        internal static Stats Combine(Stats left, Stats right) => left.Length == 0 ? right : right.Length == 0 ? left :
            new(left.Length + right.Length, left.Breaks + right.Breaks - (left.EndsCr && right.StartsLf ? 1 : 0), left.StartsLf, right.EndsCr);
    }

    private static Stats Prefix(Node? node, int length)
    {
        if (node is null || length == 0)
            return default;
        if (node.Length == length)
            return new(node.Length, node.Breaks, node.StartsLf, node.EndsCr);
        if (node is Leaf leaf)
        {
            var text = leaf.Text.AsSpan(0, length);
            return new(length, CountBreaks(text), text[0] == '\n', text[^1] == '\r');
        }
        var branch = (Branch)node;
        return length <= branch.Left.Length ? Prefix(branch.Left, length) :
            Stats.Combine(Prefix(branch.Left, branch.Left.Length), Prefix(branch.Right, length - branch.Left.Length));
    }

    private static int CountBreaks(ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] is '\r' or '\n')
            {
                count++;
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
            }
        return count;
    }

    private abstract class Node(int length, int height, int breaks, bool startsLf, bool endsCr)
    {
        internal int Length { get; } = length;
        internal int Height { get; } = height;
        internal int Breaks { get; } = breaks;
        internal bool StartsLf { get; } = startsLf;
        internal bool EndsCr { get; } = endsCr;
    }

    private sealed class Leaf(string text) : Node(text.Length, 1, CountBreaks(text), text[0] == '\n', text[^1] == '\r')
    {
        internal string Text { get; } = text;
    }

    private sealed class Branch(Node left, Node right) : Node(checked(left.Length + right.Length), Math.Max(left.Height, right.Height) + 1,
        left.Breaks + right.Breaks - (left.EndsCr && right.StartsLf ? 1 : 0), left.StartsLf, right.EndsCr)
    {
        internal Node Left { get; } = left;
        internal Node Right { get; } = right;
    }
}
