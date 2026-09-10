using System.Collections.Immutable;
using System.Text;

namespace RichEdit.Maui;

// Native reservations are identified by an object owned by the handler, never by their text.
// A literal U+FFFC, authored image, or source newline is therefore never removed by this map.
internal sealed record RichTextDisplayReservation(RichTextAdornment Adornment, int SourcePosition, int DisplayStart, string Text)
{
    internal int DisplayEnd => DisplayStart + Text.Length;
    internal int ImagePosition => Text.IndexOf('\uFFFC') is var offset && offset >= 0 ? DisplayStart + offset : -1;
}

// Native spans and ranges track positions. Their payload only identifies a private
// character and its application view; it does not retain obsolete source offsets.
internal readonly record struct RichTextDisplayMarker(RichTextAdornment Adornment, char Character);

internal sealed class RichTextDisplayProjection
{
    internal static ImmutableArray<byte> TransparentPixel { get; } = [.. Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=")];
    internal static RichTextDisplayProjection Empty { get; } = new([]);
    internal ImmutableArray<RichTextDisplayReservation> Reservations { get; }
    internal bool IsEmpty => Reservations.IsEmpty;
    internal IEnumerable<(int Position, RichTextDisplayMarker Marker)> Markers => Reservations.SelectMany(static item =>
        item.Text.Select((character, offset) => (item.DisplayStart + offset, new RichTextDisplayMarker(item.Adornment, character))));
    private readonly int[] _sourcePositions;
    private readonly int[] _displayStarts;
    private readonly int[] _prefixLengths;

    internal RichTextDisplayProjection(IEnumerable<RichTextDisplayReservation> reservations)
    {
        Reservations = [.. reservations.OrderBy(static item => item.DisplayStart)];
        _sourcePositions = new int[Reservations.Length];
        _displayStarts = new int[Reservations.Length];
        _prefixLengths = new int[Reservations.Length + 1];
        for (var index = 0; index < Reservations.Length; index++)
        {
            var item = Reservations[index];
            _sourcePositions[index] = item.SourcePosition;
            _displayStarts[index] = item.DisplayStart;
            _prefixLengths[index + 1] = _prefixLengths[index] + item.Text.Length;
        }
    }

    private static int Bound(int[] values, int position, bool after)
    {
        var low = 0;
        var high = values.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] < position || after && values[middle] == position) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    internal int ToDisplay(int position, bool after = true) => checked(position + _prefixLengths[Bound(_sourcePositions, position, after)]);

    internal int ToSource(int position)
    {
        var index = Bound(_displayStarts, position, false) - 1;
        return index < 0 ? Math.Max(0, position) : position - _prefixLengths[index] - Math.Min(position - Reservations[index].DisplayStart, Reservations[index].Text.Length);
    }

    internal int ToDisplayCaret(int position, bool afterInline = true)
    {
        var result = ToDisplay(position, false);
        for (var index = Bound(_sourcePositions, position, false); index < Reservations.Length; index++)
        {
            var item = Reservations[index];
            if (item.SourcePosition > position) break;
            if (item.Adornment.Options.Placement == RichTextAdornmentPlacement.AboveLine ||
                afterInline && item.Adornment.Options.Placement == RichTextAdornmentPlacement.Inline) result += item.Text.Length;
        }
        return result;
    }

    internal RichTextSelectionState ToDisplay(RichTextSelectionState selection)
    {
        if (selection.Range.IsEmpty) { var position = ToDisplayCaret(selection.Active); return new(position, position); }
        var range = ToDisplay(selection.Range);
        return selection.IsReversed ? new(range.End, range.Start) : new(range.Start, range.End);
    }
    internal RichTextSelectionState ToSource(RichTextSelectionState selection) => new(ToSource(selection.Anchor), ToSource(selection.Active));
    internal RichTextRange ToDisplay(RichTextRange range)
    {
        if (range.IsEmpty) return new(ToDisplayCaret(range.Start), 0);
        var start = ToDisplay(range.Start);
        return new(start, Math.Max(0, ToDisplay(range.End, false) - start));
    }

    internal bool ContainsDisplayCharacter(int position) => Bound(_displayStarts, position, true) - 1 is var index && index >= 0 && position < Reservations[index].DisplayEnd;

