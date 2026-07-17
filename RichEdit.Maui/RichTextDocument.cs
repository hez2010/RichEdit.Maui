using System.Collections.Immutable;

namespace RichEdit.Maui;

public sealed class RichTextDocument
{
    public const char ObjectReplacementCharacter = '\uFFFC';
    public const char SoftLineBreakCharacter = '\u2028';

    private readonly ImmutableArray<RichTextRun> _runs;
    private readonly ImmutableArray<RichTextParagraph> _paragraphs;
    private readonly ImmutableArray<RichTextLink> _links;
    private readonly ImmutableArray<RichTextField> _fields;
    private readonly ImmutableArray<RichTextImage> _images;
    private readonly ImmutableDictionary<string, string> _metadata;

    public RichTextDocument(
        string? text,
        IEnumerable<RichTextRun>? runs = null,
        IEnumerable<RichTextParagraph>? paragraphs = null,
        IEnumerable<RichTextLink>? links = null,
        IEnumerable<RichTextField>? fields = null,
        IEnumerable<RichTextImage>? images = null,
        RichTextCharacterFormat? defaultCharacterFormat = null,
        RichTextParagraphFormat? defaultParagraphFormat = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Text = NormalizeText(text);
        DefaultCharacterFormat = Validate(defaultCharacterFormat ?? RichTextCharacterFormat.Default);
        DefaultParagraphFormat = Validate(defaultParagraphFormat ?? RichTextParagraphFormat.Default);
        _runs = NormalizeRuns(Text.Length, runs, DefaultCharacterFormat);
        _paragraphs = NormalizeParagraphs(Text, paragraphs, DefaultParagraphFormat);
        _links = NormalizeLinks(Text.Length, links);
        _fields = NormalizeFields(Text.Length, fields);
        _images = NormalizeImages(Text, images);
        _metadata = metadata?.ToImmutableDictionary(StringComparer.Ordinal) ??
            ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public string Text { get; }

    public IReadOnlyList<RichTextRun> Runs => _runs;

    public IReadOnlyList<RichTextParagraph> Paragraphs => _paragraphs;

    public IReadOnlyList<RichTextLink> Links => _links;

    public IReadOnlyList<RichTextField> Fields => _fields;

    public IReadOnlyList<RichTextImage> Images => _images;

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public RichTextCharacterFormat DefaultCharacterFormat { get; }

    public RichTextParagraphFormat DefaultParagraphFormat { get; }

    public static RichTextDocument FromPlainText(string? text) => new(text);

    public static RichTextDocument FromRtf(string rtf) => RtfCodec.Parse(rtf);

    public string ToRtf() => RtfCodec.Serialize(this);

    public RichTextCharacterFormat GetCharacterFormat(int position)
    {
        if (Text.Length == 0)
        {
            return DefaultCharacterFormat;
        }

        var index = Math.Clamp(position, 0, Text.Length - 1);
        foreach (var run in _runs)
        {
            if (index < run.End)
            {
                return run.Format;
            }
        }

        return _runs[^1].Format;
    }

    public RichTextCharacterFormat GetCaretFormat(int position)
    {
        if (Text.Length == 0)
        {
            return DefaultCharacterFormat;
        }

        var index = position == 0 ? 0 : Math.Clamp(position - 1, 0, Text.Length - 1);
        return GetCharacterFormat(index);
    }

    public RichTextCharacterFormat? GetUniformCharacterFormat(Range range)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        if (length == 0)
        {
            return GetCaretFormat(start);
        }

        var end = start + length;
        RichTextCharacterFormat? result = null;
        foreach (var run in _runs)
        {
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

        return result ?? DefaultCharacterFormat;
    }

    public RichTextParagraphFormat GetParagraphFormat(int position)
    {
        var lineStart = GetParagraphStart(Text, Math.Clamp(position, 0, Text.Length));
        foreach (var paragraph in _paragraphs)
        {
            if (paragraph.Start == lineStart)
            {
                return paragraph.Format;
            }
        }

        return DefaultParagraphFormat;
    }

    public RichTextParagraphFormat? GetUniformParagraphFormat(Range range)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        var lastPosition = length == 0 ? start : start + length - 1;
        var firstParagraph = GetParagraphStart(Text, start);
        var lastParagraph = GetParagraphStart(Text, lastPosition);
        RichTextParagraphFormat? result = null;
        foreach (var paragraph in _paragraphs)
        {
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

    public RichTextDocument ApplyCharacterFormat(
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

    public RichTextDocument ApplyParagraphFormat(
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
                ? paragraph with { Format = Validate(transform(paragraph.Format)) }
                : paragraph);
        return With(paragraphs: paragraphs);
    }

    public RichTextDocument Replace(
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

        return new RichTextDocument(
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
            _metadata);
    }

    public RichTextDocument SetLink(Range range, string target, string? toolTip = null)
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

    public RichTextDocument RemoveLinks(Range range)
    {
        var (start, length) = range.GetOffsetAndLength(Text.Length);
        var end = start + length;
        return With(links: _links.Where(link => link.End <= start || link.Start >= end));
    }

    public RichTextDocument InsertImage(int position, RichTextImage image)
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

    public RichTextDocument SetMetadata(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var metadata = value is null ? _metadata.Remove(name) : _metadata.SetItem(name, value);
        return With(metadata: metadata);
    }

    internal RichTextDocument With(
        string? text = null,
        IEnumerable<RichTextRun>? runs = null,
        IEnumerable<RichTextParagraph>? paragraphs = null,
        IEnumerable<RichTextLink>? links = null,
        IEnumerable<RichTextField>? fields = null,
        IEnumerable<RichTextImage>? images = null,
        RichTextCharacterFormat? defaultCharacterFormat = null,
        RichTextParagraphFormat? defaultParagraphFormat = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null) =>
        new(
            text ?? Text,
            runs ?? _runs,
            paragraphs ?? _paragraphs,
            links ?? _links,
            fields ?? _fields,
            images ?? _images,
            defaultCharacterFormat ?? DefaultCharacterFormat,
            defaultParagraphFormat ?? DefaultParagraphFormat,
            metadata ?? _metadata);

    internal RichTextDocument MergeNativeSnapshot(
        string text,
        IEnumerable<RichTextRun> runs,
        IEnumerable<RichTextParagraph> paragraphs,
        IEnumerable<RichTextLink> links,
        IEnumerable<RichTextImage> images,
        RichTextCharacterFormat defaultCharacterFormat,
        RichTextParagraphFormat defaultParagraphFormat)
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

        var nativeLinks = links.ToArray();
        var linksWithToolTips = nativeLinks.Select(link =>
        {
            var prior = remapped._links.FirstOrDefault(candidate =>
                candidate.Start == link.Start &&
                candidate.Length == link.Length &&
                string.Equals(candidate.Target, link.Target, StringComparison.Ordinal));
            return prior is null ? link : link with { ToolTip = prior.ToolTip };
        });
        return new RichTextDocument(
            text,
            runs,
            paragraphs,
            linksWithToolTips,
            remapped._fields,
            images,
            defaultCharacterFormat,
            defaultParagraphFormat,
            _metadata);
    }

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
                AddRun(result, new RichTextRun(position, run.Start - position, defaultFormat));
            }

            AddRun(result, run with { Format = Validate(run.Format) });
            position = run.End;
        }

        if (position < textLength)
        {
            AddRun(result, new RichTextRun(position, textLength - position, defaultFormat));
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
            .Select(start => new RichTextParagraph(
                start,
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

        var list = format.List;
        if (list is not null &&
            (list.Id <= 0 || list.Level is < 0 or > 8 || list.StartAt <= 0 ||
             string.IsNullOrEmpty(list.BulletText)))
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
