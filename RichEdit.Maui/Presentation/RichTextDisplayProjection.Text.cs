namespace RichEdit.Maui;

internal sealed partial class RichTextDisplayProjection
{
    internal TextSnapshot ProjectText(TextSnapshot source, RichTextDisplayProjection? previous = null,
        TextSnapshot? previousSource = null, TextSnapshot? previousDisplay = null)
    {
        if (_lineEndings is not null)
            return WithoutLineEndings().ProjectText(_lineEndings.Display, previous?.WithoutLineEndings(),
                previous?._lineEndings?.Display ?? previousSource, previousDisplay);
        if (Reservations.IsEmpty) return source;
        if (previous is null || previousSource is null || previousDisplay is null || previous._structured is not null ||
            previousDisplay.Length != (long)previousSource.Length + previous._prefixLengths[^1])
            return BuildText(source, 0, source.Length);

        var difference = previousSource.FindDifference(source, out _);
        var oldStart = difference?.Before.Start ?? previousSource.Length;
        var oldEnd = difference?.Before.End ?? 0;
        var newStart = difference?.After.Start ?? source.Length;
        var newEnd = difference?.After.End ?? 0;
        var prefix = 0;
        while (prefix < previous.Reservations.Length && prefix < Reservations.Length && Same(previous.Reservations[prefix], Reservations[prefix])) prefix++;
        var oldLast = previous.Reservations.Length;
        var newLast = Reservations.Length;
        while (oldLast > prefix && newLast > prefix && Same(previous.Reservations[oldLast - 1], Reservations[newLast - 1])) { oldLast--; newLast--; }
        if (difference is null && prefix == oldLast && prefix == newLast) return previousDisplay;
        for (var index = prefix; index < oldLast; index++)
        {
            var position = previous.Reservations[index].SourcePosition;
            oldStart = Math.Min(oldStart, position); oldEnd = Math.Max(oldEnd, position);
            var mapped = Map(position, inverse: false);
            newStart = Math.Min(newStart, mapped.Start); newEnd = Math.Max(newEnd, mapped.End);
        }
        for (var index = prefix; index < newLast; index++)
        {
            var position = Reservations[index].SourcePosition;
            newStart = Math.Min(newStart, position); newEnd = Math.Max(newEnd, position);
            var mapped = Map(position, inverse: true);
            oldStart = Math.Min(oldStart, mapped.Start); oldEnd = Math.Max(oldEnd, mapped.End);
        }
        var displayStart = previous.ToDisplay(oldStart, after: false);
        var displayEnd = previous.ToDisplay(oldEnd, after: true);
        var replacement = BuildText(source, newStart, newEnd);
        return previousDisplay.Slice(new(0, displayStart)).Concat(replacement)
            .Concat(previousDisplay.Slice(new(displayEnd, previousDisplay.Length - displayEnd)));

        bool Same(RichTextDisplayReservation before, RichTextDisplayReservation after)
        {
            if (!ReferenceEquals(before.Adornment, after.Adornment) || before.Text != after.Text) return false;
            if (difference is not { } change) return before.SourcePosition == after.SourcePosition;
            if (before.SourcePosition < change.Before.Start) return before.SourcePosition == after.SourcePosition;
            if (before.SourcePosition > change.Before.End) return before.SourcePosition + change.After.Length - change.Before.Length == after.SourcePosition;
            // Reservations touching input can move to either side of it according
            // to affinity, or to another paragraph boundary. Rebuild that region.
            return false;
        }
        TextSpan Map(int position, bool inverse)
        {
            if (difference is not { } change) return new(position, 0);
            var before = inverse ? change.After : change.Before;
            var after = inverse ? change.Before : change.After;
            if (position < before.Start) return new(position, 0);
            if (position > before.End) return new(position + after.Length - before.Length, 0);
            return after;
        }
    }

    private TextSnapshot BuildText(TextSnapshot source, int start, int end)
    {
        var result = TextSnapshot.FromText("");
        var position = start;
        for (var index = Bound(_sourcePositions, start, false); index < Reservations.Length; index++)
        {
            var item = Reservations[index];
            if (item.SourcePosition > end) break;
            if (position < item.SourcePosition) result = result.Concat(source.Slice(new(position, item.SourcePosition - position)));
            result = result.Concat(TextSnapshot.FromText(item.Text));
            position = item.SourcePosition;
        }
        return position < end ? result.Concat(source.Slice(new(position, end - position))) : result;
    }
}