    internal string SourceText(string displayText, int displayStart = 0)
    {
        if (IsEmpty) return displayText;
        var result = new StringBuilder(displayText.Length);
        var end = displayStart + displayText.Length;
        var position = displayStart;
        for (var index = Math.Max(0, Bound(_displayStarts, displayStart, true) - 1); index < Reservations.Length; index++)
        {
            var item = Reservations[index];
            if (item.DisplayEnd <= position) continue;
            if (item.DisplayStart >= end) break;
            if (item.DisplayStart > position) result.Append(displayText, position - displayStart, item.DisplayStart - position);
            position = Math.Min(end, item.DisplayEnd);
        }
        if (position < end) result.Append(displayText, position - displayStart, end - position);
        return result.ToString();
    }

    internal static RichTextDisplayProjection Create(RichEditor editor, double viewportWidth, ISet<RichTextAdornment>? included = null)
    {
        var source = editor.Document.Text;
        var candidates = new List<(RichTextAdornment Item, int Position, string Text, int Order)>();
        foreach (var item in editor.Adornments)
        {
            if (!item.ReservesSpace || !item.IsValid || included is not null && !included.Contains(item)) continue;
            var position = item.Position;
            var text = "\uFFFC";
            var order = 1;
            switch (item.Options.Placement)
            {
                case RichTextAdornmentPlacement.AboveLine:
                    position = position == 0 ? 0 : source.LastIndexOf('\n', position - 1) + 1;
                    text = "\uFFFC\n";
                    order = 0;
                    break;
                case RichTextAdornmentPlacement.BelowLine:
                    var newline = source.IndexOf('\n', position);
                    position = newline < 0 ? source.Length : newline;
                    text = "\n\uFFFC";
                    order = 2;
                    break;
                case RichTextAdornmentPlacement.Inline when item.MeasuredSize.Width > viewportWidth:
                    text = (position > 0 && source[position - 1] != '\n' ? "\n" : "") + "\uFFFC" +
                        (position < source.Length && source[position] != '\n' ? "\n" : "");
                    break;
            }
            if (editor.Folding.FindRange(position) is not null) continue;
            candidates.Add((item, position, text, order));
        }
        var shift = 0;
        var entries = new List<RichTextDisplayReservation>();
        foreach (var (item, position, text, _) in candidates.OrderBy(static item => item.Position).ThenBy(static item => item.Order))
        {
            entries.Add(new(item, position, position + shift, text));
            shift += text.Length;
        }
        return new(entries);
    }

    internal RichTextDocumentSnapshot Project(RichTextDocumentSnapshot snapshot, double viewportWidth)
    {
        if (IsEmpty) return snapshot;
        var text = new StringBuilder(snapshot.Length + Reservations.Sum(static item => item.Text.Length));
        var previous = 0;
        foreach (var item in Reservations)
        {
            text.Append(snapshot.Text, previous, item.SourcePosition - previous);
            text.Append(item.Text);
            previous = item.SourcePosition;
        }
        text.Append(snapshot.Text, previous, snapshot.Length - previous);
        var projectedText = text.ToString();
        var runs = new List<RichTextRun>();
        foreach (var run in snapshot.Runs)
        {
            var start = run.Start;
            for (var index = Bound(_sourcePositions, start, false); index < Reservations.Length; index++)
            {
                var item = Reservations[index];
                if (item.SourcePosition >= run.End) break;
                if (item.SourcePosition > start) runs.Add(new(ToDisplay(start), item.SourcePosition - start, run.Format));
                start = item.SourcePosition;
            }
            if (start < run.End) runs.Add(new(ToDisplay(start), run.End - start, run.Format));
        }
        foreach (var item in Reservations)
        {
            var format = snapshot.GetCaretFormat(item.SourcePosition);
            runs.Add(new(item.DisplayStart, item.Text.Length, format.Hidden ? format with { Hidden = false } : format));
        }
        var paragraphs = new List<RichTextParagraph>();
        for (var start = 0; start <= projectedText.Length;)
        {
            var end = projectedText.IndexOf('\n', start);
            if (end < 0) end = projectedText.Length;
            var isReservationRow = end > start && ToSource(start) == ToSource(end);
            paragraphs.Add(new(start, isReservationRow ? RichTextParagraphFormat.Default : snapshot.GetParagraphFormat(ToSource(start))));
            if (end == projectedText.Length) break;
            start = end + 1;
        }
        var images = snapshot.Images.Select(image => image with { Position = ToDisplay(image.Position) }).ToList();
        foreach (var item in Reservations.Where(static item => item.ImagePosition >= 0))
            images.Add(new()
            {
                Position = item.ImagePosition,
                Width = Math.Max(1, item.Adornment.Options.Placement == RichTextAdornmentPlacement.Inline
                    ? Math.Min(item.Adornment.MeasuredSize.Width, viewportWidth) : viewportWidth),
                Height = Math.Max(1, item.Adornment.MeasuredSize.Height),
                Adornment = item.Adornment,
                AdornmentBaseline = item.Adornment.Options.Baseline,
                Data = TransparentPixel,
            });
        return snapshot.With(text: projectedText, runs: runs, paragraphs: paragraphs,
            images: images, links: snapshot.Links.Select(link => link with { Start = ToDisplay(link.Start), Length = ToDisplay(link.End, false) - ToDisplay(link.Start) }),
            fields: snapshot.Fields.Select(field => field with { Start = ToDisplay(field.Start), Length = ToDisplay(field.End, false) - ToDisplay(field.Start) })).WithVersion(snapshot.Version);
    }

