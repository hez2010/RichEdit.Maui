using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RichEdit.Core;

/// <summary>An authored underline supported by standard RTF controls.</summary>
public enum RichUnderline
{
    /// <summary>No underline.</summary>
    None,
    /// <summary>A continuous single underline.</summary>
    Single,
    /// <summary>A continuous double underline.</summary>
    Double,
    /// <summary>A dotted underline.</summary>
    Dotted,
    /// <summary>A dashed underline.</summary>
    Dashed,
    /// <summary>A wavy underline.</summary>
    Wave,
    /// <summary>Underline words while leaving intervening spaces undecorated.</summary>
    Words,
    /// <summary>Alternating dashes and dots.</summary>
    DashDot,
    /// <summary>A dash followed by two dots.</summary>
    DashDotDot,
    /// <summary>A thick continuous underline.</summary>
    Thick,
    /// <summary>Two wavy underline strokes.</summary>
    DoubleWave,
    /// <summary>A heavy wavy underline.</summary>
    HeavyWave,
    /// <summary>A long-dashed underline.</summary>
    LongDash,
}

/// <summary>An authored strike-through supported by RTF.</summary>
public enum RichStrike
{
    /// <summary>No strike-through.</summary>
    None,
    /// <summary>One strike-through line.</summary>
    Single,
    /// <summary>Two strike-through lines.</summary>
    Double,
}

/// <summary>An authored script position.</summary>
public enum RichScript
{
    /// <summary>Ordinary baseline position.</summary>
    Normal,
    /// <summary>Raised superscript.</summary>
    Superscript,
    /// <summary>Lowered subscript.</summary>
    Subscript,
}

/// <summary>An authored writing direction.</summary>
public enum RichDirection
{
    /// <summary>Use the text engine's default direction.</summary>
    Automatic,
    /// <summary>Left to right.</summary>
    LeftToRight,
    /// <summary>Right to left.</summary>
    RightToLeft,
}

/// <summary>Paragraph alignment in the document model.</summary>
public enum RichAlignment
{
    /// <summary>Align to the left.</summary>
    Left,
    /// <summary>Align to the right.</summary>
    Right,
    /// <summary>Center the paragraph.</summary>
    Center,
    /// <summary>Justify the paragraph.</summary>
    Justify,
    /// <summary>Distribute character spacing across the full line width.</summary>
    Distributed,
}

/// <summary>The kind of content that can reference a style.</summary>
public enum RichStyleKind
{
    /// <summary>Character formatting applied to inline content.</summary>
    Character,
    /// <summary>Paragraph formatting and its character defaults.</summary>
    Paragraph,
}

/// <summary>A reusable document-owned style with explicit inheritance.</summary>
public sealed record RichStyleDefinition(RichStyleId Id, string Name, RichStyleKind Kind)
{
    /// <summary>Gets the optional base style of the same kind.</summary>
    public RichStyleId? BasedOn { get; init; }
    /// <summary>Gets the paragraph style to use after splitting a paragraph. Null retains this style.</summary>
    public RichStyleId? NextStyle { get; init; }
    /// <summary>Gets cascading character properties.</summary>
    public RichCharacterStyle Character { get; init; } = new();
    /// <summary>Gets cascading paragraph properties, for paragraph styles.</summary>
    public RichParagraphStyle Paragraph { get; init; } = new();
}

/// <summary>An immutable, validated style graph. Resolved ancestry is cached without changing authored content.</summary>
public sealed class RichStyleSheet
{
    private readonly ConcurrentDictionary<RichStyleId, (RichCharacterStyle Character, RichParagraphStyle Paragraph)> _resolved = new();
    /// <summary>Gets an empty style graph.</summary>
    public static RichStyleSheet Empty { get; } = new([]);

    /// <summary>Copies definitions and validates identities, references, kinds, values, and inheritance cycles.</summary>
    public RichStyleSheet(IEnumerable<RichStyleDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var builder = ImmutableDictionary.CreateBuilder<RichStyleId, RichStyleDefinition>();
        foreach (var style in definitions)
        {
            ArgumentNullException.ThrowIfNull(style);
            if (!style.Id.IsValid || style.Id.Value > short.MaxValue || !Enum.IsDefined(style.Kind) ||
                string.IsNullOrWhiteSpace(style.Name) || style.Name.ContainsAny('\r', '\n', '\0'))
                throw new ArgumentException("Styles need an RTF-compatible identity, valid kind, and nonempty single-line name.", nameof(definitions));
            ArgumentNullException.ThrowIfNull(style.Character);
            ArgumentNullException.ThrowIfNull(style.Paragraph);
            Validate(style.Character);
            Validate(style.Paragraph);
            if (style.Kind == RichStyleKind.Character && (style.NextStyle is not null || style.Paragraph != new RichParagraphStyle()))
                throw new ArgumentException("Character styles cannot define paragraph properties or a next paragraph style.", nameof(definitions));
            if (!builder.TryAdd(style.Id, style)) throw new ArgumentException("Style identities must be unique.", nameof(definitions));
        }
        Definitions = builder.ToImmutable();
        foreach (var style in Definitions.Values)
        {
            if (style.BasedOn is { } parent && (!Definitions.TryGetValue(parent, out var inherited) || inherited.Kind != style.Kind))
                throw new ArgumentException("A base style must exist and have the same kind.", nameof(definitions));
            if (style.NextStyle is { } next && (!Definitions.TryGetValue(next, out var following) || following.Kind != RichStyleKind.Paragraph))
                throw new ArgumentException("The next style must be an existing paragraph style.", nameof(definitions));
        }
        var complete = new HashSet<RichStyleId>();
        foreach (var style in Definitions.Values)
        {
            if (complete.Contains(style.Id)) continue;
            var path = new HashSet<RichStyleId>();
            for (RichStyleDefinition? current = style; current is not null && !complete.Contains(current.Id);
                current = current.BasedOn is { } id ? Definitions[id] : null)
                if (!path.Add(current.Id)) throw new ArgumentException("Style inheritance cannot contain cycles.", nameof(definitions));
            complete.UnionWith(path);
        }
    }

