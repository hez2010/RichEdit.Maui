using System.Collections.Immutable;

namespace RichEdit.Maui;

public enum RichTextScript
{
    Normal,
    Superscript,
    Subscript,
}

public enum RichTextUnderlineStyle
{
    None,
    Single,
    Words,
    Double,
    Dotted,
    Dash,
    DashDot,
    DashDotDot,
    Wave,
    Thick,
    DoubleWave,
    HeavyWave,
    LongDash,
}

public enum RichTextStrikethroughStyle
{
    None,
    Single,
    Double,
}

public enum RichTextDirection
{
    Automatic,
    LeftToRight,
    RightToLeft,
}

public enum RichTextFeatureMode
{
    Automatic,
    Disabled,
    Enabled,
}

public enum RichTextAlignment
{
    Left,
    Center,
    Right,
    Justified,
    Distributed,
}

public enum RichTextLineSpacingRule
{
    Automatic,
    Single,
    OneAndHalf,
    Double,
    AtLeast,
    Exactly,
    Multiple,
}

public enum RichTextTabAlignment
{
    Left,
    Center,
    Right,
    Decimal,
}

public enum RichTextTabLeader
{
    None,
    Dots,
    Hyphens,
    Underline,
    ThickLine,
    Equals,
}

public enum RichListKind
{
    Bulleted,
    Numbered,
}

public enum RichListNumberStyle
{
    Arabic,
    UpperRoman,
    LowerRoman,
    UpperLetter,
    LowerLetter,
}

public enum RichTextImageVerticalAlignment
{
    Baseline,
    Bottom,
    Center,
    Top,
}

[Flags]
public enum RichTextBorderSides
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom,
}

public enum RichTextBorderStyle
{
    None,
    Single,
    Double,
    Dotted,
    Dashed,
}

public sealed record RichTextCharacterFormat
{
    public static RichTextCharacterFormat Default { get; } = new();

    public string? FontFamily { get; init; }

    public double? FontSize { get; init; }

    public int FontWeight { get; init; } = 400;

    public bool Italic { get; init; }

    public RichTextUnderlineStyle Underline { get; init; }

    public Color? UnderlineColor { get; init; }

    public RichTextStrikethroughStyle Strikethrough { get; init; }

    public Color? StrikethroughColor { get; init; }

    public Color? ForegroundColor { get; init; }

    public Color? BackgroundColor { get; init; }

    public RichTextScript Script { get; init; }

    public double BaselineOffset { get; init; }

    public double CharacterSpacing { get; init; }

    public double HorizontalScale { get; init; } = 1d;

    public bool SmallCaps { get; init; }

    public bool AllCaps { get; init; }

    public bool Outline { get; init; }

    public bool Shadow { get; init; }

    public bool Hidden { get; init; }

    public string? LanguageTag { get; init; }

    public RichTextDirection Direction { get; init; }

    public RichTextFeatureMode Kerning { get; init; }

    public RichTextFeatureMode Ligatures { get; init; }

    public int Shading { get; init; }

    public Color? ShadingForegroundColor { get; init; }

    public Color? ShadingBackgroundColor { get; init; }

    public string? StyleName { get; init; }

    public bool Bold => FontWeight >= 600;
}

public sealed record RichTextRun(int Start, int Length, RichTextCharacterFormat Format)
{
    public int End => checked(Start + Length);
}

public sealed record RichTextTabStop(
    double Position,
    RichTextTabAlignment Alignment = RichTextTabAlignment.Left,
    RichTextTabLeader Leader = RichTextTabLeader.None);

public sealed record RichTextListFormat
{
    public int Id { get; init; }

    public int Level { get; init; }

    public RichListKind Kind { get; init; }

    public RichListNumberStyle NumberStyle { get; init; }

    public int StartAt { get; init; } = 1;

    public bool Restart { get; init; }

    public string Prefix { get; init; } = string.Empty;

    public string Suffix { get; init; } = ".";

    public string BulletText { get; init; } = "•";

    public string? PictureId { get; init; }
}

public sealed record RichTextBorder(
    RichTextBorderSides Sides,
    RichTextBorderStyle Style,
    double Width,
    Color? Color = null);

public sealed record RichTextParagraphFormat
{
    public static RichTextParagraphFormat Default { get; } = new();

    public RichTextAlignment Alignment { get; init; }

    public RichTextDirection Direction { get; init; }

    public double LeadingIndent { get; init; }

