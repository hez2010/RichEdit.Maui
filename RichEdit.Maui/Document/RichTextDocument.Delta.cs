namespace RichEdit.Maui;

public sealed partial class RichTextDocument
{
    internal static IReadOnlyList<RichTextChange> CreateDelta(
        RichTextDocumentSnapshot before,
        RichTextDocumentSnapshot after)
    {
        if (before.ContentEquals(after))
        {
            return [];
        }

        var changes = new List<RichTextChange>();
        RichTextTextChange? textChange = null;
        if (!string.Equals(before.Text, after.Text, StringComparison.Ordinal))
        {
            var prefixLength = before.Text.AsSpan().CommonPrefixLength(after.Text);
            var suffixLength = 0;
            var maximumSuffixLength = Math.Min(before.Length, after.Length) - prefixLength;
            while (suffixLength < maximumSuffixLength &&
                   before.Text[^(suffixLength + 1)] == after.Text[^(suffixLength + 1)])
            {
                suffixLength++;
            }

            var textOldRange = new RichTextRange(
                prefixLength,
                before.Length - prefixLength - suffixLength);
            var textNewRange = new RichTextRange(
                prefixLength,
                after.Length - prefixLength - suffixLength);
            textChange = new RichTextTextChange(
                textOldRange,
                after.Text.Substring(textNewRange.Start, textNewRange.Length));
            changes.Add(textChange);
        }

        if (FindRunDifference(after, before, textChange) is { } characterRange)
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.CharacterFormat,
                MapAffectedRangeToBefore(characterRange, textChange),
                characterRange));
        }

        if (FindParagraphDifference(after, before, textChange) is { } paragraphRange)
        {
            var newRange = ExpandToParagraphs(after.Text, paragraphRange);
            var oldRange = ExpandToParagraphs(before.Text, MapAffectedRangeToBefore(newRange, textChange));
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
                new RichTextRange(0, before.Length),
                new RichTextRange(0, after.Length)));
        }

        AddSemanticDelta(changes, RichTextChangeKind.Link, before.Links, after.Links,
            static link => link.Range, before.Length, after.Length);
        AddSemanticDelta(changes, RichTextChangeKind.Field, before.Fields, after.Fields,
            static field => field.Range, before.Length, after.Length);
        AddSemanticDelta(changes, RichTextChangeKind.Image, before.Images, after.Images,
            static image => new RichTextRange(image.Position, 1), before.Length, after.Length);

        if (!DictionaryEqual(before.Lists, after.Lists) ||
            !DictionaryEqual(before.ListPictures, after.ListPictures))
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.List,
                new RichTextRange(0, before.Length),
                new RichTextRange(0, after.Length)));
        }

        if (!DictionaryEqual(before.Metadata, after.Metadata))
        {
            changes.Add(new RichTextRangeChange(
                RichTextChangeKind.Metadata,
                new RichTextRange(0, before.Length),
                new RichTextRange(0, after.Length)));
        }

        return changes;
    }

    private static RichTextRange? FindRunDifference(
        RichTextDocumentSnapshot first,
        RichTextDocumentSnapshot second,
        RichTextTextChange? textChange)
    {
        RichTextRange? affected = textChange?.NewRange;
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
                affected = UnionRanges(affected, new RichTextRange(position, end - position));
            }

            position = end;
        }

        return affected;
    }

    private static RichTextRange? FindParagraphDifference(
        RichTextDocumentSnapshot first,
        RichTextDocumentSnapshot second,
        RichTextTextChange? textChange)
    {
        RichTextRange? affected = textChange?.NewRange;
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

    private static RichTextRange UnionRanges(RichTextRange? first, RichTextRange second)
    {
        var start = Math.Min(first?.Start ?? second.Start, second.Start);
        var end = Math.Max(first?.End ?? second.End, second.End);
        return new RichTextRange(start, end - start);
    }

    // Format ranges include the entire text replacement, so mapping them back
    // only needs to undo its length change.
    private static RichTextRange MapAffectedRangeToBefore(RichTextRange range, RichTextTextChange? textChange) =>
        textChange is null
            ? range
            : new RichTextRange(range.Start, range.Length + textChange.OldRange.Length - textChange.NewRange.Length);

    private static RichTextRange ExpandToParagraphs(string text, RichTextRange range)
    {
        var start = range.Start == 0 ? 0 : text.LastIndexOf('\n', range.Start - 1) + 1;
        var inspectedEnd = Math.Clamp(range.End, 0, text.Length);
        var newline = text.IndexOf('\n', inspectedEnd);
        var end = newline < 0 ? text.Length : newline + 1;
        return new RichTextRange(start, end - start);
    }

    private static void AddSemanticDelta<T>(
        List<RichTextChange> changes,
        RichTextChangeKind kind,
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        Func<T, RichTextRange> getRange,
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

    private static RichTextRange UnionRanges<T>(
        IReadOnlyList<T> values,
        Func<T, RichTextRange> getRange,
        int documentLength)
    {
        if (values.Count == 0)
        {
            return new RichTextRange(0, 0);
        }

        var start = values.Min(value => getRange(value).Start);
        var end = values.Max(value => getRange(value).End);
        return new RichTextRange(start, Math.Min(end, documentLength) - start);
    }

    private static bool DictionaryEqual<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> first,
        IReadOnlyDictionary<TKey, TValue> second)
        where TKey : notnull =>
            first.Count == second.Count &&
            first.All(pair => second.TryGetValue(pair.Key, out var value) &&
                EqualityComparer<TValue>.Default.Equals(pair.Value, value));
}
