using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Represents an immutable, enumerable view of one rich-text document version.
/// </summary>
public sealed class RichTextDocumentSnapshot
{
    private const int MaximumRtfListOverrideCount = 2000;
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
        long version = 0,
        bool nativeListsAuthoritative = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        Version = version;
        Text = NormalizeText(text);
        DefaultCharacterFormat = Validate(defaultCharacterFormat ?? RichTextCharacterFormat.Default);
        DefaultParagraphFormat = Validate(defaultParagraphFormat ?? RichTextParagraphFormat.Default);
        _runs = NormalizeRuns(Text.Length, runs, DefaultCharacterFormat);
        _listPictures = NormalizeListPictures(listPictures);
        var normalizedParagraphs = NormalizeParagraphs(Text, paragraphs, DefaultParagraphFormat);
        _lists = NormalizeLists(lists, normalizedParagraphs, nativeListsAuthoritative);
        _paragraphs = BindParagraphLists(normalizedParagraphs, _lists);
        ValidateRtfListCapacity(_paragraphs);
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
        RichTextCharacterFormat? replacementFormat = null)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        replacement = NormalizeText(replacement);
        var oldEnd = start + length;
        var delta = replacement.Length - length;
        var text = string.Concat(Text.AsSpan(0, start), replacement, Text.AsSpan(oldEnd));
        var insertionFormat = Validate(replacementFormat ?? GetCaretFormat(start));
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

        var insertionParagraphFormat = GetParagraphFormat(start);
        var paragraphs = EnumerateParagraphStarts(text).Select(paragraphStart =>
        {
            RichTextParagraphFormat format;
            if (paragraphStart <= start)
            {
                format = GetParagraphFormat(paragraphStart);
            }
            else if (paragraphStart < start + replacement.Length)
            {
                format = insertionParagraphFormat;
            }
            else
            {
                format = GetParagraphFormat(Math.Clamp(paragraphStart - delta, 0, Text.Length));
            }

            return new RichTextParagraph(paragraphStart, format);
        });

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

    internal RichTextDocumentSnapshot PruneUnreferencedListResources()
    {
        var usedListIds = _paragraphs
            .Select(static paragraph => paragraph.Format.List?.ListId)
            .OfType<RichTextListId>()
            .ToHashSet();
        var lists = _lists.Where(pair => usedListIds.Contains(pair.Key)).ToArray();
        var usedPictureIds = lists
            .SelectMany(static pair => pair.Value.Levels)
            .Select(static level => (level.Marker as RichTextListMarker.Picture)?.PictureId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return With(
            lists: lists,
            listPictures: _listPictures.Values.Where(picture => usedPictureIds.Contains(picture.Id)));
    }

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

    internal RichTextDocumentSnapshot RemapText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(Text, text, StringComparison.Ordinal))
        {
            return this;
        }

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
        return Replace(prefixLength..oldEnd, text[prefixLength..newEnd]);
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
            mergeParagraphFormat = null,
        RichTextDocumentSnapshot? remappedSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var remapped = remappedSnapshot ?? RemapText(text);
        if (!string.Equals(remapped.Text, text, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The remapped snapshot text must match the native text.",
                nameof(remappedSnapshot));
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
            remapped._lists,
            nativeListsAuthoritative: true);
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
        var fields = source?.ToArray() ?? [];
        var usedIds = new HashSet<RichTextFieldId>();
        var nextId = fields
            .Select(static field => field.Id.Value)
            .Where(static value => value > 0)
            .DefaultIfEmpty()
            .Max();
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (field.Id.Value <= 0)
            {
                do
                {
                    nextId = checked(nextId + 1);
                }
                while (usedIds.Contains(new RichTextFieldId(nextId)));

                field = field with { Id = new RichTextFieldId(nextId) };
                fields[index] = field;
            }

            if (!usedIds.Add(field.Id))
            {
                throw new ArgumentException(
                    "Field identifiers must be positive and unique.",
                    nameof(source));
            }
        }

        Array.Sort(fields, static (first, second) =>
        {
            var position = first.Start.CompareTo(second.Start);
            return position != 0 ? position : first.Id.Value.CompareTo(second.Id.Value);
        });
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
                !IsNonnegativeRtfTwips(image.Width) ||
                !IsNonnegativeRtfTwips(image.Height) ||
                !IsRtfTwips(image.Crop.Left) ||
                !IsRtfTwips(image.Crop.Top) ||
                !IsRtfTwips(image.Crop.Right) ||
                !IsRtfTwips(image.Crop.Bottom) ||
                !double.IsFinite(image.Rotation) ||
                !Enum.IsDefined(image.VerticalAlignment))
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
                !IsNonnegativeRtfTwips(picture.Width) ||
                !IsNonnegativeRtfTwips(picture.Height))
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
        ImmutableArray<RichTextParagraph> paragraphs,
        bool nativeListsAuthoritative)
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
            if (!nativeListsAuthoritative && result.ContainsKey(id))
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
            result.TryGetValue(id, out var existingDefinition);
            var levelCount = Math.Max(
                maximumLevel + 1,
                existingDefinition?.Levels.Length ?? 0);
            var levels = new RichTextListLevelDefinition[levelCount];
            for (var level = 0; level < levelCount; level++)
            {
                if (!nativeLevels.TryGetValue(level, out var paragraph) &&
                    existingDefinition is not null &&
                    level < existingDefinition.Levels.Length)
                {
                    levels[level] = existingDefinition.Levels[level];
                    continue;
                }

                paragraph ??= fallback;
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

            // Native list state is authoritative during readback. Reusing a stable
            // managed identifier must update its definition rather than silently
            // restoring an older marker style associated with that identifier.
            result[id] = new RichTextListDefinition(levels);
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

    private static void ValidateRtfListCapacity(ImmutableArray<RichTextParagraph> paragraphs)
    {
        var definitionCount = paragraphs
            .Select(static paragraph => paragraph.Format.NativeList?.Id)
            .OfType<int>()
            .Distinct()
            .Count();
        var restartCount = paragraphs.Count(static paragraph =>
            paragraph.Format.NativeList is
            {
                Kind: RichListKind.Numbered,
                Restart: true,
            });
        if ((long)definitionCount + restartCount > MaximumRtfListOverrideCount)
        {
            throw new ArgumentException(
                $"RTF supports at most {MaximumRtfListOverrideCount} list definitions and numbered-list restarts in one document.",
                nameof(paragraphs));
        }
    }

    private static RichTextCharacterFormat Validate(RichTextCharacterFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        ValidateColor(format.ForegroundColor);
        if (format.FontSize is { } size &&
                (size <= 0 ||
                 !IsRtfScaledInteger(size, 2d) ||
                 Math.Round(size * 2d) < 1d) ||
            format.FontWeight is < 1 or > 1000 ||
            !IsRtfScaledInteger(Math.Abs(format.BaselineOffset), 2d) ||
            !IsRtfTwips(format.CharacterSpacing) ||
            !IsRtfScaledInteger(format.CharacterSpacing, 4d) ||
            format.HorizontalScale <= 0 ||
            !IsRtfScaledInteger(format.HorizontalScale, 100d) ||
            Math.Round(format.HorizontalScale * 100d) < 1d ||
            format.Shading is < 0 or > 10000 ||
            !Enum.IsDefined(format.Underline) ||
            !Enum.IsDefined(format.Strikethrough) ||
            !Enum.IsDefined(format.Script) ||
            !Enum.IsDefined(format.Direction) ||
            !Enum.IsDefined(format.Kerning) ||
            !Enum.IsDefined(format.Ligatures))
        {
            throw new ArgumentException("Character formatting contains an invalid value.", nameof(format));
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
        if (!IsRtfTwips(format.LeadingIndent) ||
            !IsRtfTwips(format.TrailingIndent) ||
            !IsRtfTwips(format.FirstLineIndent) ||
            !IsNonnegativeRtfTwips(format.SpaceBefore) ||
            !IsNonnegativeRtfTwips(format.SpaceAfter) ||
            !IsValidLineSpacing(format.LineSpacingRule, format.LineSpacing) ||
            format.MinimumLineHeight is { } minimum && !IsNonnegativeRtfTwips(minimum) ||
            format.MaximumLineHeight is { } maximum && !IsNonnegativeRtfTwips(maximum) ||
            format.MinimumLineHeight is { } min && format.MaximumLineHeight is { } max && min > max ||
            format.Shading is < 0 or > 10000 ||
            !Enum.IsDefined(format.Alignment) ||
            !Enum.IsDefined(format.Direction) ||
            !Enum.IsDefined(format.LineSpacingRule))
        {
            throw new ArgumentException("Paragraph formatting contains an invalid value.", nameof(format));
        }

        var tabs = format.TabStops.IsDefault ? [] : format.TabStops;
        if (tabs.Any(tab =>
                !IsNonnegativeRtfTwips(tab.Position) ||
                !Enum.IsDefined(tab.Alignment) ||
                !Enum.IsDefined(tab.Leader)))
        {
            throw new ArgumentException("Paragraph tab formatting is invalid.", nameof(format));
        }

        var nativeList = format.NativeList;
        if (nativeList is not null &&
            (nativeList.Id <= 0 || nativeList.Level is < 0 or > 8 || nativeList.StartAt <= 0 ||
             !Enum.IsDefined(nativeList.Kind) ||
             !Enum.IsDefined(nativeList.NumberStyle) ||
             string.IsNullOrEmpty(nativeList.BulletText) ||
             (string.IsNullOrWhiteSpace(nativeList.PictureId) && nativeList.PictureId is not null) ||
             (nativeList.PictureId is not null && nativeList.Kind != RichListKind.Bulleted)))
        {
            throw new ArgumentException("List formatting is invalid.", nameof(format));
        }

        if (format.Border is { } border &&
            (!IsNonnegativeRtfTwips(border.Width) ||
             (border.Sides & ~RichTextBorderSides.All) != 0 ||
             !Enum.IsDefined(border.Style) ||
             border.Style == RichTextBorderStyle.None && border.Sides != RichTextBorderSides.None))
        {
            throw new ArgumentException("Paragraph border formatting is invalid.", nameof(format));
        }

        ValidateColor(format.Border?.Color);

        return format with
        {
            TabStops = [.. tabs.OrderBy(tab => tab.Position).DistinctBy(tab => tab.Position)],
            BackgroundColor = NormalizeVisibleColor(format.BackgroundColor),
            ShadingForegroundColor = NormalizeVisibleColor(format.ShadingForegroundColor),
            ShadingBackgroundColor = NormalizeVisibleColor(format.ShadingBackgroundColor),
        };
    }

    private static Color? NormalizeVisibleColor(Color? color)
    {
        ValidateColor(color);

        return color is { Alpha: <= 0 } ? null : color;
    }

    private static void ValidateColor(Color? color)
    {
        if (color is not null &&
            (!float.IsFinite(color.Red) ||
             !float.IsFinite(color.Green) ||
             !float.IsFinite(color.Blue) ||
             !float.IsFinite(color.Alpha)))
        {
            throw new ArgumentException("A formatting color contains a non-finite channel.");
        }
    }

    private static bool IsValidLineSpacing(RichTextLineSpacingRule rule, double value) =>
        value >= 0 && rule switch
        {
            RichTextLineSpacingRule.Multiple => IsRtfScaledInteger(value, 240d),
            RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly =>
                IsRtfTwips(value),
            _ => double.IsFinite(value),
        };

    private static bool IsNonnegativeRtfTwips(double value) =>
        value >= 0 && IsRtfTwips(value);

    private static bool IsRtfTwips(double value) =>
        IsRtfScaledInteger(value, 20d);

    private static bool IsRtfScaledInteger(double value, double scale)
    {
        var scaled = value * scale;
        return double.IsFinite(value) &&
            scaled >= int.MinValue &&
            scaled <= int.MaxValue;
    }

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
            if (range.Length == 0 && oldEnd == editStart && range.Start == editStart)
            {
                yield return range with { Start = editStart + replacementLength };
            }
            else if (range.End <= editStart)
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
