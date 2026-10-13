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
    private readonly StructuredRichNativeMap? _rendered;
    private readonly StructuredRichPresentation? _presentation;
    internal ImmutableArray<RichTextDisplayReservation> Reservations { get; }
    private StructuredRichNativeMap(RichDocumentSnapshot snapshot, string nativeText, ImmutableArray<Paragraph> paragraphs, ImmutableDictionary<RichNodeId, int> boundaries,
        RichTextProjection? projection = null, RichTextDocumentSnapshot? display = null, RichTextDocumentSnapshot? authoredDisplay = null,
        StructuredRichNativeMap? rendered = null, StructuredRichPresentation? presentation = null, ImmutableArray<RichTextDisplayReservation> reservations = default)
    { Snapshot = snapshot; NativeText = nativeText; _ordered = paragraphs; _paragraphs = paragraphs.ToImmutableDictionary(item => item.Id); _text = projection ?? snapshot.ProjectText(); _boundaries = boundaries; Display = display; AuthoredDisplay = authoredDisplay;
        _rendered = rendered; _presentation = presentation; Reservations = reservations.IsDefault ? [] : reservations; }
    internal RichTextDocumentSnapshot? Display { get; }
    internal RichTextDocumentSnapshot? AuthoredDisplay { get; }
    internal (TextSpan Before, TextSpan After)? FindBlockReplacement(StructuredRichNativeMap next, IReadOnlySet<RichNodeId>? invalidatedBlocks = null)
    {
        if (!Reservations.IsEmpty || !next.Reservations.IsEmpty || Snapshot.Body != next.Snapshot.Body) return null;
        var before = Snapshot.Get<RichStory>(Snapshot.Body).Blocks;
        var after = next.Snapshot.Get<RichStory>(next.Snapshot.Body).Blocks;
        var prefix = 0;
        while (prefix < Math.Min(before.Count, after.Count) && Same(before[prefix], after[prefix])) prefix++;
        var suffix = 0;
        while (suffix < Math.Min(before.Count, after.Count) - prefix && Same(before[^(suffix + 1)], after[^(suffix + 1)])) suffix++;
        if (prefix == before.Count && prefix == after.Count) return null;
        if (before.Count - prefix - suffix == 1 && after.Count - prefix - suffix == 1 &&
            before[prefix] == after[prefix] && Snapshot.Get<RichBlock>(before[prefix]) is RichParagraph)
        {
            var a = _paragraphs[before[prefix]]; var b = next._paragraphs[after[prefix]];
            return (new(a.Start, a.End - a.Start), new(b.Start, b.End - b.Start));
        }
        // RTF fragment boundaries belong in ordinary body paragraphs, outside
        // native cell/list terminators. Include unchanged paragraph context on
        // each side so native paste retains the surrounding block structure.
        var first = prefix - 1;
        while (first >= 0 && Snapshot.Get<RichBlock>(before[first]) is not RichParagraph) first--;
        var oldStart = first >= 0 ? _paragraphs[before[first]].Start : 0;
        var newStart = first >= 0 ? next._paragraphs[after[first]].Start : 0;
        var oldTail = before.Count - suffix;
        var newTail = after.Count - suffix;
        while (oldTail < before.Count && Snapshot.Get<RichBlock>(before[oldTail]) is not RichParagraph) { oldTail++; newTail++; }
#if WINDOWS
        var oldLimit = Math.Max(0, NativeText.Length - 1); var newLimit = Math.Max(0, next.NativeText.Length - 1);
#else
        var oldLimit = NativeText.Length; var newLimit = next.NativeText.Length;
#endif
        var oldEnd = oldTail < before.Count ? Math.Min(oldLimit, _paragraphs[before[oldTail]].End + 1) : oldLimit;
        var newEnd = newTail < after.Count ? Math.Min(newLimit, next._paragraphs[after[newTail]].End + 1) : newLimit;
        return (new(oldStart, oldEnd - oldStart), new(newStart, newEnd - newStart));

        bool Same(RichNodeId a, RichNodeId b)
        {
            if (invalidatedBlocks?.Contains(a) == true) return false;
            if (a != b || !Snapshot.Nodes.TryGetValue(a, out var x) || !next.Snapshot.Nodes.TryGetValue(b, out var y) || !RichDocumentSnapshot.SameNode(x, y)) return false;
            var children = RichDocumentSnapshot.Children(x);
            return children.All(id => Same(id, id));
        }
    }
    internal bool CanApplyFormatting(StructuredRichNativeMap next)
    {
        if (NativeText != next.NativeText || _ordered.Length != next._ordered.Length || !Reservations.SequenceEqual(next.Reservations)) return false;
        for (var index = 0; index < _ordered.Length; index++)
        {
            var a = _ordered[index]; var b = next._ordered[index];
            if (a.Id != b.Id || a.Start != b.Start || a.End != b.End || Snapshot.Get<RichParagraph>(a.Id).Parent != next.Snapshot.Get<RichParagraph>(b.Id).Parent) return false;
        }
        var beforeNodes = Snapshot.Nodes.Values.Where(node => node is not (RichParagraph or RichText or RichSoftBreak)).ToDictionary(node => node.Id);
        var afterNodes = next.Snapshot.Nodes.Values.Where(node => node is not (RichParagraph or RichText or RichSoftBreak)).ToArray();
        if (beforeNodes.Count != afterNodes.Length) return false;
        foreach (var node in afterNodes)
        {
            if (!beforeNodes.TryGetValue(node.Id, out var before)) return false;
            if (before is RichInline a && node is RichInline b) before = a with { Format = b.Format, Style = b.Style };
            if (!RichDocumentSnapshot.SameNode(before, node)) return false;
        }
        var prior = RichTextDocumentSnapshot.CreateStructuredProjection(_text);
        var current = RichTextDocumentSnapshot.CreateStructuredProjection(next._text);
        return prior.Links.SequenceEqual(current.Links) && prior.Images.SequenceEqual(current.Images);
    }
    internal StructuredRichNativeMap WithSnapshot(RichDocumentSnapshot snapshot) =>
        new(snapshot, NativeText, _ordered, _boundaries, snapshot.ProjectText(_text.Root), Display, AuthoredDisplay, _rendered, _presentation, Reservations);

    internal bool HasSameStructureAndFormatting(RichDocumentSnapshot snapshot, RichNodeId editedParagraph)
    {
        if (Snapshot.DefaultCharacter != snapshot.DefaultCharacter || Snapshot.DefaultParagraph != snapshot.DefaultParagraph ||
            Snapshot.Styles != snapshot.Styles) return false;
        foreach (var node in Snapshot.Nodes.Values.Concat(snapshot.Nodes.Values).DistinctBy(node => node.Id))
        {
            if (node is RichText && node.Parent == editedParagraph) continue;
            if (!Snapshot.Nodes.TryGetValue(node.Id, out var before) || !snapshot.Nodes.TryGetValue(node.Id, out var after)) return false;
            if (before is RichParagraph a && after is RichParagraph b && a.Id == editedParagraph)
            { if (a with { Inlines = b.Inlines } != b) return false; }
            else if (!RichDocumentSnapshot.SameNode(before, after)) return false;
        }
        return true;
    }

    internal StructuredRichNativeMap? TryApplyNativeText(RichDocumentSnapshot snapshot, string nativeText, RichSelection selection)
    {
        // An inline edit does not change native table/field separators. Keep the
        // native addresses of those objects and shift the following paragraphs.
        if (_rendered is not null || Display is not null || selection is not RichEdit.Core.RichTextSelection text ||
            text.Anchor.Paragraph != text.Active.Paragraph) return null;
        var id = text.Anchor.Paragraph;
        if (!snapshot.Nodes.TryGetValue(id, out var node) || node is not RichParagraph paragraph ||
            snapshot.Nodes.Values.OfType<RichParagraph>().Count() != _ordered.Length) return null;
        var prior = _paragraphs[id];
        var delta = nativeText.Length - NativeText.Length;
        var segments = ImmutableArray.CreateBuilder<Segment>();
        var position = prior.Start;
        var local = 0;
        foreach (var inlineId in paragraph.Inlines)
        {
            var inline = snapshot.Get<RichInline>(inlineId);
            var length = RichDocumentSnapshot.InlineLength(inline);
            var atom = inline is RichImage or RichField;
            var previous = atom ? prior.Segments.FirstOrDefault(item => item.Inline == inlineId) : null;
            if (atom && previous is null) return null;
            var nativeLength = previous?.NativeLength ?? length;
            if (position + nativeLength > nativeText.Length) return null;
            if (inline is RichText run && !nativeText.AsSpan(position, nativeLength).SequenceEqual(NativeLiteralText(run.Content.ToString()))) return null;
            segments.Add(new(id, local, length, position, nativeLength, atom, inlineId));
            position += nativeLength;
            local += length;
        }
        if (position != prior.End + delta) return null;
        var paragraphs = _ordered.Select(item => item.Id == id ? new Paragraph(id, prior.Start, position, segments.ToImmutable()) :
            item.Start < prior.Start ? item : item with
            {
                Start = item.Start + delta, End = item.End + delta,
                Segments = item.Segments.Select(segment => segment with { NativeStart = segment.NativeStart + delta }).ToImmutableArray(),
            }).ToImmutableArray();
        var boundaries = _boundaries.ToImmutableDictionary(item => item.Key, item => item.Value < prior.End ? item.Value : item.Value + delta);
        var first = Math.Min(ToNative(text.Anchor), ToNative(text.Active));
        var end = Math.Max(ToNative(text.Anchor), ToNative(text.Active));
        var authored = AuthoredDisplay?.Replace(first..end, nativeText.Substring(first, end - first + delta));
        return new(snapshot, nativeText, paragraphs, boundaries, authoredDisplay: authored);
    }
    internal StructuredRichNativeMap WithAuthoredDisplay(RichTextDocumentSnapshot display) => new(Snapshot, NativeText, _ordered, _boundaries, _text, Display, display, _rendered, _presentation, Reservations);
    internal StructuredRichNativeMap WithPresentation(StructuredRichPresentation presentation, RichTextProjection source)
    {
        var privateImages = presentation.Widgets.ToDictionary(item => item.Image);
        var privateParagraphs = presentation.Widgets.Where(item => item.Paragraph is not null).ToDictionary(item => item.Paragraph!.Value);
        var paragraphs = ImmutableArray.CreateBuilder<Paragraph>();
        var reservations = ImmutableArray.CreateBuilder<RichTextDisplayReservation>();
        for (var index = 0; index < _ordered.Length; index++)
        {
            var paragraph = _ordered[index];
            if (privateParagraphs.TryGetValue(paragraph.Id, out var block))
            {
                var start = block.Adornment.Options.Placement == RichTextAdornmentPlacement.AboveLine ? paragraph.Start : _ordered[index - 1].End;
                var end = block.Adornment.Options.Placement == RichTextAdornmentPlacement.AboveLine ? _ordered[index + 1].Start : paragraph.End;
                reservations.Add(new(block.Adornment, source.GetOffset(block.Anchor), start, NativeText.Substring(start, end - start)));
                continue;
            }
            var segments = ImmutableArray.CreateBuilder<Segment>();
            foreach (var segment in paragraph.Segments)
            {
                if (segment.Inline is { } id && privateImages.TryGetValue(id, out var widget))
                {
                    reservations.Add(new(widget.Adornment, source.GetOffset(widget.Anchor), segment.NativeStart,
                        NativeText.Substring(segment.NativeStart, segment.NativeLength)));
                    continue;
                }
                var start = presentation.Backward.Map(new(Snapshot.Body, paragraph.Id, segment.LocalStart))!.Value;
                var end = presentation.Backward.Map(new(Snapshot.Body, paragraph.Id, segment.LocalEnd))!.Value;
                segments.Add(segment with { LocalStart = start.Offset, LocalLength = end.Offset - start.Offset });
            }
            paragraphs.Add(paragraph with { Segments = segments.ToImmutable() });
        }
        return new(presentation.Source, NativeText, paragraphs.ToImmutable(), _boundaries, source, Display, AuthoredDisplay,
            this, presentation, reservations.OrderBy(item => item.DisplayStart).ToImmutableArray());
    }
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
        anchor ??= SelectionBoundary(selection.Anchor, selection.Anchor > selection.Active);
        active ??= SelectionBoundary(selection.Active, selection.Active > selection.Anchor);
        if (anchor is null || active is null) throw new ArgumentException("The native range does not have valid rich endpoints.", nameof(selection));
        // Native table selections include cell/row separators. Their exclusive
        // end can be the next cell's start, so use the paragraphs inside the
        // selected interval rather than the caret at its end.
        if (!selection.Range.IsEmpty &&
            _ordered.FirstOrDefault(paragraph => paragraph.End >= selection.Range.Start && paragraph.Start < selection.Range.End) is { } firstParagraph &&
            _ordered.LastOrDefault(paragraph => paragraph.Start < selection.Range.End && paragraph.End >= selection.Range.Start) is { } lastParagraph)
        {
            var lastCells = CellsFor(lastParagraph.Id).ToDictionary(cell => Snapshot.Get<RichTableRow>(cell.Parent!.Value).Parent!.Value);
            foreach (var first in CellsFor(firstParagraph.Id))
            {
                var table = Snapshot.Get<RichTableRow>(first.Parent!.Value).Parent!.Value;
                if (!lastCells.TryGetValue(table, out var last)) continue;
                var grid = Snapshot.GetTableGrid(table);
                if (first.Id == last.Id)
                {
                    var paragraphs = Snapshot.EnumerateParagraphs(first.Id).ToArray();
                    if (paragraphs.All(_paragraphs.ContainsKey) && selection.Range.Start <= _paragraphs[paragraphs[0]].Start &&
                        selection.Range.End > _paragraphs[paragraphs[^1]].End)
                        return grid.GetExtent(first.Id);
                    break;
                }
                var a = grid.GetExtent(first.Id); var b = grid.GetExtent(last.Id);
                var top = Math.Min(a.Row, b.Row); var left = Math.Min(a.Column, b.Column);
                return grid.Expand(new(table, top, left,
                    Math.Max(a.Row + a.RowCount, b.Row + b.RowCount) - top, Math.Max(a.Column + a.ColumnCount, b.Column + b.ColumnCount) - left));
            }
        }
        return new RichEdit.Core.RichTextSelection(anchor.Value, active.Value);

        RichTextPoint? SelectionBoundary(int offset, bool trailing)
        {
            // An exclusive end in a native row/paragraph separator belongs to
            // the preceding content, not the first cell after that separator.
            if (trailing && _ordered.LastOrDefault(paragraph => paragraph.End <= offset) is { } previous)
                return new(Snapshot.Body, previous.Id, Snapshot.GetParagraphLength(previous.Id));
            return _text.GetPoint(ToSource(offset));
        }

        IEnumerable<RichTableCell> CellsFor(RichNodeId paragraph)
        {
            for (var node = Snapshot.Get<RichNode>(paragraph); node.Parent is { } parent; node = Snapshot.Get<RichNode>(parent))
                if (Snapshot.Get<RichNode>(parent) is RichTableCell cell) yield return cell;
        }
    }

    internal static async Task<Prepared> PrepareAsync(RichDocumentSnapshot snapshot, CancellationToken cancellationToken = default,
        string fallbackFont = "Arial", RichFontSize? fallbackSize = null, RichColor? fallbackColor = null,
        RichCellSelection? selectedCells = null, RichColor? selectionBackground = null, RichColor? selectionForeground = null,
        IReadOnlySet<RichNodeId>? nativePrivateParagraphs = null, bool nativeImagePlaceholders = false)
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
            selectionForeground: selectionForeground, nativePrivateParagraphs: nativePrivateParagraphs,
            nativeImagePlaceholders: nativeImagePlaceholders).WriteAsync(cancellationToken).ConfigureAwait(false);
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
        if (_rendered is not null) return _rendered.ToNative(_presentation!.Forward.Map(point)!.Value);
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
        if (_rendered is not null)
        {
            var reservation = Reservations.FirstOrDefault(item => nativeOffset >= item.DisplayStart && nativeOffset < item.DisplayEnd);
            if (reservation is not null) return _presentation!.Widgets.First(item => ReferenceEquals(item.Adornment, reservation.Adornment)).Anchor with { Affinity = affinity };
            if (_rendered.ToRich(nativeOffset, affinity) is not { } point) return null;
            if (_presentation!.Widgets.FirstOrDefault(item => item.Paragraph == point.Paragraph) is { } block) return block.Anchor with { Affinity = affinity };
            return _presentation.Backward.Map(point);
        }
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
