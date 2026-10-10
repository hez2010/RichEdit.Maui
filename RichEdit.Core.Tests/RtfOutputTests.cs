using System.Text;
using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public class RtfOutputTests
{
    [Fact]
    public async Task TextEscapingPreservesUtf16AndCannotInjectRtfGroups()
    {
        const string text = "{literal}\\fonttbl\t日本語😀e\u0301\r\n";
        using var stream = new MemoryStream();
        var writer = new RtfOutput(stream);
        await writer.SyntaxAsync(@"{\rtf1\ansi\uc1 ", TestContext.Current.CancellationToken);
        await writer.TextAsync(text.AsMemory(), TestContext.Current.CancellationToken);
        await writer.SyntaxAsync("}", TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.All(stream.ToArray(), value => Assert.InRange(value, (byte)0, (byte)127));
        stream.Position = 0;
        var reader = new RtfTokenReader(stream);
        var decoded = new StringBuilder();
        var fallback = 0;
        var groups = 0;
        while (true)
        {
            var token = await reader.ReadAsync(TestContext.Current.CancellationToken);
            if (token.Kind == RtfTokenKind.End) break;
            if (token.Kind == RtfTokenKind.StartGroup) groups++;
            if (token.Kind == RtfTokenKind.Control && token.Word == "u")
            {
                decoded.Append(unchecked((char)(short)token.Number!.Value));
                fallback = 1;
            }
            else if (token.Kind == RtfTokenKind.Control && token.Word == "tab") decoded.Append('\t');
            else if (token.Kind == RtfTokenKind.Symbol) decoded.Append((char)token.Symbol);
            else if (token.Kind == RtfTokenKind.Text)
                foreach (var value in token.Bytes.ToArray())
                    if (fallback > 0) fallback--; else decoded.Append((char)value);
        }
        Assert.Equal(1, groups);
        Assert.Equal(text, decoded.ToString());
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task LargePayloadsUseBoundedWritesAndCancellationLeavesTheStreamOwnedByTheCaller()
    {
        using var stream = new MeasuredStream();
        var writer = new RtfOutput(stream);
        await writer.HexAsync(new byte[100_000], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.InRange(stream.LargestWrite, 1, 16 * 1024);
        Assert.True(stream.Length >= 200_000);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = stream.Length;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.TextAsync("canceled".AsMemory(), cancellation.Token));
        Assert.Equal(before, stream.Length);
        Assert.True(stream.CanWrite);
    }

    private sealed class MeasuredStream : MemoryStream
    {
        internal int LargestWrite { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
