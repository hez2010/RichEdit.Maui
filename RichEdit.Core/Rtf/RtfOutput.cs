using System.Globalization;

namespace RichEdit.Core.Rtf;

// Writes only RTF syntax and escaped UTF-16 text. Image data and text are emitted
// in bounded chunks; ownership and replacement of the destination remain with the caller.
internal sealed class RtfOutput(Stream output)
{
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _count;

    internal async ValueTask SyntaxAsync(string syntax, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var character in syntax)
        {
            if (character > 127) throw new ArgumentException("RTF syntax must be ASCII. Use escaped text for document values.", nameof(syntax));
            await ByteAsync((byte)character, cancellationToken).ConfigureAwait(false);
        }
    }

    internal ValueTask ControlAsync(string word, int? parameter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(word) || word.Length > 32 || word.Any(character => character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z')))
            throw new ArgumentException("An RTF control name contains one through thirty-two ASCII letters.", nameof(word));
        return SyntaxAsync("\\" + word + parameter?.ToString(CultureInfo.InvariantCulture) + " ", cancellationToken);
    }

    internal async ValueTask TextAsync(ReadOnlyMemory<char> text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text.Span[index];
            switch (character)
            {
                case '{' or '}' or '\\':
                    await ByteAsync((byte)'\\', cancellationToken).ConfigureAwait(false);
                    await ByteAsync((byte)character, cancellationToken).ConfigureAwait(false);
                    break;
                case '\t': await SyntaxAsync(@"\tab ", cancellationToken).ConfigureAwait(false); break;
                case >= ' ' and <= '~': await ByteAsync((byte)character, cancellationToken).ConfigureAwait(false); break;
                default:
                    // RTF Unicode values are signed UTF-16 code units. A surrogate
                    // pair is deliberately written as two controls with one fallback each.
                    await SyntaxAsync("\\u" + unchecked((short)character).ToString(CultureInfo.InvariantCulture) + "?", cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    internal async ValueTask HexAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string digits = "0123456789abcdef";
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes.Span[index];
            await ByteAsync((byte)digits[value >> 4], cancellationToken).ConfigureAwait(false);
            await ByteAsync((byte)digits[value & 15], cancellationToken).ConfigureAwait(false);
            if ((index & 63) == 63) await ByteAsync((byte)'\n', cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ByteAsync(byte value, CancellationToken cancellationToken)
    {
        if (_count == _buffer.Length) await FlushAsync(cancellationToken).ConfigureAwait(false);
        _buffer[_count++] = value;
    }

    internal async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_count == 0) return;
        await output.WriteAsync(_buffer.AsMemory(0, _count), cancellationToken).ConfigureAwait(false);
        _count = 0;
    }
}
