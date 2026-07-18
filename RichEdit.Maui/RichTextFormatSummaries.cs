using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Describes one formatting property across a document range without using a
/// sentinel value for mixed or inherited state.
/// </summary>
/// <typeparam name="T">The formatting-property value type.</typeparam>
public readonly record struct RichTextFormatValue<T>
{
    internal RichTextFormatValue(T value, bool isMixed, bool isInherited)
    {
        Value = value;
        IsMixed = isMixed;
        IsInherited = isInherited;
    }

    /// <summary>
    /// Gets the first value in document order. Inspect <see cref="IsMixed"/>
    /// before treating it as uniform.
    /// </summary>
    public T Value { get; }

    /// <summary>Gets whether the inspected values differ.</summary>
    public bool IsMixed { get; }

    /// <summary>Gets whether every inspected authored value is inherited.</summary>
    public bool IsInherited { get; }
}

/// <summary>
/// Summarizes the declared character formats present in a document range.
/// </summary>
public sealed class RichTextCharacterFormatSummary
{
    private readonly RichTextCharacterFormat _defaultFormat;

    private RichTextCharacterFormatSummary(
        ImmutableArray<RichTextCharacterFormat> formats,
        RichTextCharacterFormat defaultFormat)
    {
        Formats = formats;
        _defaultFormat = defaultFormat;
        RepresentativeFormat = formats[0];
        UniformFormat = formats.Length == 1 ? formats[0] : null;
    }

    /// <summary>
    /// Gets the first format in the range. Use <see cref="IsMixed"/> before treating
    /// this value as uniform.
    /// </summary>
    public RichTextCharacterFormat RepresentativeFormat { get; }

    /// <summary>
    /// Gets the uniform format, or null when the range contains different complete
    /// character formats.
    /// </summary>
    public RichTextCharacterFormat? UniformFormat { get; }

    /// <summary>Gets the distinct declared formats in document order.</summary>
    public IReadOnlyList<RichTextCharacterFormat> Formats { get; }

    /// <summary>Gets a value indicating whether the complete formats differ.</summary>
    public bool IsMixed => Formats.Count > 1;

    /// <summary>Gets authored font-family state.</summary>
    public RichTextFormatValue<string?> FontFamily =>
        CreateValue(static format => format.FontFamily, static value => value is null);

    /// <summary>Gets font-family state resolved through the document default.</summary>
    public RichTextFormatValue<string?> EffectiveFontFamily => CreateValue(
        format => format.FontFamily ?? _defaultFormat.FontFamily,
        static _ => false,
        Formats.All(static format => format.FontFamily is null));

    /// <summary>Gets authored font-size state.</summary>
    public RichTextFormatValue<double?> FontSize =>
        CreateValue(static format => format.FontSize, static value => value is null);

    /// <summary>Gets font-size state resolved through the document default.</summary>
    public RichTextFormatValue<double?> EffectiveFontSize => CreateValue(
        format => format.FontSize ?? _defaultFormat.FontSize,
        static _ => false,
        Formats.All(static format => format.FontSize is null));

    /// <summary>Gets authored foreground-color state.</summary>
    public RichTextFormatValue<Color?> ForegroundColor =>
        CreateValue(static format => format.ForegroundColor, static value => value is null);

    /// <summary>Gets foreground-color state resolved through the document default.</summary>
    public RichTextFormatValue<Color?> EffectiveForegroundColor => CreateValue(
        format => format.ForegroundColor ?? _defaultFormat.ForegroundColor,
        static _ => false,
        Formats.All(static format => format.ForegroundColor is null));

    /// <summary>
    /// Summarizes any character-format property selected by the caller.
    /// </summary>
    /// <typeparam name="T">The selected property type.</typeparam>
    /// <param name="selector">A property selector evaluated for each distinct format.</param>
    /// <returns>The representative and mixed state for the selected property.</returns>
    public RichTextFormatValue<T> GetValue<T>(Func<RichTextCharacterFormat, T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return CreateValue(selector, static _ => false);
    }

