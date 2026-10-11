using System.Collections.Immutable;
using System.Text;

namespace RichEdit.Maui;

// Native reservations are identified by an object owned by the handler, never by their text.
// A literal U+FFFC, authored image, or source newline is therefore never removed by this map.
internal sealed record RichTextDisplayReservation(RichTextAdornment Adornment, int SourcePosition, int DisplayStart, string Text)
{
    internal int DisplayEnd => DisplayStart + Text.Length;

    internal int ImagePosition => Text.IndexOfAny(['\uFFFC', '\u00A0']) is var offset && offset >= 0 ? DisplayStart + offset : -1;
}

// Native spans and ranges track positions. Their payload only identifies a private
// character and its application view; it does not retain obsolete source offsets.
internal readonly record struct RichTextDisplayMarker(RichTextAdornment Adornment, char Character);

internal sealed partial class RichTextDisplayProjection
{
    internal static ImmutableArray<byte> TransparentPixel { get; } = [.. Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=")];
    internal static RichTextDisplayProjection Empty { get; } = new([]);
    internal ImmutableArray<RichTextDisplayReservation> Reservations { get; }

    internal bool IsEmpty => Reservations.IsEmpty && _lineEndings is null && _structured is null;

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
            if (values[middle] < position || after && values[middle] == position)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    internal int ToDisplay(int position, bool after = true) => _structured?.FromSource(position, after) ?? checked((_lineEndings?.ToDisplay(position, after) ?? position) + _prefixLengths[Bound(_sourcePositions, position, after)]);

    internal int ToSource(int position)
    {
        if (_structured is { } rich) return rich.ToSource(position);
        var index = Bound(_displayStarts, position, false) - 1;
        var offset = index < 0 ? Math.Max(0, position) : position - _prefixLengths[index] - Math.Min(position - Reservations[index].DisplayStart, Reservations[index].Text.Length);
        return _lineEndings?.ToSource(Math.Clamp(offset, 0, _lineEndings.Display.Length)) ?? offset;
    }

    internal int ToDisplayCaret(int position, bool afterInline = true)
    {
        if (_structured is { } rich) return rich.FromSource(position, afterInline);
        var result = ToDisplay(position, false);
        for (var index = Bound(_sourcePositions, position, false); index < Reservations.Length; index++)
        {
            var item = Reservations[index];
            if (item.SourcePosition > position)
                break;
            if (item.Adornment.Options.Placement == RichTextAdornmentPlacement.AboveLine ||
                afterInline && item.Adornment.Options.Placement == RichTextAdornmentPlacement.Inline)
                result += item.Text.Length;
        }

        return result;
    }

    internal TextSelectionState ToDisplay(TextSelectionState selection)
    {
        if (selection.Range.IsEmpty)
        {
            var position = ToDisplayCaret(selection.Active);
            return new(position, position);
        }

        var range = ToDisplay(selection.Range);
        return selection.IsReversed ? new(range.End, range.Start) : new(range.Start, range.End);
    }

    internal TextSelectionState ToSource(TextSelectionState selection) => new(ToSource(selection.Anchor), ToSource(selection.Active));

    internal TextSpan ToDisplay(TextSpan range)
    {
        if (range.IsEmpty)
            return new(ToDisplayCaret(range.Start), 0);

        var start = ToDisplay(range.Start);
        return new(start, Math.Max(0, ToDisplay(range.End, false) - start));
    }

    internal bool ContainsDisplayCharacter(int position) => Bound(_displayStarts, position, true) - 1 is var index && index >= 0 && position < Reservations[index].DisplayEnd;

    internal string SourceText(string displayText, int displayStart = 0)
    {
        if (_structured is { } rich)
        {
            var sourceStart = rich.ToSource(displayStart);
            var sourceEnd = rich.ToSource(displayStart + displayText.Length);
            return rich.TextProjection.Text.GetText(new(sourceStart, Math.Max(0, sourceEnd - sourceStart)));
        }
        if (_lineEndings is not null)
        {
            var start = ToSource(displayStart);
            return _lineEndings.Source.GetText(new(start, ToSource(displayStart + displayText.Length) - start));
        }
        if (IsEmpty)
            return displayText;

        var result = new StringBuilder(displayText.Length);
        var end = displayStart + displayText.Length;
        var position = displayStart;
        for (var index = Math.Max(0, Bound(_displayStarts, displayStart, true) - 1); index < Reservations.Length; index++)
        {
            var item = Reservations[index];
            if (item.DisplayEnd <= position)
                continue;
            if (item.DisplayStart >= end)
                break;
            if (item.DisplayStart > position)
                result.Append(displayText, position - displayStart, item.DisplayStart - position);

            position = Math.Min(end, item.DisplayEnd);
        }
        if (position < end)
            result.Append(displayText, position - displayStart, end - position);

        return result.ToString();
    }

    internal static RichTextDisplayProjection Create(NativeEditorView editor, double viewportWidth, ISet<RichTextAdornment>? included = null)
    {
        var lineEndings = editor.SourceSession is null ? null : ReferenceEquals(editor.InputSnapshot.TextBuffer, editor.Document.SourceLineEndings?.Source)
            ? editor.Document.SourceLineEndings : new LfTextProjection(editor.InputSnapshot.TextBuffer);
        var source = lineEndings?.Display ?? editor.Document.CurrentSnapshot.TextBuffer;
        var candidates = new List<(RichTextAdornment Item, int Position, string Text, int Order)>();
        foreach (var item in editor.Adornments)
        {
            if (!item.ReservesSpace || !item.IsValid || included is not null && !included.Contains(item))
                continue;

            var position = lineEndings?.ToDisplay(item.Position) ?? item.Position;
            if (editor.Folding.FindRange(lineEndings?.ToSource(position) ?? position) is not null)
                continue;

            var glyph = "\uFFFC";
#if WINDOWS
            if (editor.Handler is RichEditorHandler handler && handler.CanUseTextReservation(item)) glyph = "\u00A0";
#endif
            var text = glyph;
            var order = 1;
            switch (item.Options.Placement)
            {
                case RichTextAdornmentPlacement.AboveLine:
                    position = source.GetLine(source.GetPosition(position).Line).Span.Start;
                    text = glyph + "\n";
                    order = 0;
                    break;
                case RichTextAdornmentPlacement.BelowLine:
                    position = source.GetLine(source.GetPosition(position).Line).Span.End;
                    text = "\n" + glyph;
                    order = 2;
                    break;
                case RichTextAdornmentPlacement.Inline when item.MeasuredSize.Width > viewportWidth:
                    text = (position > 0 && source[position - 1] != '\n' ? "\n" : "") + glyph +
                        (position < source.Length && source[position] != '\n' ? "\n" : "");
                    break;
            }
            if (editor.Folding.FindRange(lineEndings?.ToSource(position) ?? position) is not null)
                continue;

            candidates.Add((item, position, text, order));
        }

        var shift = 0;
        var entries = new List<RichTextDisplayReservation>();
        foreach (var (item, position, text, _) in candidates.OrderBy(static item => item.Position).ThenBy(static item => item.Order))
        {
            entries.Add(new(item, position, position + shift, text));
            shift += text.Length;
        }

        return lineEndings is null ? new(entries) : new RichTextDisplayProjection(entries).WithLineEndings(lineEndings);
    }

    internal RichTextDocumentSnapshot Project(RichTextDocumentSnapshot snapshot, double viewportWidth, TextSnapshot? projectedBuffer = null)
    {
        if (_structured is { } rich) return NativeDescription(rich);
        if (_lineEndings is not null)
        {
            var normalized = RichTextDocumentSnapshot.CreateSourceProjection(_lineEndings.Display)
                .With(runs: snapshot.Runs.Select(run =>
                {
                    var range = _lineEndings.ToDisplay(new TextSpan(run.Start, run.Length));
                    return new RichTextRun(range.Start, range.Length, run.Format);
                }).Where(run => run.Length > 0)).WithVersion(snapshot.Version);
            return WithoutLineEndings().Project(normalized, viewportWidth, projectedBuffer);
        }
        if (IsEmpty)
            return snapshot;

        projectedBuffer ??= ProjectText(snapshot.TextBuffer);
        var runs = new List<RichTextRun>();
        foreach (var run in snapshot.Runs)
        {
            var start = run.Start;
            for (var index = Bound(_sourcePositions, start, false); index < Reservations.Length; index++)
            {
                var item = Reservations[index];
                if (item.SourcePosition >= run.End)
                    break;
                if (item.SourcePosition > start)
                    runs.Add(new(ToDisplay(start), item.SourcePosition - start, run.Format));

                start = item.SourcePosition;
            }
            if (start < run.End)
                runs.Add(new(ToDisplay(start), run.End - start, run.Format));
        }

        foreach (var item in Reservations)
        {
            var format = snapshot.GetCaretFormat(item.SourcePosition);
#if WINDOWS
            if (item.Text.Contains('\u00A0')) format = RichEditorHandler.TextReservationFormat(item.Adornment, viewportWidth);
#endif
            runs.Add(new(item.DisplayStart, item.Text.Length, format.Hidden ? format with { Hidden = false } : format));
        }

        var paragraphs = new List<RichTextParagraph>();
        // Source paragraphs all use the same defaults, including reserved rows.
        // Keep their line index in the rope instead of materializing a full array.
        if (!snapshot.IsSourceProjection)
        foreach (var line in projectedBuffer.GetLines())
        {
            var span = line.Span;
            var start = span.Start;
            var end = span.End;
            var isReservationRow = end > start && ToSource(start) == ToSource(end);
            paragraphs.Add(new(start, isReservationRow ? RichTextParagraphFormat.Default : snapshot.GetParagraphFormat(ToSource(start))));
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

        if (snapshot.IsSourceProjection)
            return RichTextDocumentSnapshot.CreateSourceDisplay(projectedBuffer, runs, images).WithVersion(snapshot.Version);
        return snapshot.With(textBuffer: projectedBuffer, runs: runs, paragraphs: paragraphs,
            images: images, links: snapshot.Links.Select(link => link with
        {
            Start = ToDisplay(link.Start),
            Length = ToDisplay(link.End, false) - ToDisplay(link.Start)
        }),
            fields: snapshot.Fields.Select(field => field with
        {
            Range = ToDisplay(field.Range)
        })).WithVersion(snapshot.Version);
    }

    internal RichTextDocumentSnapshot Unproject(RichTextDocumentSnapshot snapshot)
    {
        if (_structured is { } rich) return RichTextDocumentSnapshot.CreateStructuredProjection(rich.TextProjection);
        if (_lineEndings is not null)
        {
            var normalized = WithoutLineEndings().Unproject(snapshot);
            return RichTextDocumentSnapshot.CreateSourceProjection(_lineEndings.Source).With(runs: normalized.Runs.Select(run =>
            {
                var start = _lineEndings.ToSource(run.Start);
                return new RichTextRun(start, _lineEndings.ToSource(run.End) - start, run.Format);
            }));
        }
        if (IsEmpty)
            return snapshot;

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
                if (item.DisplayEnd <= start)
                    continue;
                if (item.DisplayStart >= run.End)
                    break;
                if (item.DisplayStart > start)
                    runs.Add(new(ToSource(start), item.DisplayStart - start, run.Format));

                start = Math.Min(run.End, item.DisplayEnd);
            }
            if (start < run.End)
                runs.Add(new(ToSource(start), run.End - start, run.Format));
        }

        var paragraphs = new List<RichTextParagraph>();
        for (var start = 0; start <= source.Length;)
        {
            paragraphs.Add(new(start, snapshot.GetParagraphFormat(ToDisplay(start))));
            var newline = source.IndexOf('\n', start);
            if (newline < 0)
                break;

            start = newline + 1;
        }

        return snapshot.With(text: source, runs: runs, paragraphs: paragraphs,
            images: snapshot.Images.Where(image => !ContainsDisplayCharacter(image.Position)).Select(image => image with { Position = ToSource(image.Position) }),
            links: snapshot.Links.Select(link => link with
        {
            Start = ToSource(link.Start),
            Length = ToSource(link.End) - ToSource(link.Start)
        }).Where(static link => link.Length > 0),
            fields: snapshot.Fields.Select(field => field with { Start = ToSource(field.Start), Length = ToSource(field.End) - ToSource(field.Start) }));
    }

    // Native spans/ranges have already tracked the change; rebuild source positions from those ranges.
    internal static RichTextDisplayProjection FromNative(IEnumerable<(RichTextDisplayMarker Marker, int Start, int Length)> spans, string text)
    {
        var shift = 0;
        var entries = new List<RichTextDisplayReservation>();
        foreach (var (item, start, length) in spans.OrderBy(static span => span.Start))
        {
            if (start < 0 || start >= text.Length || length != 1 || text[start] != item.Character)
                continue;

            if (entries.Count != 0 && ReferenceEquals(entries[^1].Adornment, item.Adornment) && entries[^1].DisplayEnd == start)
                entries[^1] = entries[^1] with { Text = entries[^1].Text + item.Character };
            else entries.Add(new(item.Adornment, start - shift, start, item.Character.ToString()));
            shift += length;
        }

        return new(entries);
    }
}