    internal RichTextDocumentSnapshot Unproject(RichTextDocumentSnapshot snapshot)
    {
        if (IsEmpty) return snapshot;
        var text = new StringBuilder(snapshot.Length);
        var previous = 0;
        foreach (var item in Reservations)
        {
            text.Append(snapshot.Text, previous, item.DisplayStart - previous);
            previous = item.DisplayEnd;
        }
        text.Append(snapshot.Text, previous, snapshot.Length - previous);
        var source = text.ToString();
        var runs = new List<RichTextRun>();
        foreach (var run in snapshot.Runs)
        {
            var start = run.Start;
            for (var index = Math.Max(0, Bound(_displayStarts, start, true) - 1); index < Reservations.Length; index++)
            {
                var item = Reservations[index];
                if (item.DisplayEnd <= start) continue;
                if (item.DisplayStart >= run.End) break;
                if (item.DisplayStart > start) runs.Add(new(ToSource(start), item.DisplayStart - start, run.Format));
                start = Math.Min(run.End, item.DisplayEnd);
            }
            if (start < run.End) runs.Add(new(ToSource(start), run.End - start, run.Format));
        }
        var paragraphs = new List<RichTextParagraph>();
        for (var start = 0; start <= source.Length;)
        {
            paragraphs.Add(new(start, snapshot.GetParagraphFormat(ToDisplay(start))));
            var newline = source.IndexOf('\n', start);
            if (newline < 0) break;
            start = newline + 1;
        }
        return snapshot.With(text: source, runs: runs, paragraphs: paragraphs,
            images: snapshot.Images.Where(image => !ContainsDisplayCharacter(image.Position)).Select(image => image with { Position = ToSource(image.Position) }),
            links: snapshot.Links.Select(link => link with { Start = ToSource(link.Start), Length = ToSource(link.End) - ToSource(link.Start) }).Where(static link => link.Length > 0),
            fields: snapshot.Fields.Select(field => field with { Start = ToSource(field.Start), Length = ToSource(field.End) - ToSource(field.Start) }));
    }

    // Native spans/ranges have already tracked the change; rebuild source positions from those ranges.
    internal static RichTextDisplayProjection FromNative(IEnumerable<(RichTextDisplayMarker Marker, int Start, int Length)> spans, string text)
    {
        var shift = 0;
        var entries = new List<RichTextDisplayReservation>();
        foreach (var (item, start, length) in spans.OrderBy(static span => span.Start))
        {
            if (start < 0 || start >= text.Length || length != 1 || text[start] != item.Character) continue;
            entries.Add(new(item.Adornment, start - shift, start, item.Character.ToString()));
            shift += length;
        }
        return new(entries);
    }
}
