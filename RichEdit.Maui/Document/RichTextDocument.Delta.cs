namespace RichEdit.Maui;

public sealed partial class RichTextDocument
{
    internal static IReadOnlyList<RichTextChange> CreateDelta(
        RichTextDocumentSnapshot before,
        RichTextDocumentSnapshot after)
    {
        var sourceOnly = before.IsSourceProjection && after.IsSourceProjection &&
            before.Images.IsEmpty && after.Images.IsEmpty &&
            before.Runs.All(run => run.Format == before.DefaultCharacterFormat) &&
            after.Runs.All(run => run.Format == after.DefaultCharacterFormat);
        if (ReferenceEquals(before, after) || sourceOnly && ReferenceEquals(before.TextBuffer, after.TextBuffer) ||
            !before.IsSourceProjection && before.ContentEquals(after))
        {
            return [];
        }

        var changes = new List<RichTextChange>();
        RichTextTextChange? textChange = null;
        if (before.TextBuffer.FindDifference(after.TextBuffer, out _) is { } difference)
        {
            textChange = new RichTextTextChange(
                difference.Before,
                after.GetText(difference.After));
            changes.Add(textChange);
        }

        if (sourceOnly) return changes;

        if (FindRunDifference(after, before, textChange) is { } characterRange)
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.CharacterFormat,
                MapAffectedRangeToBefore(characterRange, textChange),
                characterRange));
        }

        if (FindParagraphDifference(after, before, textChange) is { } paragraphRange)
        {
            var newRange = ExpandToParagraphs(after.TextBuffer, paragraphRange);
            var oldRange = ExpandToParagraphs(before.TextBuffer, MapAffectedRangeToBefore(newRange, textChange));
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.ParagraphFormat,
                oldRange,
                newRange));
        }

        if (before.DefaultCharacterFormat != after.DefaultCharacterFormat ||
            before.DefaultParagraphFormat != after.DefaultParagraphFormat)
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.DefaultFormat,
                new TextSpan(0, before.Length),
                new TextSpan(0, after.Length)));
        }

        AddSemanticDelta(changes, RichTextChangeKind.Link, before.Links, after.Links,
            static link => link.Range, before.Length, after.Length);
        AddSemanticDelta(changes, RichTextChangeKind.Field, before.Fields, after.Fields,
            static field => field.Range, before.Length, after.Length);
        AddSemanticDelta(changes, RichTextChangeKind.Image, before.Images, after.Images,
            static image => new TextSpan(image.Position, 1), before.Length, after.Length);

        if (!DictionaryEqual(before.Lists, after.Lists) ||
            !DictionaryEqual(before.ListPictures, after.ListPictures))
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.List,
                new TextSpan(0, before.Length),
                new TextSpan(0, after.Length)));
        }

        if (!DictionaryEqual(before.Metadata, after.Metadata))
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.Metadata,
                new TextSpan(0, before.Length),
                new TextSpan(0, after.Length)));
        }

        return changes;
    }

    private static TextSpan? FindRunDifference(
        RichTextDocumentSnapshot first,
        RichTextDocumentSnapshot second,
        RichTextTextChange? textChange)
    {
        TextSpan? affected = textChange?.NewRange;
        var firstIndex = 0;
        var secondIndex = 0;
        var position = 0;
        while (position < first.Length)
        {
            if (textChange is not null &&
                position >= textChange.NewRange.Start && position < textChange.NewRange.End)
            {
                position = textChange.NewRange.End;
                continue;
            }

            var offset = textChange is not null && position >= textChange.NewRange.End
                ? textChange.NewRange.Length - textChange.OldRange.Length
                : 0;
            var oldPosition = position - offset;
            while (first.Runs[firstIndex].End <= position)
            {
                firstIndex++;
            }

            while (second.Runs[secondIndex].End <= oldPosition)
            {
                secondIndex++;
            }

            var firstRun = first.Runs[firstIndex];
            var secondRun = second.Runs[secondIndex];
            var end = Math.Min(firstRun.End, secondRun.End + offset);
            if (textChange is not null && position < textChange.NewRange.Start)
            {
                end = Math.Min(end, textChange.NewRange.Start);
            }

            if (firstRun.Format != secondRun.Format)
            {
                affected = UnionRanges(affected, new TextSpan(position, end - position));
            }

            position = end;
        }

        return affected;
    }

    private static TextSpan? FindParagraphDifference(
        RichTextDocumentSnapshot first,
        RichTextDocumentSnapshot second,
        RichTextTextChange? textChange)
    {
        TextSpan? affected = textChange?.NewRange;
        if (first.IsSourceProjection && second.IsSourceProjection && first.DefaultParagraphFormat == second.DefaultParagraphFormat) return affected;
        foreach (var paragraph in first.Paragraphs)
        {
            var oldPosition = paragraph.Start;
            if (textChange is not null && oldPosition >= textChange.NewRange.Start)
            {
                oldPosition = oldPosition >= textChange.NewRange.End
                    ? oldPosition + textChange.OldRange.Length - textChange.NewRange.Length
                    : textChange.OldRange.Start;
            }

            if (second.GetParagraphFormat(oldPosition) != paragraph.Format)
            {
                affected = UnionRanges(affected, paragraph.Range);
            }
        }

        return affected;
    }

    private static TextSpan UnionRanges(TextSpan? first, TextSpan second)
    {
        var start = Math.Min(first?.Start ?? second.Start, second.Start);
        var end = Math.Max(first?.End ?? second.End, second.End);
        return new TextSpan(start, end - start);
    }

    // Format ranges include the entire text replacement, so mapping them back
    // only needs to undo its length change.
    private static TextSpan MapAffectedRangeToBefore(TextSpan range, RichTextTextChange? textChange) =>
        textChange is null
            ? range
            : new TextSpan(range.Start, range.Length + textChange.OldRange.Length - textChange.NewRange.Length);

    private static TextSpan ExpandToParagraphs(TextSnapshot text, TextSpan range)
    {
        var start = text.GetLine(text.GetPosition(range.Start).Line).Span.Start;
        var last = text.GetLine(text.GetPosition(Math.Clamp(range.End, 0, text.Length)).Line);
        var end = last.Span.End + last.LineBreakLength;
        return new TextSpan(start, end - start);
    }

    private static void AddSemanticDelta<T>(
        List<RichTextChange> changes,
        RichTextChangeKind kind,
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        Func<T, TextSpan> getRange,
        int beforeLength,
        int afterLength)
    {
        if (before.SequenceEqual(after))
        {
            return;
        }

        changes.Add(new RichTextRangeChange(
            kind,
            UnionRanges(before, getRange, beforeLength),
            UnionRanges(after, getRange, afterLength)));
    }

    private static TextSpan UnionRanges<T>(
        IReadOnlyList<T> values,
        Func<T, TextSpan> getRange,
        int documentLength)
    {
        if (values.Count == 0)
        {
            return new TextSpan(0, 0);
        }

        var start = values.Min(value => getRange(value).Start);
        var end = values.Max(value => getRange(value).End);
        return new TextSpan(start, Math.Min(end, documentLength) - start);
    }

    private static bool DictionaryEqual<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> first,
        IReadOnlyDictionary<TKey, TValue> second)
        where TKey : notnull =>
            first.Count == second.Count &&
            first.All(pair => second.TryGetValue(pair.Key, out var value) &&
                EqualityComparer<TValue>.Default.Equals(pair.Value, value));
}
