using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Represents an immutable, enumerable view of one rich-text document version.
/// </summary>
public sealed class RichTextDocumentSnapshot
{
    internal const char ObjectReplacementCharacter = RichTextDocument.ObjectReplacementCharacter;
    internal const char SoftLineBreakCharacter = RichTextDocument.SoftLineBreakCharacter;

    private readonly ImmutableArray<RichTextRun> _runs;
    private readonly ImmutableArray<RichTextParagraph> _paragraphs;
    private readonly ImmutableArray<RichTextLink> _links;
    private readonly ImmutableArray<RichTextField> _fields;
    private readonly ImmutableArray<RichTextImage> _images;
    private string? _cachedRtf;
    private readonly ImmutableDictionary<RichTextListId, RichTextListDefinition> _lists;
    private readonly ImmutableDictionary<string, RichTextListPicture> _listPictures;
    private readonly ImmutableDictionary<string, string> _metadata;

    internal RichTextDocumentSnapshot(
        string? text,
        IEnumerable<RichTextRun>? runs = null,
        IEnumerable<RichTextParagraph>? paragraphs = null,
        IEnumerable<RichTextLink>? links = null,
        IEnumerable<RichTextField>? fields = null,
        IEnumerable<RichTextImage>? images = null,
        RichTextCharacterFormat? defaultCharacterFormat = null,
        RichTextParagraphFormat? defaultParagraphFormat = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null,
        IEnumerable<RichTextListPicture>? listPictures = null,
        IEnumerable<KeyValuePair<RichTextListId, RichTextListDefinition>>? lists = null,
        long version = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        Version = version;
        Text = NormalizeText(text);
        DefaultCharacterFormat = Validate(defaultCharacterFormat ?? RichTextCharacterFormat.Default);
        DefaultParagraphFormat = Validate(defaultParagraphFormat ?? RichTextParagraphFormat.Default);
        _runs = NormalizeRuns(Text.Length, runs, DefaultCharacterFormat);
        _listPictures = NormalizeListPictures(listPictures);
        var normalizedParagraphs = NormalizeParagraphs(Text, paragraphs, DefaultParagraphFormat);
        _lists = NormalizeLists(lists, normalizedParagraphs);
        _paragraphs = BindParagraphLists(normalizedParagraphs, _lists);
        ValidateListPictureReferences(_lists, _listPictures, nameof(listPictures));
        _links = NormalizeLinks(Text.Length, links);
        _fields = NormalizeFields(Text.Length, fields);
        _images = NormalizeImages(Text, images);
        _metadata = metadata?.ToImmutableDictionary(StringComparer.Ordinal) ??
            ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
    }

    /// <summary>Gets the document version captured by this snapshot.</summary>
    public long Version { get; }

    /// <summary>Gets the logical UTF-16 text length.</summary>
    public int Length => Text.Length;

    /// <summary>Gets the complete normalized plain text.</summary>
    public string Text { get; }

    /// <summary>Gets the canonical RTF representation of this snapshot.</summary>
    public string RtfText => _cachedRtf ??= RtfCodec.Serialize(this);

    /// <summary>Gets the normalized character-format runs.</summary>
    public ImmutableArray<RichTextRun> Runs => _runs;

    /// <summary>Gets one normalized entry for every paragraph.</summary>
    public ImmutableArray<RichTextParagraph> Paragraphs => _paragraphs;

    /// <summary>Gets the ordered, non-overlapping hyperlinks.</summary>
    public ImmutableArray<RichTextLink> Links => _links;

    /// <summary>Gets the ordered, non-overlapping fields.</summary>
    public ImmutableArray<RichTextField> Fields => _fields;

    /// <summary>Gets the ordered inline images.</summary>
    public ImmutableArray<RichTextImage> Images => _images;

    /// <summary>Gets reusable document-list definitions by document-local ID.</summary>
    public ImmutableDictionary<RichTextListId, RichTextListDefinition> Lists => _lists;

    /// <summary>Gets owned list-marker pictures by ordinal identifier.</summary>
    public ImmutableDictionary<string, RichTextListPicture> ListPictures => _listPictures;

    /// <summary>Gets application metadata by ordinal key.</summary>
    public ImmutableDictionary<string, string> Metadata => _metadata;

    /// <summary>Gets the declared default character format.</summary>
    public RichTextCharacterFormat DefaultCharacterFormat { get; }

    /// <summary>Gets the declared default paragraph format.</summary>
    public RichTextParagraphFormat DefaultParagraphFormat { get; }

    internal static RichTextDocumentSnapshot FromPlainText(string? text) => new(text);

    internal static RichTextDocumentSnapshot FromRtf(string rtf) => RtfCodec.Parse(rtf);

    internal string ToRtf() => RtfText;

    internal RichTextCharacterFormat GetCharacterFormat(int position)
    {
        if (Text.Length == 0)
        {
            return CreateInheritedCharacterFormat(DefaultCharacterFormat);
        }

        var index = Math.Clamp(position, 0, Text.Length - 1);
        return _runs[FindRunIndex(index)].Format;
    }

