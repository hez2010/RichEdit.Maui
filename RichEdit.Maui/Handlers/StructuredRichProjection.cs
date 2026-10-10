using System.Text;
using RichEdit.Core;
using RichEdit.Core.Rtf;

namespace RichEdit.Maui;

internal static class StructuredRichProjection
{
    internal static async Task<string> CreateRtfAsync(RichDocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        await new RtfProjectionWriter(snapshot, stream).WriteAsync(cancellationToken).ConfigureAwait(false);
        return Encoding.ASCII.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }
}
