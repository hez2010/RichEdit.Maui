using System.Collections.Immutable;

namespace RichEdit.Maui;

public sealed partial class RichTextDocumentSnapshot
{
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

            var format = Validate(run.Format);
            AddRun(result, ReferenceEquals(format, run.Format) ? run : run with { Format = format });
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
            if (position != 0)
            {
                return position;
            }

            var length = first.Length.CompareTo(second.Length);
            return length != 0 ? length : first.Id.Value.CompareTo(second.Id.Value);
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
        ImmutableDictionary<RichTextListId, RichTextListDefinition> lists)
    {
        if (paragraphs.All(static paragraph => paragraph.Format.List is null && paragraph.Format.NativeList is null))
            return paragraphs;

        return [.. paragraphs.Select(paragraph =>
        {
            var format = BindParagraphList(paragraph.Format, lists);
            return ReferenceEquals(format, paragraph.Format) ? paragraph : paragraph with { Format = format };
        })];
    }

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
            return format;
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

    internal static RichTextCharacterFormat Validate(RichTextCharacterFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
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

        var foreground = NormalizeVisibleColor(format.ForegroundColor);
        var background = NormalizeVisibleColor(format.BackgroundColor);
        var underline = NormalizeVisibleColor(format.UnderlineColor);
        var strikethrough = NormalizeVisibleColor(format.StrikethroughColor);
        var shadingForeground = NormalizeVisibleColor(format.ShadingForegroundColor);
        var shadingBackground = NormalizeVisibleColor(format.ShadingBackgroundColor);
        if (ReferenceEquals(foreground, format.ForegroundColor) && ReferenceEquals(background, format.BackgroundColor) &&
            ReferenceEquals(underline, format.UnderlineColor) && ReferenceEquals(strikethrough, format.StrikethroughColor) &&
            ReferenceEquals(shadingForeground, format.ShadingForegroundColor) && ReferenceEquals(shadingBackground, format.ShadingBackgroundColor))
            return format;

        return format with
        {
            ForegroundColor = foreground,
            BackgroundColor = background,
            UnderlineColor = underline,
            StrikethroughColor = strikethrough,
            ShadingForegroundColor = shadingForeground,
            ShadingBackgroundColor = shadingBackground,
        };
    }

    internal static RichTextParagraphFormat Validate(RichTextParagraphFormat format)
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

        var orderedTabs = !format.TabStops.IsDefault;
        for (var index = 1; index < tabs.Length; index++)
            orderedTabs &= tabs[index - 1].Position < tabs[index].Position;

        var background = NormalizeVisibleColor(format.BackgroundColor);
        var shadingForeground = NormalizeVisibleColor(format.ShadingForegroundColor);
        var shadingBackground = NormalizeVisibleColor(format.ShadingBackgroundColor);
        var borderColor = NormalizeVisibleColor(format.Border?.Color);
        if (orderedTabs && ReferenceEquals(background, format.BackgroundColor) && ReferenceEquals(shadingForeground, format.ShadingForegroundColor) &&
            ReferenceEquals(shadingBackground, format.ShadingBackgroundColor) && ReferenceEquals(borderColor, format.Border?.Color))
            return format;

        return format with
        {
            TabStops = orderedTabs ? tabs : [.. tabs.OrderBy(tab => tab.Position).DistinctBy(tab => tab.Position)],
            BackgroundColor = background,
            ShadingForegroundColor = shadingForeground,
            ShadingBackgroundColor = shadingBackground,
            Border = format.Border is { } visibleBorder
                ? ReferenceEquals(borderColor, visibleBorder.Color) ? visibleBorder : visibleBorder with { Color = borderColor }
                : null,
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
}