    internal RichTextCharacterFormat GetCaretFormat(int position)
    {
        if (Text.Length == 0)
        {
            return CreateInheritedCharacterFormat(DefaultCharacterFormat);
        }

        var index = position == 0 ? 0 : Math.Clamp(position - 1, 0, Text.Length - 1);
        return GetCharacterFormat(index);
    }

    internal RichTextCharacterFormat? GetUniformCharacterFormat(Range range)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        if (length == 0)
        {
            return GetCaretFormat(start);
        }

        var end = start + length;
        RichTextCharacterFormat? result = null;
        for (var runIndex = FindRunIndex(start); runIndex < _runs.Length; runIndex++)
        {
            var run = _runs[runIndex];
            if (run.End <= start)
            {
                continue;
            }

            if (run.Start >= end)
            {
                break;
            }

            if (result is null)
            {
                result = run.Format;
            }
            else if (result != run.Format)
            {
                return null;
            }
        }

        return result ?? CreateInheritedCharacterFormat(DefaultCharacterFormat);
    }

    internal RichTextCharacterFormat ResolveCharacterFormat(
        RichTextCharacterFormat format) =>
        format with
        {
            FontFamily = format.FontFamily ?? DefaultCharacterFormat.FontFamily,
            FontSize = format.FontSize ?? DefaultCharacterFormat.FontSize,
            ForegroundColor = format.ForegroundColor ?? DefaultCharacterFormat.ForegroundColor,
        };

    internal static RichTextCharacterFormat CreateInheritedCharacterFormat(
        RichTextCharacterFormat defaultFormat) =>
        defaultFormat with
        {
            FontFamily = null,
            FontSize = null,
            ForegroundColor = null,
        };

    internal RichTextParagraphFormat GetParagraphFormat(int position)
    {
        var lineStart = GetParagraphStart(Text, Math.Clamp(position, 0, Text.Length));
        return _paragraphs[FindParagraphIndex(lineStart)].Format;
    }

    internal RichTextParagraphFormat? GetUniformParagraphFormat(Range range)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        var lastPosition = length == 0 ? start : start + length - 1;
        var firstParagraph = GetParagraphStart(Text, start);
        var lastParagraph = GetParagraphStart(Text, lastPosition);
        RichTextParagraphFormat? result = null;
        for (var paragraphIndex = FindParagraphIndex(firstParagraph);
             paragraphIndex < _paragraphs.Length;
             paragraphIndex++)
        {
            var paragraph = _paragraphs[paragraphIndex];
            if (paragraph.Start < firstParagraph)
            {
                continue;
            }

            if (paragraph.Start > lastParagraph)
            {
                break;
            }

            if (result is null)
            {
                result = paragraph.Format;
            }
            else if (result != paragraph.Format)
            {
                return null;
            }
        }

        return result ?? DefaultParagraphFormat;
    }

    internal int FindRunIndex(int position)
    {
        if (_runs.IsDefaultOrEmpty)
        {
            return 0;
        }

        var low = 0;
        var high = _runs.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_runs[middle].End <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    internal int FindParagraphIndex(int paragraphStart)
    {
        var low = 0;
        var high = _paragraphs.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) >> 1);
            if (_paragraphs[middle].Start <= paragraphStart)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    internal RichTextDocumentSnapshot ApplyCharacterFormat(
        Range range,
        Func<RichTextCharacterFormat, RichTextCharacterFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        if (length == 0)
        {
            return this;
        }

        var end = start + length;
        var runs = new List<RichTextRun>(_runs.Length + 2);
        foreach (var run in _runs)
        {
            if (run.End <= start || run.Start >= end)
            {
                runs.Add(run);
                continue;
            }

            if (run.Start < start)
            {
                runs.Add(run with { Length = start - run.Start });
            }

            var transformedStart = Math.Max(run.Start, start);
            var transformedEnd = Math.Min(run.End, end);
            runs.Add(new RichTextRun(
                transformedStart,
                transformedEnd - transformedStart,
                Validate(transform(run.Format))));

            if (run.End > end)
            {
                runs.Add(new RichTextRun(end, run.End - end, run.Format));
            }
        }

        return With(runs: runs);
    }

    internal RichTextDocumentSnapshot ApplyParagraphFormat(
        Range range,
        Func<RichTextParagraphFormat, RichTextParagraphFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        var lastPosition = length == 0 ? start : start + length - 1;
        var firstParagraph = GetParagraphStart(Text, start);
        var lastParagraph = GetParagraphStart(Text, lastPosition);
        var paragraphs = _paragraphs
            .Select(paragraph => paragraph.Start >= firstParagraph && paragraph.Start <= lastParagraph
                ? paragraph with
                {
                    // The public list reference is canonical. Native list state is a
                    // projection rebuilt by the snapshot constructor, so carrying the
                    // old projection through a public transformation would undo list
                    // removal, level changes, and restarts.
                    Format = Validate(transform(paragraph.Format)) with { NativeList = null },
                }
                : paragraph);
        return With(paragraphs: paragraphs);
    }

    internal RichTextDocumentSnapshot Replace(
        Range range,
        string? replacement,
        RichTextCharacterFormat? replacementFormat = null) =>
        Replace(range, replacement, replacementFormat, out _);

    internal RichTextDocumentSnapshot Replace(
        Range range,
        string? replacement,
        RichTextCharacterFormat? replacementFormat,
        out RichTextRange? affectedParagraphRange)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        replacement = NormalizeText(replacement);
        var oldEnd = start + length;
        var delta = replacement.Length - length;
        var text = string.Concat(Text.AsSpan(0, start), replacement, Text.AsSpan(oldEnd));
        var insertionFormat = Validate(replacementFormat ?? GetCaretFormat(start));
        var editedParagraphStart = GetParagraphStart(Text, start);
        var removedNewlineOffset = Text.AsSpan(start, length).IndexOf('\n');
        var removedNewline = removedNewlineOffset < 0
            ? -1
            : start + removedNewlineOffset;
        // RichEdit keeps the left paragraph format when the delimiter itself is
        // deleted after nonempty text. A selection that crosses the delimiter,
        // or the delimiter of an empty paragraph, adopts the surviving right
        // paragraph's format. The same rule naturally covers multi-line edits.
        var adoptsEndingParagraphFormat = removedNewline >= 0 &&
            (start < removedNewline || start == editedParagraphStart);
        var editedParagraphFormat = adoptsEndingParagraphFormat
            ? GetParagraphFormat(oldEnd)
            : GetParagraphFormat(start);
        var exitsEmptyList = length == 0 &&
            string.Equals(replacement, "\n", StringComparison.Ordinal) &&
            start == editedParagraphStart &&
            GetParagraphContentEnd(Text, editedParagraphStart) == editedParagraphStart &&
            editedParagraphFormat.List is not null;
        var runs = new List<RichTextRun>(_runs.Length + 2);

        foreach (var run in _runs)
        {
            if (run.End <= start)
            {
                runs.Add(run);
            }
            else if (run.Start >= oldEnd)
            {
                runs.Add(run with { Start = run.Start + delta });
            }
            else
            {
                if (run.Start < start)
                {
                    runs.Add(run with { Length = start - run.Start });
                }

                if (run.End > oldEnd)
                {
                    runs.Add(new RichTextRun(
                        start + replacement.Length,
                        run.End - oldEnd,
                        run.Format));
                }
            }
        }

        if (replacement.Length > 0)
        {
            runs.Add(new RichTextRun(start, replacement.Length, insertionFormat));
        }

        var paragraphs = EnumerateParagraphStarts(text).Select(paragraphStart =>
        {
            RichTextParagraphFormat format;
            if (paragraphStart < editedParagraphStart)
            {
                format = GetParagraphFormat(paragraphStart);
            }
            else if (paragraphStart == editedParagraphStart)
            {
                format = editedParagraphFormat;
            }
            else if (paragraphStart < start + replacement.Length)
            {
                format = editedParagraphFormat;
            }
            else
            {
                format = GetParagraphFormat(Math.Clamp(paragraphStart - delta, 0, Text.Length));
            }

            // WinUI RichEdit terminates an empty list item by inserting the
            // requested paragraph delimiter, then removing list membership from
            // both empty paragraphs. Other paragraph properties remain authored.
            if (exitsEmptyList &&
                (paragraphStart == start || paragraphStart == start + 1))
            {
                format = format with { List = null, NativeList = null };
            }

            return new RichTextParagraph(paragraphStart, format);
        });

        var paragraphStructureChanged = removedNewline >= 0 || replacement.Contains('\n');
        if (paragraphStructureChanged)
        {
            var affectedEndPosition = Math.Clamp(start + replacement.Length, 0, text.Length);
            var followingNewline = text.IndexOf('\n', affectedEndPosition);
            var affectedEnd = followingNewline < 0 ? text.Length : followingNewline + 1;
            affectedParagraphRange = new RichTextRange(
                editedParagraphStart,
                affectedEnd - editedParagraphStart);
        }
        else
        {
            affectedParagraphRange = null;
        }

        return new RichTextDocumentSnapshot(
            text,
            runs,
            paragraphs,
            RemapRanges(_links, start, oldEnd, replacement.Length),
            RemapRanges(_fields, start, oldEnd, replacement.Length),
            _images
                .Where(image => image.Position < start || image.Position >= oldEnd)
                .Select(image => image.Position >= oldEnd
                    ? image with { Position = image.Position + delta }
                    : image),
            DefaultCharacterFormat,
            DefaultParagraphFormat,
            _metadata,
            _listPictures.Values,
            _lists);
    }

    private static int GetParagraphContentEnd(string text, int paragraphStart)
    {
        var newline = text.IndexOf('\n', paragraphStart);
        return newline < 0 ? text.Length : newline;
    }

    internal RichTextDocumentSnapshot SetLink(Range range, string target, string? toolTip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        if (length == 0)
        {
            throw new ArgumentException("A hyperlink must cover at least one character.", nameof(range));
        }

        var end = start + length;
        var links = _links
            .Where(link => link.End <= start || link.Start >= end)
            .Append(new RichTextLink(start, length, target, toolTip));
        return With(links: links);
    }

    internal RichTextDocumentSnapshot RemoveLinks(Range range)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        var end = start + length;
        return With(links: _links.Where(link => link.End <= start || link.Start >= end));
    }

    internal RichTextDocumentSnapshot InsertImage(int position, RichTextImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if ((uint)position > (uint)Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var document = Replace(position..position, ObjectReplacementCharacter.ToString());
        var images = document._images.Append(image with { Position = position });
        return document.With(images: images);
    }

    internal RichTextDocumentSnapshot SetMetadata(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var metadata = value is null ? _metadata.Remove(name) : _metadata.SetItem(name, value);
        return With(metadata: metadata);
    }

    internal RichTextDocumentSnapshot With(
        string? text = null,
        IEnumerable<RichTextRun>? runs = null,
        IEnumerable<RichTextParagraph>? paragraphs = null,
        IEnumerable<RichTextLink>? links = null,
        IEnumerable<RichTextField>? fields = null,
        IEnumerable<RichTextImage>? images = null,
        RichTextCharacterFormat? defaultCharacterFormat = null,
        RichTextParagraphFormat? defaultParagraphFormat = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null,
        IEnumerable<RichTextListPicture>? listPictures = null,
        IEnumerable<KeyValuePair<RichTextListId, RichTextListDefinition>>? lists = null) =>
        new(
            text ?? Text,
            runs ?? _runs,
            paragraphs ?? _paragraphs,
            links ?? _links,
            fields ?? _fields,
            images ?? _images,
            defaultCharacterFormat ?? DefaultCharacterFormat,
            defaultParagraphFormat ?? DefaultParagraphFormat,
            metadata ?? _metadata,
            listPictures ?? _listPictures.Values,
            lists ?? _lists);

    internal RichTextDocumentSnapshot WithVersion(long version) =>
        new(
            Text,
            _runs,
            _paragraphs,
            _links,
            _fields,
            _images,
            DefaultCharacterFormat,
            DefaultParagraphFormat,
            _metadata,
            _listPictures.Values,
            _lists,
            version);

    internal bool ContentEquals(RichTextDocumentSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Text, other.Text, StringComparison.Ordinal) &&
            DefaultCharacterFormat == other.DefaultCharacterFormat &&
            DefaultParagraphFormat == other.DefaultParagraphFormat &&
            _runs.AsSpan().SequenceEqual(other._runs.AsSpan()) &&
            _paragraphs.AsSpan().SequenceEqual(other._paragraphs.AsSpan()) &&
            _links.AsSpan().SequenceEqual(other._links.AsSpan()) &&
            _fields.AsSpan().SequenceEqual(other._fields.AsSpan()) &&
            ImagesEqual(_images, other._images) &&
            ListsEqual(_lists, other._lists) &&
            ListPicturesEqual(_listPictures, other._listPictures) &&
            DictionariesEqual(_metadata, other._metadata);
    }

    internal RichTextDocumentSnapshot MergeNativeSnapshot(
        string text,
        IEnumerable<RichTextRun> runs,
        IEnumerable<RichTextParagraph> paragraphs,
        IEnumerable<RichTextLink>? links,
        IEnumerable<RichTextImage>? images,
        RichTextCharacterFormat defaultCharacterFormat,
        RichTextParagraphFormat defaultParagraphFormat,
        Func<RichTextCharacterFormat, RichTextCharacterFormat, RichTextCharacterFormat>?
            mergeCharacterFormat = null,
        Func<RichTextParagraphFormat, RichTextParagraphFormat, RichTextParagraphFormat>?
            mergeParagraphFormat = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var remapped = this;
        if (!string.Equals(Text, text, StringComparison.Ordinal))
        {
            var prefixLength = Text.AsSpan().CommonPrefixLength(text);
            var suffixLength = 0;
            var maximumSuffixLength = Math.Min(Text.Length, text.Length) - prefixLength;
            while (suffixLength < maximumSuffixLength &&
                   Text[^(suffixLength + 1)] == text[^(suffixLength + 1)])
            {
                suffixLength++;
            }

            var oldEnd = Text.Length - suffixLength;
            var newEnd = text.Length - suffixLength;
            remapped = Replace(
                prefixLength..oldEnd,
                text[prefixLength..newEnd]);
        }

        var nativeRuns = NormalizeRuns(text.Length, runs, defaultCharacterFormat);
        var nativeParagraphs = NormalizeParagraphs(text, paragraphs, defaultParagraphFormat);
        if (mergeCharacterFormat is not null)
        {
            defaultCharacterFormat = mergeCharacterFormat(
                defaultCharacterFormat,
                remapped.DefaultCharacterFormat);
            nativeRuns = MergeCharacterFormats(
                nativeRuns,
                remapped._runs,
                mergeCharacterFormat);
        }

        if (mergeParagraphFormat is not null)
        {
            defaultParagraphFormat = mergeParagraphFormat(
                defaultParagraphFormat,
                remapped.DefaultParagraphFormat);
            nativeParagraphs =
            [
                .. nativeParagraphs.Select(paragraph => paragraph with
                {
                    Format = mergeParagraphFormat(
                        paragraph.Format,
                        remapped.GetParagraphFormat(paragraph.Start)),
                }),
            ];
        }

        var linksWithToolTips = links is null
            ? remapped._links
            : links.Select(link =>
            {
                var prior = remapped._links.FirstOrDefault(candidate =>
                    candidate.Start == link.Start &&
                    candidate.Length == link.Length &&
                    string.Equals(candidate.Target, link.Target, StringComparison.Ordinal));
                return prior is null ? link : link with { ToolTip = prior.ToolTip };
            });
        return new RichTextDocumentSnapshot(
            text,
            nativeRuns,
            nativeParagraphs,
            linksWithToolTips,
            remapped._fields,
            images ?? remapped._images,
            defaultCharacterFormat,
            defaultParagraphFormat,
            _metadata,
            remapped._listPictures.Values,
            remapped._lists);
    }

    private static ImmutableArray<RichTextRun> MergeCharacterFormats(
        ImmutableArray<RichTextRun> nativeRuns,
        ImmutableArray<RichTextRun> previousRuns,
        Func<RichTextCharacterFormat, RichTextCharacterFormat, RichTextCharacterFormat> merge)
    {
        var result = new List<RichTextRun>(nativeRuns.Length + previousRuns.Length);
        var nativeIndex = 0;
        var previousIndex = 0;
        while (nativeIndex < nativeRuns.Length && previousIndex < previousRuns.Length)
        {
            var native = nativeRuns[nativeIndex];
            var previous = previousRuns[previousIndex];
            var start = Math.Max(native.Start, previous.Start);
            var end = Math.Min(native.End, previous.End);
            if (end > start)
            {
                AddRun(result, new RichTextRun(
                    start,
                    end - start,
                    merge(native.Format, previous.Format)));
            }

            if (native.End <= end)
            {
                nativeIndex++;
            }

            if (previous.End <= end)
            {
                previousIndex++;
            }
        }

        return [.. result];
    }

    private static bool ImagesEqual(
        ImmutableArray<RichTextImage> first,
        ImmutableArray<RichTextImage> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (var index = 0; index < first.Length; index++)
        {
            var left = first[index];
            var right = second[index];
            if (left with { Data = [] } != right with { Data = [] } ||
                !left.Data.AsSpan().SequenceEqual(right.Data.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ListsEqual(
        ImmutableDictionary<RichTextListId, RichTextListDefinition> first,
        ImmutableDictionary<RichTextListId, RichTextListDefinition> second) =>
        first.Count == second.Count &&
        first.All(pair => second.TryGetValue(pair.Key, out var value) && pair.Value == value);

    private static bool ListPicturesEqual(
        ImmutableDictionary<string, RichTextListPicture> first,
        ImmutableDictionary<string, RichTextListPicture> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach (var pair in first)
        {
            if (!second.TryGetValue(pair.Key, out var value) ||
                pair.Value with { Data = [] } != value with { Data = [] } ||
                !pair.Value.Data.AsSpan().SequenceEqual(value.Data.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> first,
        ImmutableDictionary<TKey, TValue> second)
        where TKey : notnull =>
        first.Count == second.Count &&
        first.All(pair => second.TryGetValue(pair.Key, out var value) &&
            EqualityComparer<TValue>.Default.Equals(pair.Value, value));

    private static string NormalizeText(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static ImmutableArray<RichTextRun> NormalizeRuns(
        int textLength,
        IEnumerable<RichTextRun>? source,
        RichTextCharacterFormat defaultFormat)
    {
        if (textLength == 0)
        {
            if (source?.Any() == true)
            {
                throw new ArgumentException("An empty document cannot contain character runs.", nameof(source));
            }

            return [];
        }

        var inheritedDefault = CreateInheritedCharacterFormat(defaultFormat);
        var ordered = source?.OrderBy(run => run.Start).ToArray() ?? [];
        var result = new List<RichTextRun>(ordered.Length + 1);
        var position = 0;
        foreach (var run in ordered)
        {
            ArgumentNullException.ThrowIfNull(run.Format);
            if (run.Start < position || run.Start < 0 || run.Length <= 0 || run.End > textLength)
            {
                throw new ArgumentException("Character runs must be positive, ordered, non-overlapping, and inside the text.", nameof(source));
            }

            if (run.Start > position)
            {
                AddRun(result, new RichTextRun(position, run.Start - position, inheritedDefault));
            }

            AddRun(result, run with { Format = Validate(run.Format) });
            position = run.End;
        }

        if (position < textLength)
        {
            AddRun(result, new RichTextRun(position, textLength - position, inheritedDefault));
        }

        return result.ToImmutableArray();
    }

    private static void AddRun(List<RichTextRun> runs, RichTextRun run)
    {
        if (run.Length == 0)
        {
            return;
        }

        if (runs.Count > 0 && runs[^1].End == run.Start && runs[^1].Format == run.Format)
        {
            runs[^1] = runs[^1] with { Length = runs[^1].Length + run.Length };
        }
        else
        {
            runs.Add(run);
        }
    }

    private static ImmutableArray<RichTextParagraph> NormalizeParagraphs(
        string text,
        IEnumerable<RichTextParagraph>? source,
        RichTextParagraphFormat defaultFormat)
    {
        var sourceArray = source?.ToArray() ?? [];
        if (sourceArray.DistinctBy(paragraph => paragraph.Start).Count() != sourceArray.Length)
        {
            throw new ArgumentException("A document cannot contain multiple formats for one paragraph.", nameof(source));
        }

        var supplied = sourceArray.ToDictionary(paragraph => paragraph.Start);

        var starts = EnumerateParagraphStarts(text).ToArray();
        var validStarts = starts.ToHashSet();
        if (supplied.Keys.Any(start => !validStarts.Contains(start)))
        {
            throw new ArgumentException("Paragraph formats must start at a paragraph boundary.", nameof(source));
        }

        return starts
            .Select((start, index) => new RichTextParagraph(
                new RichTextRange(
                    start,
                    (index + 1 < starts.Length ? starts[index + 1] : text.Length) - start),
                supplied.TryGetValue(start, out var paragraph)
                    ? Validate(paragraph.Format)
                    : defaultFormat))
            .ToImmutableArray();
    }

    private static ImmutableArray<RichTextLink> NormalizeLinks(
        int textLength,
        IEnumerable<RichTextLink>? source)
    {
        var links = source?.OrderBy(link => link.Start).ToArray() ?? [];
        var previousEnd = 0;
        foreach (var link in links)
        {
            if (link.Start < previousEnd || link.Start < 0 || link.Length <= 0 || link.End > textLength)
            {
                throw new ArgumentException("Hyperlinks must be positive, non-overlapping ranges inside the text.", nameof(source));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(link.Target);
            previousEnd = link.End;
        }

        return [.. links];
    }

    private static ImmutableArray<RichTextField> NormalizeFields(
        int textLength,
        IEnumerable<RichTextField>? source)
    {
        var fields = source?.OrderBy(field => field.Start).ToArray() ?? [];
        var previousEnd = 0;
        foreach (var field in fields)
        {
            if (field.Start < previousEnd || field.Start < 0 || field.Length < 0 || field.End > textLength)
            {
                throw new ArgumentException("Fields must be non-overlapping ranges inside the text.", nameof(source));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(field.Instruction);
            previousEnd = field.End;
        }

        return [.. fields];
    }

    private static ImmutableArray<RichTextImage> NormalizeImages(
        string text,
        IEnumerable<RichTextImage>? source)
    {
        var images = source?.OrderBy(image => image.Position).ToArray() ?? [];
        var positions = new HashSet<int>();
        foreach (var image in images)
        {
            if ((uint)image.Position >= (uint)text.Length ||
                text[image.Position] != ObjectReplacementCharacter ||
                !positions.Add(image.Position))
            {
                throw new ArgumentException("Each image must occupy a unique object-replacement character in the text.", nameof(source));
            }

            if (string.IsNullOrWhiteSpace(image.MediaType) ||
                !double.IsFinite(image.Width) || image.Width < 0 ||
                !double.IsFinite(image.Height) || image.Height < 0 ||
                !double.IsFinite(image.Rotation))
            {
                throw new ArgumentException("Image metadata is invalid.", nameof(source));
            }
        }

        return [.. images];
    }

    private static ImmutableDictionary<string, RichTextListPicture> NormalizeListPictures(
        IEnumerable<RichTextListPicture>? source)
    {
        var result = ImmutableDictionary.CreateBuilder<string, RichTextListPicture>(
            StringComparer.Ordinal);
        foreach (var picture in source ?? [])
        {
            ArgumentNullException.ThrowIfNull(picture);
            if (string.IsNullOrWhiteSpace(picture.Id) ||
                string.IsNullOrWhiteSpace(picture.MediaType) ||
                !double.IsFinite(picture.Width) || picture.Width < 0 ||
                !double.IsFinite(picture.Height) || picture.Height < 0)
            {
                throw new ArgumentException("List picture metadata is invalid.", nameof(source));
            }

            if (!result.TryAdd(
                    picture.Id,
                    picture.Data.IsDefault ? picture with { Data = [] } : picture))
            {
                throw new ArgumentException(
                    "List picture identifiers must be unique.",
                    nameof(source));
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableDictionary<RichTextListId, RichTextListDefinition> NormalizeLists(
        IEnumerable<KeyValuePair<RichTextListId, RichTextListDefinition>>? source,
        ImmutableArray<RichTextParagraph> paragraphs)
    {
        var result = ImmutableDictionary.CreateBuilder<RichTextListId, RichTextListDefinition>();
        foreach (var pair in source ?? [])
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            var normalized = new RichTextListDefinition(pair.Value.Levels);
            if (!result.TryAdd(pair.Key, normalized))
            {
                throw new ArgumentException("List identifiers must be unique.", nameof(source));
            }
        }

        foreach (var group in paragraphs
                     .Where(static paragraph => paragraph.Format.NativeList is not null)
                     .GroupBy(static paragraph => paragraph.Format.NativeList!.Id))
        {
            var id = new RichTextListId(group.Key);
            if (result.ContainsKey(id))
            {
                continue;
            }

            var nativeLevels = group
                .GroupBy(static paragraph => paragraph.Format.NativeList!.Level)
                .ToDictionary(
                    static level => level.Key,
                    static level => level.FirstOrDefault(paragraph =>
                        !paragraph.Format.NativeList!.Restart) ?? level.First());
            var maximumLevel = nativeLevels.Keys.Max();
            var fallback = nativeLevels.Values.First();
            var levels = new RichTextListLevelDefinition[maximumLevel + 1];
            for (var level = 0; level <= maximumLevel; level++)
            {
                var paragraph = nativeLevels.GetValueOrDefault(level, fallback);
                var native = paragraph.Format.NativeList!;
                var converted = RichTextListConversions.ToLevel(native);
                levels[level] = converted with
                {
                    LeadingIndent = paragraph.Format.LeadingIndent,
                    FirstLineIndent = paragraph.Format.FirstLineIndent,
                    MarkerTab = paragraph.Format.TabStops.IsDefaultOrEmpty
                        ? 0
                        : paragraph.Format.TabStops[0].Position,
                };
            }

            result.Add(id, new RichTextListDefinition(levels));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<RichTextParagraph> BindParagraphLists(
        ImmutableArray<RichTextParagraph> paragraphs,
        ImmutableDictionary<RichTextListId, RichTextListDefinition> lists) =>
        [
            .. paragraphs.Select(paragraph => paragraph with
            {
                Format = BindParagraphList(paragraph.Format, lists),
            }),
        ];

    private static RichTextParagraphFormat BindParagraphList(
        RichTextParagraphFormat format,
        ImmutableDictionary<RichTextListId, RichTextListDefinition> lists)
    {
        var item = format.List;
        if (format.NativeList is { } native)
        {
            item = RichTextListConversions.ToItem(native);
        }

        if (item is null)
        {
            return format with { List = null, NativeList = null };
        }

        if (!lists.TryGetValue(item.ListId, out var definition) ||
            (uint)item.Level >= (uint)definition.Levels.Length)
        {
            throw new ArgumentException(
                "Every list item must reference an existing list definition and level.",
                nameof(lists));
        }

        return format with
        {
            List = item,
            NativeList = RichTextListConversions.ToNative(
                item.ListId,
                item.Level,
                item.RestartAt,
                definition),
        };
    }

    private static void ValidateListPictureReferences(
        ImmutableDictionary<RichTextListId, RichTextListDefinition> lists,
        ImmutableDictionary<string, RichTextListPicture> pictures,
        string parameterName)
    {
        var missingId = lists.Values
            .SelectMany(static definition => definition.Levels)
            .Select(static level => (level.Marker as RichTextListMarker.Picture)?.PictureId)
            .FirstOrDefault(id => id is not null && !pictures.ContainsKey(id));
        if (missingId is not null)
        {
            throw new ArgumentException(
                $"List picture '{missingId}' is not present in the document.",
                parameterName);
        }
    }

    private static RichTextCharacterFormat Validate(RichTextCharacterFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.FontSize is { } size && (!double.IsFinite(size) || size <= 0) ||
            format.FontWeight is < 1 or > 1000 ||
            !double.IsFinite(format.BaselineOffset) ||
            !double.IsFinite(format.CharacterSpacing) ||
            !double.IsFinite(format.HorizontalScale) || format.HorizontalScale <= 0 ||
            format.Shading is < 0 or > 10000)
        {
            throw new ArgumentException("Character formatting contains an invalid numeric value.", nameof(format));
        }

        return format with
        {
            BackgroundColor = NormalizeVisibleColor(format.BackgroundColor),
            UnderlineColor = NormalizeVisibleColor(format.UnderlineColor),
            StrikethroughColor = NormalizeVisibleColor(format.StrikethroughColor),
            ShadingForegroundColor = NormalizeVisibleColor(format.ShadingForegroundColor),
            ShadingBackgroundColor = NormalizeVisibleColor(format.ShadingBackgroundColor),
        };
    }

    private static RichTextParagraphFormat Validate(RichTextParagraphFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!double.IsFinite(format.LeadingIndent) ||
            !double.IsFinite(format.TrailingIndent) ||
            !double.IsFinite(format.FirstLineIndent) ||
            !double.IsFinite(format.SpaceBefore) || format.SpaceBefore < 0 ||
            !double.IsFinite(format.SpaceAfter) || format.SpaceAfter < 0 ||
            !double.IsFinite(format.LineSpacing) || format.LineSpacing < 0 ||
            format.MinimumLineHeight is { } minimum && (!double.IsFinite(minimum) || minimum < 0) ||
            format.MaximumLineHeight is { } maximum && (!double.IsFinite(maximum) || maximum < 0) ||
            format.MinimumLineHeight is { } min && format.MaximumLineHeight is { } max && min > max ||
            format.Shading is < 0 or > 10000)
        {
            throw new ArgumentException("Paragraph formatting contains an invalid numeric value.", nameof(format));
        }

        var tabs = format.TabStops.IsDefault ? [] : format.TabStops;
        if (tabs.Any(tab => !double.IsFinite(tab.Position) || tab.Position < 0))
        {
            throw new ArgumentException("Tab positions must be finite and nonnegative.", nameof(format));
        }

        var nativeList = format.NativeList;
        if (nativeList is not null &&
            (nativeList.Id <= 0 || nativeList.Level is < 0 or > 8 || nativeList.StartAt <= 0 ||
             string.IsNullOrEmpty(nativeList.BulletText) ||
             (string.IsNullOrWhiteSpace(nativeList.PictureId) && nativeList.PictureId is not null) ||
             (nativeList.PictureId is not null && nativeList.Kind != RichListKind.Bulleted)))
        {
            throw new ArgumentException("List formatting is invalid.", nameof(format));
        }

        if (format.Border is { } border &&
            (!double.IsFinite(border.Width) || border.Width < 0 ||
             border.Style == RichTextBorderStyle.None && border.Sides != RichTextBorderSides.None))
        {
            throw new ArgumentException("Paragraph border formatting is invalid.", nameof(format));
        }

        return format with
        {
            TabStops = [.. tabs.OrderBy(tab => tab.Position).DistinctBy(tab => tab.Position)],
            BackgroundColor = NormalizeVisibleColor(format.BackgroundColor),
            ShadingForegroundColor = NormalizeVisibleColor(format.ShadingForegroundColor),
            ShadingBackgroundColor = NormalizeVisibleColor(format.ShadingBackgroundColor),
        };
    }

    private static Color? NormalizeVisibleColor(Color? color) =>
        color is { Alpha: <= 0 } ? null : color;

    private static int GetParagraphStart(string text, int position) =>
        position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

    private static IEnumerable<int> EnumerateParagraphStarts(string text)
    {
        yield return 0;
        for (var index = text.IndexOf('\n'); index >= 0; index = text.IndexOf('\n', index + 1))
        {
            yield return index + 1;
        }
    }

    private static IEnumerable<RichTextLink> RemapRanges(
        IEnumerable<RichTextLink> ranges,
        int editStart,
        int oldEnd,
        int replacementLength)
    {
        var delta = replacementLength - (oldEnd - editStart);
        foreach (var range in ranges)
        {
            if (range.End <= editStart)
            {
                yield return range;
            }
            else if (range.Start >= oldEnd)
            {
                yield return range with { Start = range.Start + delta };
            }
            else if (range.Start < editStart && range.End > oldEnd)
            {
                yield return range with { Length = range.Length + delta };
            }
            else if (range.Start < editStart)
            {
                yield return range with { Length = editStart - range.Start };
            }
            else if (range.End > oldEnd)
            {
                yield return range with
                {
                    Start = editStart + replacementLength,
                    Length = range.End - oldEnd,
                };
            }
        }
    }

    private static IEnumerable<RichTextField> RemapRanges(
        IEnumerable<RichTextField> ranges,
        int editStart,
        int oldEnd,
        int replacementLength)
    {
        var delta = replacementLength - (oldEnd - editStart);
        foreach (var range in ranges)
        {
            if (range.End <= editStart)
            {
                yield return range;
            }
            else if (range.Start >= oldEnd)
            {
                yield return range with { Start = range.Start + delta };
            }
            else if (range.Start < editStart && range.End > oldEnd)
            {
                yield return range with { Length = range.Length + delta };
            }
            else if (range.Start < editStart)
            {
                yield return range with { Length = editStart - range.Start };
            }
            else if (range.End > oldEnd)
            {
                yield return range with
                {
                    Start = editStart + replacementLength,
                    Length = range.End - oldEnd,
                };
            }
        }
    }
}