    /// <summary>Gets definitions indexed by document-local identity.</summary>
    public ImmutableDictionary<RichStyleId, RichStyleDefinition> Definitions { get; }

    /// <summary>Resolves document defaults, paragraph style and its direct character defaults, character style, and direct inline overrides in that order.</summary>
    public ResolvedCharacterStyle ResolveCharacter(RichStyleId? paragraphStyle = null, RichStyleId? characterStyle = null,
        RichCharacterStyle? direct = null, RichCharacterStyle? defaults = null, RichCharacterStyle? paragraphDirect = null)
    {
        if (defaults is not null) Validate(defaults);
        if (direct is not null) Validate(direct);
        if (paragraphDirect is not null) Validate(paragraphDirect);
        var result = (defaults ?? new()).Apply(new());
        if (paragraphStyle is { } paragraph) result = Resolve(paragraph, RichStyleKind.Paragraph).Character.Apply(result);
        if (paragraphDirect is not null) result = paragraphDirect.Apply(result);
        if (characterStyle is { } character) result = Resolve(character, RichStyleKind.Character).Character.Apply(result);
        return direct?.Apply(result) ?? result;
    }

    /// <summary>Resolves document defaults, paragraph style, and direct paragraph overrides in that order.</summary>
    public ResolvedParagraphStyle ResolveParagraph(RichStyleId? style = null, RichParagraphStyle? direct = null, RichParagraphStyle? defaults = null)
    {
        if (defaults is not null) Validate(defaults);
        if (direct is not null) Validate(direct);
        var result = (defaults ?? new()).Apply(new());
        if (style is { } id) result = Resolve(id, RichStyleKind.Paragraph).Paragraph.Apply(result);
        return direct?.Apply(result) ?? result;
    }

    private (RichCharacterStyle Character, RichParagraphStyle Paragraph) Resolve(RichStyleId id, RichStyleKind kind)
    {
        if (!Definitions.TryGetValue(id, out var definition) || definition.Kind != kind)
            throw new ArgumentException("The style must exist and match the requested content kind.", nameof(id));
        if (_resolved.TryGetValue(id, out var result)) return result;
        var ancestors = new Stack<RichStyleDefinition>();
        for (var current = definition; ; current = Definitions[current.BasedOn!.Value])
        {
            if (_resolved.TryGetValue(current.Id, out result)) break;
            ancestors.Push(current);
            if (current.BasedOn is null) { result = (new(), new()); break; }
        }
        while (ancestors.TryPop(out var current))
        {
            result = (result.Character.Overlay(current.Character), result.Paragraph.Overlay(current.Paragraph));
            _resolved.TryAdd(current.Id, result);
        }
        return result;
    }

    internal static void Validate(RichCharacterStyle style)
    {
        var value = style.Apply(new());
        if (value.FontSize is { HalfPoints: <= 0 } || value.FontFamily is { } family && (string.IsNullOrWhiteSpace(family) || family.ContainsAny('\r', '\n', '\0')) ||
            !Enum.IsDefined(value.Underline) || !Enum.IsDefined(value.Strike) || !Enum.IsDefined(value.Script) || !Enum.IsDefined(value.Direction) || value.HorizontalScalePercent <= 0)
            throw new ArgumentException("Invalid authored character properties.", nameof(style));
        if (value.Language is { } language)
        {
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("Use null for an unspecified language.", nameof(style));
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            if (culture.LCID is <= 0 or > ushort.MaxValue || culture.LCID == 4096)
                throw new ArgumentException("The language must have a defined RTF language identifier.", nameof(style));
        }
    }

    internal static void Validate(RichParagraphStyle style)
    {
        var value = style.Apply(new());
        if (!Enum.IsDefined(value.Alignment) || !Enum.IsDefined(value.Direction) || value.HeadingLevel is < 1 or > 9 ||
            value.SpaceBefore.Twips < 0 || value.SpaceAfter.Twips < 0 || value.TabStops is null)
            throw new ArgumentException("Invalid authored paragraph properties.", nameof(style));
        value.Border?.Validate();
    }
}

internal static class RichStringValidation
{
    internal static bool ContainsAny(this string value, char first, char second, char third) => value.AsSpan().ContainsAny(first, second, third);
}
