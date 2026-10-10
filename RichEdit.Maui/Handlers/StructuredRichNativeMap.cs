using System.Collections.Immutable;
using System.Text;
using RichEdit.Core;
using RichEdit.Core.Rtf;

namespace RichEdit.Maui;

// Native RTF import owns layout. Temporary markers are resolved and removed in
// a detached native document, before assigning content to an observable editor.
// Mapping never recognizes authored content by a magic character value.
internal sealed class StructuredRichNativeMap
{
    internal sealed record Marker(RichNodeId Node, bool End, string Text);
    internal sealed record Segment(RichNodeId Paragraph, int LocalStart, int LocalLength, int NativeStart, int NativeLength, bool Atom)
    {
        internal int NativeEnd => NativeStart + NativeLength;
        internal int LocalEnd => LocalStart + LocalLength;
    }
    internal sealed record Paragraph(RichNodeId Id, int Start, int End, ImmutableArray<Segment> Segments);
    internal sealed record Prepared(string Rtf, ImmutableArray<Marker> Markers, RichDocumentSnapshot Snapshot, string MarkerPrefix)
    {
        internal (StructuredRichNativeMap Map, ImmutableArray<TextSpan> Removals) Resolve(string nativeText)
        {
            var positions = new List<(Marker Marker, int Start)>();
            var pending = Markers.ToDictionary(marker => marker.Text, StringComparer.Ordinal);
            var search = 0;
            while (nativeText.IndexOf(MarkerPrefix, search, StringComparison.Ordinal) is var index && index >= 0)
            {
                var end = nativeText.IndexOf(':', index + MarkerPrefix.Length);
                if (end < 0 || !pending.Remove(nativeText[index..(end + 1)], out var marker))
                    throw new InvalidOperationException("The native RTF engine did not preserve a unique projection marker.");
                positions.Add((marker, index));
                search = end + 1;
            }
            if (pending.Count != 0) throw new InvalidOperationException("The native RTF engine omitted a projection marker.");
            var offsets = new Dictionary<(RichNodeId, bool), int>();
            var removals = ImmutableArray.CreateBuilder<TextSpan>();
            var removed = 0;
            var text = new StringBuilder(nativeText.Length);
            var previous = 0;
            foreach (var (marker, position) in positions)
            {
                offsets.Add((marker.Node, marker.End), position - removed);
                removals.Add(new(position, marker.Text.Length));
                text.Append(nativeText, previous, position - previous);
                previous = position + marker.Text.Length;
                removed += marker.Text.Length;
            }
            text.Append(nativeText, previous, nativeText.Length - previous);
            var paragraphs = ImmutableArray.CreateBuilder<Paragraph>();
            foreach (var node in Snapshot.Nodes.Values.OfType<RichParagraph>())
            {
                var segments = ImmutableArray.CreateBuilder<Segment>();
                var local = 0;
                foreach (var id in node.Inlines)
                {
                    var inline = Snapshot.Get<RichInline>(id);
                    var start = offsets[(id, false)];
                    var end = offsets[(id, true)];
                    var length = inline is RichText run ? run.Content.Length : 1;
                    if (end < start || inline is RichText && end - start != length || inline is RichSoftBreak && end - start != 1)
                        throw new InvalidOperationException($"The native RTF engine changed authored text during projection (node {id.Value}, expected {length}, observed {end - start}).");
                    segments.Add(new(node.Id, local, length, start, end - start, inline is RichImage or RichField));
                    local += length;
                }
                paragraphs.Add(new(node.Id, offsets[(node.Id, false)], offsets[(node.Id, true)], segments.ToImmutable()));
            }
            return (new(Snapshot, text.ToString(), paragraphs.OrderBy(paragraph => paragraph.Start).ToImmutableArray()), removals.ToImmutable());
        }
    }

    private readonly ImmutableDictionary<RichNodeId, Paragraph> _paragraphs;
    private readonly ImmutableArray<Paragraph> _ordered;
    private StructuredRichNativeMap(RichDocumentSnapshot snapshot, string nativeText, ImmutableArray<Paragraph> paragraphs)
    { Snapshot = snapshot; NativeText = nativeText; _ordered = paragraphs; _paragraphs = paragraphs.ToImmutableDictionary(item => item.Id); }
    internal RichDocumentSnapshot Snapshot { get; }
    internal string NativeText { get; }
    internal ImmutableArray<Paragraph> Paragraphs => _ordered;

