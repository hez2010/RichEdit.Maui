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
    internal sealed record Segment(RichNodeId Paragraph, int LocalStart, int LocalLength, int NativeStart, int NativeLength, bool Atom, RichNodeId? Inline = null)
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
                    segments.Add(new(node.Id, local, length, start, end - start, inline is RichImage or RichField, inline.Id));
                    local += length;
                }
                paragraphs.Add(new(node.Id, offsets[(node.Id, false)], offsets[(node.Id, true)], segments.ToImmutable()));
            }
            var boundaries = offsets.Where(pair => pair.Key.Item2 && Snapshot.Get<RichNode>(pair.Key.Item1) is RichTable)
                .ToImmutableDictionary(pair => pair.Key.Item1, pair => pair.Value);
            return (new(Snapshot, text.ToString(), paragraphs.OrderBy(paragraph => paragraph.Start).ToImmutableArray(), boundaries), removals.ToImmutable());
        }
    }

    private readonly ImmutableDictionary<RichNodeId, Paragraph> _paragraphs;
    private readonly ImmutableArray<Paragraph> _ordered;
    private readonly RichTextProjection _text;
    private readonly ImmutableDictionary<RichNodeId, int> _boundaries;
    private StructuredRichNativeMap(RichDocumentSnapshot snapshot, string nativeText, ImmutableArray<Paragraph> paragraphs, ImmutableDictionary<RichNodeId, int> boundaries,
        RichTextProjection? projection = null, RichTextDocumentSnapshot? display = null)
    { Snapshot = snapshot; NativeText = nativeText; _ordered = paragraphs; _paragraphs = paragraphs.ToImmutableDictionary(item => item.Id); _text = projection ?? snapshot.ProjectText(); _boundaries = boundaries; Display = display; }
    internal RichTextDocumentSnapshot? Display { get; }
    internal static StructuredRichNativeMap FromDisplay(RichTextProjection projection, RichTextDocumentSnapshot display, ImmutableArray<Paragraph> paragraphs) =>
        new(projection.Snapshot, display.Text, paragraphs, ImmutableDictionary<RichNodeId, int>.Empty, projection, display);
    internal RichDocumentSnapshot Snapshot { get; }
    internal string NativeText { get; }
    internal ImmutableArray<Paragraph> Paragraphs => _ordered;
    internal RichTextProjection TextProjection => _text;
    internal int? BoundaryPosition(RichBoundarySelection boundary) => boundary.Edge == RichBlockEdge.After && _boundaries.TryGetValue(boundary.Block, out var offset) ? offset : null;

    internal int FromSource(int offset, bool after = true)
    {
        var point = _text.GetPoint(offset, after ? CaretAffinity.Downstream : CaretAffinity.Upstream);
        if (point is { } value) return ToNative(value);
        var next = _text.Paragraphs.FirstOrDefault(paragraph => paragraph.Range.Start > offset);
        if (after && next is not null) return _paragraphs[next.Paragraph].Start;
        var previous = _text.Paragraphs.LastOrDefault(paragraph => paragraph.Range.End < offset);
        return previous is not null ? _paragraphs[previous.Paragraph].End : 0;
    }

    internal int ToSource(int nativeOffset)
    {
        nativeOffset = Math.Clamp(nativeOffset, 0, NativeText.Length);
        if (ToRich(nativeOffset) is { } point) return _text.GetOffset(point);
        var next = _ordered.FirstOrDefault(paragraph => paragraph.Start > nativeOffset);
        return next is not null ? _text.GetOffset(new(Snapshot.Body, next.Id, 0)) : _text.Text.Length;
    }

    internal RichSelection SelectionFromNative(TextSelectionState selection)
    {
        selection.Range.Validate(NativeText.Length);
        if (selection.Range.IsEmpty)
        {
            var atom = _ordered.SelectMany(paragraph => paragraph.Segments).FirstOrDefault(segment => segment.Atom &&
                selection.Active > segment.NativeStart && selection.Active < segment.NativeEnd);
            if (atom?.Inline is { } selected) return new RichObjectSelection(selected);
        }
        if (selection.Range.IsEmpty)
            foreach (var boundary in _boundaries)
                if (boundary.Value == selection.Active) return new RichBoundarySelection(Snapshot.Body, boundary.Key, RichBlockEdge.After);
        var anchor = ToRich(selection.Anchor);
        var active = ToRich(selection.Active);
        var body = Snapshot.Get<RichStory>(Snapshot.Body);
        if (!selection.Range.IsEmpty)
        {
            var atom = _ordered.SelectMany(paragraph => paragraph.Segments).FirstOrDefault(segment => segment.Atom &&
                selection.Range.Start >= segment.NativeStart && selection.Range.End <= segment.NativeEnd);
            if (atom?.Inline is { } selected) return new RichObjectSelection(selected);
        }
        if (Display is null && !selection.Range.IsEmpty && selection.Range.Start == 0 && selection.Range.End >= NativeText.Length - 1)
            return new RichBlockSelection(body.Id, body.Blocks[0], body.Blocks[^1]);
        if (Display is not null && !selection.Range.IsEmpty && selection.Range.Start == 0 && selection.Range.End == NativeText.Length &&
            Snapshot.Get<RichNode>(_text.Root) is RichStory or RichTableCell or RichListItem && !_text.Blocks.IsEmpty)
            return new RichBlockSelection(_text.Root, _text.Blocks[0], _text.Blocks[^1]);
        if (selection.Range.IsEmpty && active is null)
        {
            if (selection.Active >= _ordered[^1].End && Snapshot.Get<RichBlock>(body.Blocks[^1]) is RichTable)
                return new RichBoundarySelection(Snapshot.Body, body.Blocks[^1], RichBlockEdge.After);
            if (selection.Active < _ordered[0].Start && Snapshot.Get<RichBlock>(body.Blocks[0]) is RichTable)
                return new RichBoundarySelection(Snapshot.Body, body.Blocks[0], RichBlockEdge.Before);
        }
        anchor ??= _text.GetPoint(ToSource(selection.Anchor));
        active ??= _text.GetPoint(ToSource(selection.Active));
        if (anchor is null || active is null) throw new ArgumentException("The native range does not have valid rich endpoints.", nameof(selection));
        var firstCell = CellFor(anchor.Value.Paragraph);
        var lastCell = CellFor(active.Value.Paragraph);
        if (firstCell is { } first && lastCell is { } last && first != last)
        {
            var firstRow = Snapshot.Get<RichTableRow>(Snapshot.Get<RichTableCell>(first).Parent!.Value);
            var lastRow = Snapshot.Get<RichTableRow>(Snapshot.Get<RichTableCell>(last).Parent!.Value);
            if (firstRow.Parent == lastRow.Parent)
            {
                var grid = Snapshot.GetTableGrid(firstRow.Parent!.Value);
                var a = grid.GetExtent(first); var b = grid.GetExtent(last);
                var top = Math.Min(a.Row, b.Row); var left = Math.Min(a.Column, b.Column);
                return grid.Expand(new(firstRow.Parent.Value, top, left,
                    Math.Max(a.Row + a.RowCount, b.Row + b.RowCount) - top, Math.Max(a.Column + a.ColumnCount, b.Column + b.ColumnCount) - left));
            }
        }
        return new RichEdit.Core.RichTextSelection(anchor.Value, active.Value);

        RichNodeId? CellFor(RichNodeId paragraph)
        {
            for (var node = Snapshot.Get<RichNode>(paragraph); node.Parent is { } parent; node = Snapshot.Get<RichNode>(parent))
                if (Snapshot.Get<RichNode>(parent) is RichTableCell cell) return cell.Id;
            return null;
        }
    }

    internal static async Task<Prepared> PrepareAsync(RichDocumentSnapshot snapshot, CancellationToken cancellationToken = default,
        string fallbackFont = "Arial", RichFontSize? fallbackSize = null, RichColor? fallbackColor = null,
        RichCellSelection? selectedCells = null, RichColor? selectionBackground = null, RichColor? selectionForeground = null)
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
        await new RtfProjectionWriter(snapshot, stream, fallbackFont, fallbackSize, fallbackColor, marker: (node, end) =>
        {
            var marker = new Marker(node, end, prefix + markers.Count + ":");
            markers.Add(marker);
            return marker.Text;
        }, nativeText: NativeLiteralText, selectedCells: selectedCells, selectionBackground: selectionBackground,
            selectionForeground: selectionForeground).WriteAsync(cancellationToken).ConfigureAwait(false);
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
