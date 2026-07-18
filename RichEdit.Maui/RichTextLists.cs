using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Identifies one list instance in a rich-text document.
/// </summary>
public readonly record struct RichTextListId
{
    /// <summary>
    /// Initializes a list identifier.
    /// </summary>
    /// <param name="value">A positive document-local identifier.</param>
    public RichTextListId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    /// <summary>Gets the positive document-local identifier.</summary>
    public int Value { get; }
}

/// <summary>
/// Specifies the numbering sequence used by a numbered list level.
/// </summary>
public enum RichTextListNumberStyle
{
    /// <summary>Arabic decimal numbers.</summary>
    Arabic,

    /// <summary>Uppercase Roman numerals.</summary>
    UpperRoman,

    /// <summary>Lowercase Roman numerals.</summary>
    LowerRoman,

    /// <summary>Uppercase Latin letters.</summary>
    UpperLetter,

    /// <summary>Lowercase Latin letters.</summary>
    LowerLetter,
}

/// <summary>
/// Defines the marker produced for one list level.
/// </summary>
public abstract record RichTextListMarker
{
    private RichTextListMarker()
    {
    }

    /// <summary>Defines a caller-selected text bullet.</summary>
    public sealed record Bullet : RichTextListMarker
    {
        /// <summary>
        /// Initializes a text bullet marker.
        /// </summary>
        /// <param name="text">The nonempty marker text.</param>
        public Bullet(string text)
        {
            ArgumentException.ThrowIfNullOrEmpty(text);
            Text = text;
        }

        /// <summary>Gets the caller-selected marker text.</summary>
        public string Text { get; }
    }

    /// <summary>Defines a numbered marker and its initial counter.</summary>
    public sealed record Number : RichTextListMarker
    {
        /// <summary>
        /// Initializes a numbered marker.
        /// </summary>
        /// <param name="style">The numbering sequence.</param>
        /// <param name="startAt">The positive initial value.</param>
        public Number(RichTextListNumberStyle style, int startAt)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startAt);
            Style = style;
            StartAt = startAt;
        }

        /// <summary>Gets the numbering sequence.</summary>
        public RichTextListNumberStyle Style { get; }

        /// <summary>Gets the positive initial value.</summary>
        public int StartAt { get; }
    }

    /// <summary>Defines a picture marker and portable text fallback.</summary>
    public sealed record Picture : RichTextListMarker
    {
        /// <summary>
        /// Initializes a picture marker.
        /// </summary>
        /// <param name="pictureId">The owned list-picture identifier.</param>
        /// <param name="fallbackText">The nonempty fallback marker text.</param>
        public Picture(string pictureId, string fallbackText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pictureId);
            ArgumentException.ThrowIfNullOrEmpty(fallbackText);
            PictureId = pictureId;
            FallbackText = fallbackText;
        }

        /// <summary>Gets the owned list-picture identifier.</summary>
        public string PictureId { get; }

        /// <summary>Gets the portable fallback marker text.</summary>
        public string FallbackText { get; }
    }
}

/// <summary>
/// Defines the marker and layout of one nesting level.
/// </summary>
public sealed record RichTextListLevelDefinition
{
    /// <summary>Gets the caller-selected marker definition.</summary>
    public required RichTextListMarker Marker { get; init; }

    /// <summary>Gets the text placed before the marker value.</summary>
    public required string Prefix { get; init; }

    /// <summary>Gets the text placed after the marker value.</summary>
    public required string Suffix { get; init; }

    /// <summary>Gets the leading paragraph indent in points.</summary>
    public double LeadingIndent { get; init; }

    /// <summary>Gets the first-line or hanging indent in points.</summary>
    public double FirstLineIndent { get; init; }

    /// <summary>Gets the marker tab position in points.</summary>
    public double MarkerTab { get; init; }
}

/// <summary>
/// Defines all levels of a reusable list instance.
/// </summary>
public sealed record RichTextListDefinition
{
    /// <summary>
    /// Initializes a list definition.
    /// </summary>
    /// <param name="levels">Between one and nine caller-defined levels.</param>
    public RichTextListDefinition(IEnumerable<RichTextListLevelDefinition> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        Levels = [.. levels];
        if (Levels.Length is < 1 or > 9 || Levels.Any(static level => level is null))
        {
            throw new ArgumentException(
                "A list definition must contain between one and nine levels.",
                nameof(levels));
        }

        foreach (var level in Levels)
        {
            ArgumentNullException.ThrowIfNull(level.Marker);
            ArgumentNullException.ThrowIfNull(level.Prefix);
            ArgumentNullException.ThrowIfNull(level.Suffix);
            if (!double.IsFinite(level.LeadingIndent) ||
                !double.IsFinite(level.FirstLineIndent) ||
                !double.IsFinite(level.MarkerTab) ||
                level.MarkerTab < 0)
            {
                throw new ArgumentException("List level layout values are invalid.", nameof(levels));
            }
        }
    }