    public double TrailingIndent { get; init; }

    public double FirstLineIndent { get; init; }

    public double SpaceBefore { get; init; }

    public double SpaceAfter { get; init; }

    public RichTextLineSpacingRule LineSpacingRule { get; init; }

    public double LineSpacing { get; init; }

    public double? MinimumLineHeight { get; init; }

    public double? MaximumLineHeight { get; init; }

    public ImmutableArray<RichTextTabStop> TabStops { get; init; } = [];

    public bool Hyphenation { get; init; }

    public Color? BackgroundColor { get; init; }

    public int Shading { get; init; }

    public Color? ShadingForegroundColor { get; init; }

    public Color? ShadingBackgroundColor { get; init; }

    public RichTextBorder? Border { get; init; }

    public string? StyleName { get; init; }

    public RichTextListFormat? List { get; init; }

    public bool Equals(RichTextParagraphFormat? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Alignment == other.Alignment &&
        Direction == other.Direction &&
        LeadingIndent.Equals(other.LeadingIndent) &&
        TrailingIndent.Equals(other.TrailingIndent) &&
        FirstLineIndent.Equals(other.FirstLineIndent) &&
        SpaceBefore.Equals(other.SpaceBefore) &&
        SpaceAfter.Equals(other.SpaceAfter) &&
        LineSpacingRule == other.LineSpacingRule &&
        LineSpacing.Equals(other.LineSpacing) &&
        Nullable.Equals(MinimumLineHeight, other.MinimumLineHeight) &&
        Nullable.Equals(MaximumLineHeight, other.MaximumLineHeight) &&
        TabStops.AsSpan().SequenceEqual(other.TabStops.AsSpan()) &&
        Hyphenation == other.Hyphenation &&
        Equals(BackgroundColor, other.BackgroundColor) &&
        Shading == other.Shading &&
        Equals(ShadingForegroundColor, other.ShadingForegroundColor) &&
        Equals(ShadingBackgroundColor, other.ShadingBackgroundColor) &&
        Border == other.Border &&
        string.Equals(StyleName, other.StyleName, StringComparison.Ordinal) &&
        List == other.List;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Alignment);
        hash.Add(Direction);
        hash.Add(LeadingIndent);
        hash.Add(TrailingIndent);
        hash.Add(FirstLineIndent);
        hash.Add(SpaceBefore);
        hash.Add(SpaceAfter);
        hash.Add(LineSpacingRule);
        hash.Add(LineSpacing);
        hash.Add(MinimumLineHeight);
        hash.Add(MaximumLineHeight);
        foreach (var tabStop in TabStops)
        {
            hash.Add(tabStop);
        }

        hash.Add(Hyphenation);
        hash.Add(BackgroundColor);
        hash.Add(Shading);
        hash.Add(ShadingForegroundColor);
        hash.Add(ShadingBackgroundColor);
        hash.Add(Border);
        hash.Add(StyleName, StringComparer.Ordinal);
        hash.Add(List);
        return hash.ToHashCode();
    }
}

public sealed record RichTextParagraph(int Start, RichTextParagraphFormat Format);

public sealed record RichTextLink(
    int Start,
    int Length,
    string Target,
    string? ToolTip = null)
{
    public int End => checked(Start + Length);
}

public sealed record RichTextField(
    int Start,
    int Length,
    string Instruction)
{
    public int End => checked(Start + Length);
}

public readonly record struct RichTextImageCrop(
    double Left,
    double Top,
    double Right,
    double Bottom);

public sealed record RichTextImage
{
    public int Position { get; init; }

    public string MediaType { get; init; } = "image/png";

    public ImmutableArray<byte> Data { get; init; } = [];

    public string? Source { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public RichTextImageVerticalAlignment VerticalAlignment { get; init; }

    public string? AlternativeText { get; init; }

    public double Rotation { get; init; }

    public RichTextImageCrop Crop { get; init; }

    public static RichTextImage FromBytes(
        int position,
        string mediaType,
        ReadOnlySpan<byte> data,
        double width,
        double height,
        RichTextImageVerticalAlignment verticalAlignment = RichTextImageVerticalAlignment.Baseline,
        string? alternativeText = null) =>
        new()
        {
            Position = position,
            MediaType = mediaType,
            Data = ImmutableArray.CreateRange(data.ToArray()),
            Width = width,
            Height = height,
            VerticalAlignment = verticalAlignment,
            AlternativeText = alternativeText,
        };
}