    internal static RichTextCharacterFormatSummary Create(
        RichTextDocumentSnapshot snapshot,
        RichTextRange range)
    {
        if (range.IsEmpty)
        {
            return new RichTextCharacterFormatSummary(
                [snapshot.GetCaretFormat(range.Start)],
                snapshot.DefaultCharacterFormat);
        }

        var formatsBuilder = ImmutableArray.CreateBuilder<RichTextCharacterFormat>();
        var seen = new HashSet<RichTextCharacterFormat>();
        for (var index = snapshot.FindRunIndex(range.Start);
             index < snapshot.Runs.Length && snapshot.Runs[index].Start < range.End;
             index++)
        {
            var format = snapshot.Runs[index].Format;
            if (seen.Add(format))
            {
                formatsBuilder.Add(format);
            }
        }

        var formats = formatsBuilder.ToImmutable();
        return new RichTextCharacterFormatSummary(
            formats.IsDefaultOrEmpty ? [snapshot.DefaultCharacterFormat] : formats,
            snapshot.DefaultCharacterFormat);
    }

    private RichTextFormatValue<T> CreateValue<T>(
        Func<RichTextCharacterFormat, T> selector,
        Func<T, bool> isInherited,
        bool? inheritedOverride = null)
    {
        var value = selector(Formats[0]);
        var comparer = EqualityComparer<T>.Default;
        var mixed = Formats.Skip(1).Any(format =>
            !comparer.Equals(value, selector(format)));
        var inherited = inheritedOverride ??
            Formats.All(format => isInherited(selector(format)));
        return new RichTextFormatValue<T>(value, mixed, inherited);
    }
}

/// <summary>
/// Summarizes the declared paragraph formats present in a document range.
/// </summary>
public sealed class RichTextParagraphFormatSummary
{
    private RichTextParagraphFormatSummary(ImmutableArray<RichTextParagraphFormat> formats)
    {
        Formats = formats;
        RepresentativeFormat = formats[0];
        UniformFormat = formats.Length == 1 ? formats[0] : null;
    }

    /// <summary>
    /// Gets the first paragraph format in the range. Use <see cref="IsMixed"/> before
    /// treating this value as uniform.
    /// </summary>
    public RichTextParagraphFormat RepresentativeFormat { get; }

    /// <summary>
    /// Gets the uniform format, or null when the selected paragraphs have different
    /// complete formats.
    /// </summary>
    public RichTextParagraphFormat? UniformFormat { get; }

    /// <summary>Gets the distinct declared paragraph formats in document order.</summary>
    public IReadOnlyList<RichTextParagraphFormat> Formats { get; }

    /// <summary>Gets a value indicating whether the complete formats differ.</summary>
    public bool IsMixed => Formats.Count > 1;

    /// <summary>
    /// Summarizes any paragraph-format property selected by the caller.
    /// </summary>
    /// <typeparam name="T">The selected property type.</typeparam>
    /// <param name="selector">A property selector evaluated for each distinct format.</param>
    /// <returns>The representative and mixed state for the selected property.</returns>
    public RichTextFormatValue<T> GetValue<T>(Func<RichTextParagraphFormat, T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var value = selector(Formats[0]);
        var comparer = EqualityComparer<T>.Default;
        return new RichTextFormatValue<T>(
            value,
            Formats.Skip(1).Any(format => !comparer.Equals(value, selector(format))),
            isInherited: false);
    }

    internal static RichTextParagraphFormatSummary Create(
        RichTextDocumentSnapshot snapshot,
        RichTextRange range)
    {
        var lastPosition = range.IsEmpty ? range.Start : range.End - 1;
        var firstParagraphStart = GetParagraphStart(snapshot.Text, range.Start);
        var lastParagraphStart = GetParagraphStart(snapshot.Text, lastPosition);
        var formatsBuilder = ImmutableArray.CreateBuilder<RichTextParagraphFormat>();
        var seen = new HashSet<RichTextParagraphFormat>();
        for (var index = snapshot.FindParagraphIndex(firstParagraphStart);
             index < snapshot.Paragraphs.Length &&
             snapshot.Paragraphs[index].Start <= lastParagraphStart;
             index++)
        {
            var format = snapshot.Paragraphs[index].Format;
            if (seen.Add(format))
            {
                formatsBuilder.Add(format);
            }
        }

        var formats = formatsBuilder.ToImmutable();
        return new RichTextParagraphFormatSummary(
            formats.IsDefaultOrEmpty ? [snapshot.DefaultParagraphFormat] : formats);
    }

    private static int GetParagraphStart(string text, int position) =>
        position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
}
