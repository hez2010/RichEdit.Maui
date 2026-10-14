using System.Text;
using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public class RtfTokenReaderTests
{
    [Fact]
    public async Task LeadingZeroesDoNotChangeAnOtherwiseRepresentableParameter()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(@"{\rtf1\u00000000000000000065?\u-000000000000000010179?}"));
        var values = (await ReadAll(new(stream))).Where(token => token.Word == "u").Select(token => token.Number).ToArray();
        Assert.Equal(new int?[] { 65, -10179 }, values);
    }

    [Fact]
    public async Task ByteTokensRetainAnsiEscapesBinaryBracesAndOffsetsAcrossShortReads()
    {
        var prefix = Encoding.ASCII.GetBytes("{\\rtf1\\ansi A\\'e9\\u-10179?{\\*\\pict\\bin4 ");
        using var stream = new ShortReadStream(prefix.Concat(new byte[] { 0, (byte)'{', (byte)'\\', 255 }).Concat("}}"u8.ToArray()).ToArray());
        var tokens = await ReadAll(new(stream));
        Assert.Equal(RtfTokenKind.StartGroup, tokens[0].Kind);
        Assert.Equal("rtf", tokens[1].Word);
        Assert.Equal(1, tokens[1].Offset);
        Assert.Equal(1, tokens[1].Number);
        Assert.Equal(0xe9, Assert.Single(tokens, token => token.Kind == RtfTokenKind.HexByte).Symbol);
        Assert.Equal(-10179, Assert.Single(tokens, token => token.Word == "u").Number);
        Assert.Equal(new byte[] { 0, (byte)'{', (byte)'\\', 255 }, Assert.Single(tokens, token => token.Kind == RtfTokenKind.Binary).Bytes.ToArray());
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("{\\rtf1")]
    [InlineData("{\\rtf1}}")]
    [InlineData("{\\rtf1\\'xz}")]
    [InlineData("{\\rtf1\\bin5 xx}")]
    [InlineData("{\\rtf1\\u2147483648 }")]
    [InlineData("{\\rtf1\\u-2147483649 }")]
    [InlineData("{\\rtf1\\abcdefghijklmnopqrstuvwxyzabcdefgh }")]
    public async Task InvalidLexicalInputIsRejected(string input)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(input));
        await Assert.ThrowsAsync<FormatException>(async () => await ReadAll(new(stream)));
    }

    [Fact]
    public async Task CancellationAndResourceLimitsStopReadingWithoutClosingTheCallerStream()
    {
        using var stream = new MemoryStream("{\\rtf1 {text}}"u8.ToArray());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new RtfTokenReader(stream).ReadAsync(canceled.Token));
        Assert.Equal(0, stream.Position);
        await Assert.ThrowsAsync<FormatException>(async () => await ReadAll(new(stream, maximumDepth: 1)));
        stream.Position = 0;
        await Assert.ThrowsAsync<FormatException>(async () => await ReadAll(new(stream, maximumBytes: 4)));
        Assert.True(stream.CanRead);
        using var binary = new MemoryStream("{\\rtf1\\bin1000000000 }"u8.ToArray());
        await Assert.ThrowsAsync<FormatException>(async () => await ReadAll(new(binary, maximumBinaryBytes: 10)));
    }

    private static async Task<List<RtfToken>> ReadAll(RtfTokenReader reader)
    {
        var tokens = new List<RtfToken>();
        while (true)
        {
            var token = await reader.ReadAsync();
            tokens.Add(token);
            if (token.Kind == RtfTokenKind.End) return tokens;
        }
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
}
