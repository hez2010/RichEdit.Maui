using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Represents an immutable, enumerable view of one rich-text document version.
/// </summary>
public sealed partial class RichTextDocumentSnapshot
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

    /// <summary>Gets the owning document's opaque revision, or an invalid value for a standalone fragment.</summary>
    public RichTextRevision Revision { get; private init; }

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
            else if (paragraphStart < start + replacement.Length ||
                paragraphStart == start + replacement.Length &&
                (oldEnd == start || oldEnd == 0 || Text[oldEnd - 1] != '\n'))
            {
                // A new list item continues its predecessor's counter. A restart
                // belongs to the original item, not every paragraph split from it.
                format = insertionParagraphFormat.List is { } item
                    ? insertionParagraphFormat with { List = new RichTextListItemFormat(item.ListId, item.Level), NativeList = null }
                    : insertionParagraphFormat;
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
        IEnumerable<KeyValuePair<RichTextListId, RichTextListDefinition>>? lists = null)
    {
        // Character presentation changes reuse the immutable source structure. Revalidating
        // every paragraph and semantic object here made dense decoration updates allocate
        // complete document indexes repeatedly, even though only character runs changed.
        if (text is null && paragraphs is null && links is null && fields is null && images is null &&
            defaultCharacterFormat is null && defaultParagraphFormat is null && metadata is null && listPictures is null && lists is null)
            return new(this, runs);

        return new(
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
    }

    private RichTextDocumentSnapshot(RichTextDocumentSnapshot source, IEnumerable<RichTextRun>? runs) : this(source, 0, null)
    {
        Revision = default;
        _cachedRtf = null;
        _runs = runs is null ? source._runs : NormalizeRuns(Text.Length, runs, DefaultCharacterFormat);
    }

    private RichTextDocumentSnapshot(RichTextDocumentSnapshot source, long version, object? identity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        Version = version;
        Revision = new(identity ?? source.Revision.Identity, version);
        Text = source.Text;
        DefaultCharacterFormat = source.DefaultCharacterFormat;
        DefaultParagraphFormat = source.DefaultParagraphFormat;
        _runs = source._runs;
        _paragraphs = source._paragraphs;
        _links = source._links;
        _fields = source._fields;
        _images = source._images;
        _lists = source._lists;
        _listPictures = source._listPictures;
        _metadata = source._metadata;
        _cachedRtf = source._cachedRtf;
    }

    internal RichTextDocumentSnapshot WithVersion(long version, object? identity = null) => new(this, version, identity);

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
            _images.AsSpan().SequenceEqual(other._images.AsSpan()) &&
            DictionariesEqual(_lists, other._lists) &&
            DictionariesEqual(_listPictures, other._listPictures) &&
            DictionariesEqual(_metadata, other._metadata);
    }

    internal RichTextDocumentSnapshot RemapText(string text, RichTextRange? replacedRange = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(Text, text, StringComparison.Ordinal))
        {
            return this;
        }

        if (replacedRange is { } range && TryGetReplacement(Text, text, range, out var insertedText))
        {
            return Replace(range.Start..range.End, insertedText);
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

    internal static bool TryGetReplacement(string before, string after, RichTextRange range, out string insertedText)
    {
        insertedText = string.Empty;
        var insertedLength = after.Length - (before.Length - range.Length);
        if (range.Start < 0 || range.End > before.Length || insertedLength < 0 || range.Start > after.Length - insertedLength ||
            !before.AsSpan(0, range.Start).SequenceEqual(after.AsSpan(0, range.Start)) ||
            !before.AsSpan(range.End).SequenceEqual(after.AsSpan(range.Start + insertedLength)))
        {
            return false;
        }

        insertedText = after.Substring(range.Start, insertedLength);
        return true;
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

    private static bool DictionariesEqual<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> first,
        ImmutableDictionary<TKey, TValue> second)
        where TKey : notnull =>
            first.Count == second.Count &&
            first.All(pair => second.TryGetValue(pair.Key, out var value) &&
                EqualityComparer<TValue>.Default.Equals(pair.Value, value));

    internal static string NormalizeText(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

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
