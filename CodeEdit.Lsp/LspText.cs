namespace CodeEdit.Lsp;

/// <summary>Converts LSP's zero-based UTF-16 coordinates and decodes semantic tokens.</summary>
public static class LspText
{
    /// <summary>Converts a UTF-16 source offset to an LSP position.</summary>
    /// <param name="text">The source snapshot.</param>
    /// <param name="offset">The UTF-16 offset.</param>
    /// <returns>A zero-based position. CRLF, CR, and LF are line terminators.</returns>
    public static LspPosition GetPosition(string text, int offset) => new LspLineMap(text).GetPosition(offset);

    /// <summary>Converts an LSP position to a UTF-16 source offset.</summary>
    /// <param name="text">The source snapshot.</param>
    /// <param name="position">A position within that snapshot.</param>
    /// <returns>The UTF-16 offset. Out-of-bounds positions are rejected.</returns>
    public static int GetOffset(string text, LspPosition position) => new LspLineMap(text).GetOffset(position);

    /// <summary>Decodes and validates a non-overlapping, single-line semantic-token stream.</summary>
    /// <param name="text">The source snapshot used by the request.</param>
    /// <param name="tokens">The full or range response.</param>
    /// <param name="legend">The server's legend.</param>
    /// <param name="cancellationToken">Cancels decoding.</param>
    /// <returns>Immutable tokens with UTF-16 offsets and the original type and modifier names.</returns>
    public static IReadOnlyList<SemanticToken> DecodeSemanticTokens(string text, SemanticTokens tokens, SemanticTokensLegend legend,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(legend);
        if (tokens.Data.Length % 5 != 0) throw new InvalidDataException("LSP semantic tokens require five integers per token.");
        var lines = new LspLineMap(text);
        var result = new List<SemanticToken>(tokens.Data.Length / 5);
        long line = 0, character = 0;
        var end = 0;
        for (var i = 0; i < tokens.Data.Length; i += 5)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deltaLine = tokens.Data[i];
            var deltaStart = tokens.Data[i + 1];
            var length = tokens.Data[i + 2];
            var type = tokens.Data[i + 3];
            var flags = tokens.Data[i + 4];
            if (deltaLine < 0 || deltaStart < 0 || length <= 0 || type < 0 || type >= legend.TokenTypes.Length || type >= 65536 || flags < 0 ||
                legend.TokenModifiers.Length < 31 && flags >> legend.TokenModifiers.Length != 0)
                throw new InvalidDataException("Invalid semantic-token data or legend index.");
            line += deltaLine;
            character = deltaLine == 0 ? character + deltaStart : deltaStart;
            if (line >= lines.Count || character > lines.GetLength((int)line)) throw new InvalidDataException("A semantic token starts outside its source line.");
            var start = lines.GetOffset(new((int)line, (int)character));
            // LSP specifies clipping tokens at line end when multilineTokenSupport is false.
            length = Math.Min(length, lines.GetLength((int)line) - (int)character);
            if (start < end) throw new InvalidDataException("The server returned overlapping semantic tokens.");
            if (length == 0) continue;
            var modifiers = new List<string>();
            for (var bit = 0; bit < Math.Min(31, legend.TokenModifiers.Length); bit++)
                if ((flags & 1 << bit) != 0) modifiers.Add(legend.TokenModifiers[bit]);
            result.Add(new(start, length, legend.TokenTypes[type], modifiers.AsReadOnly()));
            end = start + length;
        }
        return result.AsReadOnly();
    }

    internal static TextDocumentContentChangeEvent CreateChange(string previous, string current)
    {
        var start = 0;
        while (start < previous.Length && start < current.Length && previous[start] == current[start]) start++;
        if (IsSplit(previous, start) || IsSplit(current, start)) start--;
        var oldEnd = previous.Length;
        var newEnd = current.Length;
        while (oldEnd > start && newEnd > start && previous[oldEnd - 1] == current[newEnd - 1]) { oldEnd--; newEnd--; }
        if (IsSplit(previous, oldEnd) || IsSplit(current, newEnd)) { oldEnd++; newEnd++; }
        var lines = new LspLineMap(previous);
        return new(current[start..newEnd], new(lines.GetPosition(start), lines.GetPosition(oldEnd)));
    }

    private static bool IsSplit(string text, int offset) => offset > 0 && offset < text.Length &&
        (char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]) || text[offset - 1] == '\r' && text[offset] == '\n');
}

internal sealed class LspLineMap
{
    private readonly string _text;
    private readonly int[] _starts;
    private readonly int[] _ends;
    internal int Count => _starts.Length;

    internal LspLineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        var starts = new List<int> { 0 };
        var ends = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            ends.Add(i);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            starts.Add(i + 1);
        }
        ends.Add(text.Length);
        _starts = [.. starts];
        _ends = [.. ends];
    }

    internal int GetLength(int line) => _ends[line] - _starts[line];
    internal LspPosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _text.Length);
        var line = Array.BinarySearch(_starts, offset);
        if (line < 0) line = ~line - 1;
        return new(line, Math.Min(offset, _ends[line]) - _starts[line]);
    }
    internal int GetOffset(LspPosition position)
    {
        if (position.Line < 0 || position.Line >= Count || position.Character < 0 || position.Character > GetLength(position.Line))
            throw new ArgumentOutOfRangeException(nameof(position), "The LSP position is outside the source snapshot.");
        return _starts[position.Line] + position.Character;
    }
}
