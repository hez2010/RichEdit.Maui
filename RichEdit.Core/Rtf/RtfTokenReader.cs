using System.Text;

namespace RichEdit.Core.Rtf;

internal enum RtfTokenKind { StartGroup, EndGroup, Control, Symbol, HexByte, Text, Binary, End }
internal sealed record RtfToken(RtfTokenKind Kind, long Offset, string? Word = null, int? Number = null,
    ReadOnlyMemory<byte> Bytes = default, byte Symbol = 0);

// Byte-oriented input is essential: ANSI and DBCS runs use the current RTF code
// page, while a binary destination must retain every byte including braces.
internal sealed class RtfTokenReader(Stream input, long maximumBytes = 512L * 1024 * 1024,
    int maximumDepth = 128, int maximumBinaryBytes = 64 * 1024 * 1024)
{
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;
    private int _peek = -2;
    private int _depth;
    private long _bytesRead;
    private long _offset;

    internal async ValueTask<RtfToken> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int value;
        do { value = await TakeAsync(cancellationToken).ConfigureAwait(false); } while (value is '\r' or '\n');
        var offset = _offset - (value < 0 ? 0 : 1);
        if (value < 0)
        {
            if (_depth != 0) throw Error("Unclosed RTF group", offset);
            return new(RtfTokenKind.End, offset);
        }
        if (value == '{')
        {
            if (++_depth > maximumDepth) throw Error("RTF nesting limit exceeded", offset);
            return new(RtfTokenKind.StartGroup, offset);
        }
        if (value == '}')
        {
            if (_depth-- == 0) throw Error("Unexpected RTF closing brace", offset);
            return new(RtfTokenKind.EndGroup, offset);
        }
        if (value != '\\')
        {
            var text = new List<byte>(128) { (byte)value };
            while (text.Count < 4096 && await PeekAsync(cancellationToken).ConfigureAwait(false) is var next && next >= 0 && next is not ('{' or '}' or '\\' or '\r' or '\n'))
                text.Add((byte)await TakeAsync(cancellationToken).ConfigureAwait(false));
            return new(RtfTokenKind.Text, offset, Bytes: text.ToArray());
        }

        value = await TakeAsync(cancellationToken).ConfigureAwait(false);
        if (value < 0) throw Error("Incomplete RTF escape", offset);
        if (value == '\'')
        {
            var first = Hex(await TakeAsync(cancellationToken).ConfigureAwait(false));
            var second = Hex(await TakeAsync(cancellationToken).ConfigureAwait(false));
            if (first < 0 || second < 0) throw Error("Invalid RTF hexadecimal byte", offset);
            return new(RtfTokenKind.HexByte, offset, Symbol: (byte)(first * 16 + second));
        }
        if (!Letter(value)) return new(RtfTokenKind.Symbol, offset, Symbol: (byte)value);
        var word = new StringBuilder(16);
        word.Append((char)value);
        while (Letter(await PeekAsync(cancellationToken).ConfigureAwait(false)))
        {
            if (word.Length == 32) throw Error("RTF control words cannot exceed 32 letters", offset);
            word.Append((char)await TakeAsync(cancellationToken).ConfigureAwait(false));
        }
        var negative = await PeekAsync(cancellationToken).ConfigureAwait(false) == '-';
        if (negative) _ = await TakeAsync(cancellationToken).ConfigureAwait(false);
        long number = 0;
        var digits = 0;
        while (await PeekAsync(cancellationToken).ConfigureAwait(false) is >= '0' and <= '9')
        {
            number = number * 10 + await TakeAsync(cancellationToken).ConfigureAwait(false) - '0';
            if (++digits > 10 || number > (negative ? 2147483648L : int.MaxValue)) throw Error("RTF numeric parameter is outside the 32-bit range", offset);
        }
        if (negative && digits == 0) throw Error("An RTF parameter sign requires digits", offset);
        int? parameter = digits == 0 ? null : (int)(negative ? -number : number);
        if (await PeekAsync(cancellationToken).ConfigureAwait(false) == ' ') _ = await TakeAsync(cancellationToken).ConfigureAwait(false);
        var name = word.ToString();
        if (name != "bin") return new(RtfTokenKind.Control, offset, name, parameter);
        if (parameter is null or < 0 || parameter > maximumBinaryBytes) throw Error("Invalid or oversized RTF binary destination", offset);
        var bytes = new byte[parameter.Value];
        for (var index = 0; index < bytes.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var next = await TakeAsync(cancellationToken).ConfigureAwait(false);
            if (next < 0) throw Error("Truncated RTF binary destination", offset);
            bytes[index] = (byte)next;
        }
        return new(RtfTokenKind.Binary, offset, name, parameter, bytes);
    }

    private async ValueTask<int> PeekAsync(CancellationToken cancellationToken)
    {
        if (_peek != -2) return _peek;
        if (_start == _end)
        {
            _end = await input.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            _start = 0;
            _bytesRead += _end;
            if (_bytesRead > maximumBytes) throw Error("RTF input size limit exceeded", _offset);
            if (_end == 0) return _peek = -1;
        }
        return _peek = _buffer[_start++];
    }
    private async ValueTask<int> TakeAsync(CancellationToken cancellationToken)
    {
        var value = await PeekAsync(cancellationToken).ConfigureAwait(false);
        _peek = -2;
        if (value >= 0) _offset++;
        return value;
    }
    private static bool Letter(int value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    private static int Hex(int value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };
    private static FormatException Error(string message, long offset) => new($"{message} at byte {offset}.");
}