    /// <summary>Gets the ordered level definitions.</summary>
    public ImmutableArray<RichTextListLevelDefinition> Levels { get; }

    /// <inheritdoc />
    public bool Equals(RichTextListDefinition? other) =>
        ReferenceEquals(this, other) ||
        other is not null && Levels.AsSpan().SequenceEqual(other.Levels.AsSpan());

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var level in Levels)
        {
            hash.Add(level);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Associates one paragraph with a document list instance and nesting level.
/// </summary>
public sealed record RichTextListItemFormat
{
    /// <summary>
    /// Initializes list-item state.
    /// </summary>
    /// <param name="listId">The existing document list identifier.</param>
    /// <param name="level">The zero-based nesting level.</param>
    /// <param name="restartAt">An optional positive counter restart value.</param>
    public RichTextListItemFormat(
        RichTextListId listId,
        int level,
        int? restartAt = null)
    {
        if (level is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        if (restartAt is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restartAt));
        }

        ListId = listId;
        Level = level;
        RestartAt = restartAt;
    }

    /// <summary>Gets the document-local list identifier.</summary>
    public RichTextListId ListId { get; }

    /// <summary>Gets the zero-based nesting level.</summary>
    public int Level { get; }

    /// <summary>Gets an optional positive counter restart value.</summary>
    public int? RestartAt { get; }
}

internal static class RichTextListConversions
{
    public static RichTextListFormat ToNative(
        RichTextListId listId,
        int level,
        int? restartAt,
        RichTextListDefinition definition)
    {
        if ((uint)level >= (uint)definition.Levels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var levelDefinition = definition.Levels[level];
        var native = new RichTextListFormat
        {
            Id = listId.Value,
            Level = level,
            Prefix = levelDefinition.Prefix,
            Suffix = levelDefinition.Suffix,
            Restart = restartAt is not null,
        };
        return levelDefinition.Marker switch
        {
            RichTextListMarker.Bullet bullet => native with
            {
                Kind = RichListKind.Bulleted,
                BulletText = bullet.Text,
                StartAt = 1,
            },
            RichTextListMarker.Picture picture => native with
            {
                Kind = RichListKind.Bulleted,
                BulletText = picture.FallbackText,
                PictureId = picture.PictureId,
                StartAt = 1,
            },
            RichTextListMarker.Number number => native with
            {
                Kind = RichListKind.Numbered,
                NumberStyle = ToNativeNumberStyle(number.Style),
                StartAt = restartAt ?? number.StartAt,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(definition)),
        };
    }

    public static RichTextListItemFormat ToItem(RichTextListFormat format) =>
        new(
            new RichTextListId(format.Id),
            format.Level,
            format.Restart ? format.StartAt : null);

    public static RichTextListLevelDefinition ToLevel(RichTextListFormat format) =>
        new()
        {
            Marker = format.Kind == RichListKind.Numbered
                ? new RichTextListMarker.Number(
                    FromNativeNumberStyle(format.NumberStyle),
                    format.Restart ? 1 : format.StartAt)
                : format.PictureId is { } pictureId
                    ? new RichTextListMarker.Picture(pictureId, format.BulletText)
                    : new RichTextListMarker.Bullet(format.BulletText),
            Prefix = format.Prefix,
            Suffix = format.Suffix,
        };

    public static RichListNumberStyle ToNativeNumberStyle(RichTextListNumberStyle style) =>
        style switch
        {
            RichTextListNumberStyle.UpperRoman => RichListNumberStyle.UpperRoman,
            RichTextListNumberStyle.LowerRoman => RichListNumberStyle.LowerRoman,
            RichTextListNumberStyle.UpperLetter => RichListNumberStyle.UpperLetter,
            RichTextListNumberStyle.LowerLetter => RichListNumberStyle.LowerLetter,
            _ => RichListNumberStyle.Arabic,
        };

    public static RichTextListNumberStyle FromNativeNumberStyle(RichListNumberStyle style) =>
        style switch
        {
            RichListNumberStyle.UpperRoman => RichTextListNumberStyle.UpperRoman,
            RichListNumberStyle.LowerRoman => RichTextListNumberStyle.LowerRoman,
            RichListNumberStyle.UpperLetter => RichTextListNumberStyle.UpperLetter,
            RichListNumberStyle.LowerLetter => RichTextListNumberStyle.LowerLetter,
            _ => RichTextListNumberStyle.Arabic,
        };
}