    internal static async Task<Prepared> PrepareAsync(RichDocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var prefix = "RichMap" + Guid.NewGuid().ToString("N") + ":";
        // A randomly chosen marker must still be checked against authored text;
        // uniqueness, not an improbable collision assumption, is the contract.
        while (snapshot.Nodes.Values.Any(node => node is RichText text && text.Content.ToString().Contains(prefix, StringComparison.Ordinal) ||
            node is RichField field && (field.Result.Contains(prefix, StringComparison.Ordinal) || field.Instruction.Contains(prefix, StringComparison.Ordinal)) ||
            node is RichInline { Link: { } link } && (link.Target.Contains(prefix, StringComparison.Ordinal) || link.ToolTip?.Contains(prefix, StringComparison.Ordinal) == true)))
            prefix = "RichMap" + Guid.NewGuid().ToString("N") + ":";
        var markers = ImmutableArray.CreateBuilder<Marker>();
        using var stream = new MemoryStream();
        await new RtfProjectionWriter(snapshot, stream, marker: (node, end) =>
        {
            var marker = new Marker(node, end, prefix + markers.Count + ":");
            markers.Add(marker);
            return marker.Text;
        }, nativeText: NativeLiteralText).WriteAsync(cancellationToken).ConfigureAwait(false);
        return new(Encoding.ASCII.GetString(stream.GetBuffer(), 0, checked((int)stream.Length)), markers.ToImmutable(), snapshot, prefix);
    }

    private static string NativeLiteralText(string text)
    {
#if WINDOWS
        // The RTF engine discards its reserved annotation/object code points
        // when they occur as literal authored text. Native square glyphs retain
        // their positions; the canonical document and RTF codec retain the values.
        return text.Replace('\uFFF9', '\u25A1').Replace('\uFFFA', '\u25A1').Replace('\uFFFB', '\u25A1').Replace('\uFFFC', '\u25A1');
#else
        return text;
#endif
    }

    internal int ToNative(RichTextPoint point)
    {
        Snapshot.ValidatePoint(point);
        var paragraph = _paragraphs[point.Paragraph];
        if (paragraph.Segments.IsEmpty) return paragraph.Start;
        foreach (var segment in paragraph.Segments)
        {
            if (point.Offset < segment.LocalStart || point.Offset > segment.LocalEnd) continue;
            if (point.Offset == segment.LocalEnd && point.Affinity == CaretAffinity.Downstream && segment != paragraph.Segments[^1]) continue;
            return segment.Atom ? point.Offset == segment.LocalStart ? segment.NativeStart : segment.NativeEnd :
                segment.NativeStart + point.Offset - segment.LocalStart;
        }
        return paragraph.End;
    }

    internal RichTextPoint? ToRich(int nativeOffset, CaretAffinity affinity = CaretAffinity.Downstream)
    {
        if (nativeOffset < 0 || nativeOffset > NativeText.Length) throw new ArgumentOutOfRangeException(nameof(nativeOffset));
        var low = 0; var high = _ordered.Length;
        while (low < high)
        { var middle = low + (high - low) / 2; if (_ordered[middle].Start <= nativeOffset) low = middle + 1; else high = middle; }
        if (low == 0) return null;
        var paragraph = _ordered[low - 1];
        if (nativeOffset > paragraph.End) return null;
        foreach (var segment in paragraph.Segments)
        {
            if (nativeOffset < segment.NativeStart) return new(Snapshot.Body, paragraph.Id, segment.LocalStart, affinity);
            if (nativeOffset > segment.NativeEnd) continue;
            var local = segment.Atom ? nativeOffset == segment.NativeStart ? segment.LocalStart : segment.LocalEnd : segment.LocalStart + nativeOffset - segment.NativeStart;
            return new(Snapshot.Body, paragraph.Id, local, affinity);
        }
        return new(Snapshot.Body, paragraph.Id, Snapshot.GetParagraphLength(paragraph.Id), affinity);
    }
}
