using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>Specifies the vertical script position of text.</summary>
public enum RichTextScript
{
    /// <summary>Uses the normal text baseline.</summary>
    Normal,
    /// <summary>Uses superscript positioning.</summary>
    Superscript,
    /// <summary>Uses subscript positioning.</summary>
    Subscript,
}

/// <summary>Specifies an underline style.</summary>
public enum RichTextUnderlineStyle
{
    /// <summary>No underline.</summary>
    None,
    /// <summary>A single solid underline.</summary>
    Single,
    /// <summary>Underlines words but not intervening spaces.</summary>
    Words,
    /// <summary>A double solid underline.</summary>
    Double,
    /// <summary>A dotted underline.</summary>
    Dotted,
    /// <summary>A dashed underline.</summary>
    Dash,
    /// <summary>An alternating dash-and-dot underline.</summary>
    DashDot,
    /// <summary>An alternating dash-dot-dot underline.</summary>
    DashDotDot,
    /// <summary>A wavy underline.</summary>
    Wave,
    /// <summary>A thick solid underline.</summary>
    Thick,
    /// <summary>A double wavy underline.</summary>
    DoubleWave,
    /// <summary>A heavy wavy underline.</summary>
    HeavyWave,
    /// <summary>A long-dash underline.</summary>
    LongDash,
}

/// <summary>Specifies a strikethrough style.</summary>
public enum RichTextStrikethroughStyle
{
    /// <summary>No strikethrough.</summary>
    None,
    /// <summary>A single strikethrough line.</summary>
    Single,
    /// <summary>A double strikethrough line.</summary>
    Double,
}

/// <summary>Specifies text or paragraph direction.</summary>
public enum RichTextDirection
{
    /// <summary>Determines direction from content and platform rules.</summary>
    Automatic,
    /// <summary>Uses left-to-right direction.</summary>
    LeftToRight,
    /// <summary>Uses right-to-left direction.</summary>
    RightToLeft,
}

/// <summary>Specifies a three-state typography feature preference.</summary>
public enum RichTextFeatureMode
{
    /// <summary>Delegates the decision to the font or native renderer.</summary>
    Automatic,
    /// <summary>Disables the feature.</summary>
    Disabled,
    /// <summary>Enables the feature.</summary>
    Enabled,
}

/// <summary>Specifies horizontal paragraph alignment.</summary>
public enum RichTextAlignment
{
    /// <summary>Aligns to the leading left edge.</summary>
    Left,
    /// <summary>Centers the paragraph.</summary>
    Center,
    /// <summary>Aligns to the trailing right edge.</summary>
    Right,
    /// <summary>Justifies text between both edges.</summary>
    Justified,
    /// <summary>Distributes text across the available width.</summary>
    Distributed,
}

/// <summary>Specifies how paragraph line spacing is interpreted.</summary>
public enum RichTextLineSpacingRule
{
    /// <summary>Uses the native automatic rule.</summary>
    Automatic,
    /// <summary>Uses single line spacing.</summary>
    Single,
    /// <summary>Uses one-and-a-half line spacing.</summary>
    OneAndHalf,
    /// <summary>Uses double line spacing.</summary>
    Double,
    /// <summary>Uses at least the requested value.</summary>
    AtLeast,
    /// <summary>Uses exactly the requested value.</summary>
    Exactly,
    /// <summary>Uses the requested line-height multiplier.</summary>
    Multiple,
}

/// <summary>Specifies the alignment of text at a tab stop.</summary>
public enum RichTextTabAlignment
{
    /// <summary>Aligns the leading edge at the tab stop.</summary>
    Left,
    /// <summary>Centers text at the tab stop.</summary>
    Center,
    /// <summary>Aligns the trailing edge at the tab stop.</summary>
    Right,
    /// <summary>Aligns the decimal separator at the tab stop.</summary>
    Decimal,
}

/// <summary>Specifies the leader drawn before a tab stop.</summary>
public enum RichTextTabLeader
{
    /// <summary>No leader.</summary>
    None,
    /// <summary>A dotted leader.</summary>
    Dots,
    /// <summary>A hyphen leader.</summary>
    Hyphens,
    /// <summary>An underline leader.</summary>
    Underline,
    /// <summary>A thick-line leader.</summary>
    ThickLine,
    /// <summary>An equals-sign leader.</summary>
    Equals,
}

internal enum RichListKind
{
    Bulleted,
    Numbered,
}

internal enum RichListNumberStyle
{
    Arabic,
    UpperRoman,
    LowerRoman,
    UpperLetter,
    LowerLetter,
}

/// <summary>Specifies the vertical placement of an inline image.</summary>
public enum RichTextImageVerticalAlignment
{
    /// <summary>Aligns the image with the text baseline.</summary>
    Baseline,
    /// <summary>Aligns the image bottom with the line box.</summary>
    Bottom,
    /// <summary>Centers the image in the line box.</summary>
    Center,
    /// <summary>Aligns the image top with the line box.</summary>
    Top,
}

/// <summary>Specifies paragraph-border sides.</summary>
[Flags]
public enum RichTextBorderSides
{
    /// <summary>No border sides.</summary>
    None = 0,
    /// <summary>The left side.</summary>
    Left = 1,
    /// <summary>The top side.</summary>
    Top = 2,
    /// <summary>The right side.</summary>
    Right = 4,
    /// <summary>The bottom side.</summary>
    Bottom = 8,
    /// <summary>All four sides.</summary>
    All = Left | Top | Right | Bottom,
}

/// <summary>Specifies a paragraph-border line style.</summary>
public enum RichTextBorderStyle
{
    /// <summary>No border.</summary>
    None,
    /// <summary>A single solid line.</summary>
    Single,
    /// <summary>A double solid line.</summary>
    Double,
    /// <summary>A dotted line.</summary>
    Dotted,
    /// <summary>A dashed line.</summary>
    Dashed,
}

/// <summary>Defines immutable character formatting.</summary>
public sealed record RichTextCharacterFormat
{
    /// <summary>Gets an inherited, otherwise neutral character format.</summary>
    public static RichTextCharacterFormat Default { get; } = new();

    /// <summary>Gets the authored font family, or null to inherit.</summary>
    public string? FontFamily { get; init; }
    /// <summary>Gets the authored font size in points, or null to inherit.</summary>
    public double? FontSize { get; init; }
    /// <summary>Gets the numeric font weight from 1 through 1000.</summary>
    public int FontWeight { get; init; } = 400;
    /// <summary>Gets whether italic styling is enabled.</summary>
    public bool Italic { get; init; }
    /// <summary>Gets the underline style.</summary>
    public RichTextUnderlineStyle Underline { get; init; }
    /// <summary>Gets the underline color, or null for the text color.</summary>
    public Color? UnderlineColor { get; init; }
    /// <summary>Gets the strikethrough style.</summary>
    public RichTextStrikethroughStyle Strikethrough { get; init; }
    /// <summary>Gets the strikethrough color, or null for the text color.</summary>
    public Color? StrikethroughColor { get; init; }
    /// <summary>Gets the authored foreground color, or null to inherit.</summary>
    public Color? ForegroundColor { get; init; }
    /// <summary>Gets the solid text background color, or null for no highlight.</summary>
    public Color? BackgroundColor { get; init; }
    /// <summary>Gets the superscript or subscript state.</summary>
    public RichTextScript Script { get; init; }
    /// <summary>Gets the additional baseline offset in points.</summary>
    public double BaselineOffset { get; init; }
    /// <summary>Gets additional character spacing in points.</summary>
    public double CharacterSpacing { get; init; }
    /// <summary>Gets the horizontal glyph scale, where 1 is unchanged.</summary>
    public double HorizontalScale { get; init; } = 1d;
    /// <summary>Gets whether small-cap glyphs are requested.</summary>
    public bool SmallCaps { get; init; }
    /// <summary>Gets whether uppercase presentation is requested.</summary>
    public bool AllCaps { get; init; }
    /// <summary>Gets whether the outline effect is requested.</summary>
    public bool Outline { get; init; }
    /// <summary>Gets whether the shadow effect is requested.</summary>
    public bool Shadow { get; init; }
    /// <summary>Gets whether text is hidden.</summary>
    public bool Hidden { get; init; }
    /// <summary>Gets the BCP 47 language tag, or null when unspecified.</summary>
    public string? LanguageTag { get; init; }
    /// <summary>Gets explicit character direction.</summary>
    public RichTextDirection Direction { get; init; }
    /// <summary>Gets the kerning preference.</summary>
    public RichTextFeatureMode Kerning { get; init; }
    /// <summary>Gets the ligature preference.</summary>
    public RichTextFeatureMode Ligatures { get; init; }
    /// <summary>Gets the RTF shading amount from 0 through 10000.</summary>
    public int Shading { get; init; }
    /// <summary>Gets the RTF shading foreground color.</summary>
    public Color? ShadingForegroundColor { get; init; }
    /// <summary>Gets the RTF shading background color.</summary>
    public Color? ShadingBackgroundColor { get; init; }
    /// <summary>Gets the named character style, or null when unspecified.</summary>
    public string? StyleName { get; init; }
    /// <summary>Gets whether <see cref="FontWeight"/> represents a bold weight.</summary>
    public bool Bold => FontWeight >= 600;
}

/// <summary>Associates a document range with one character format.</summary>
public sealed record RichTextRun
{
    /// <summary>Initializes a character-format run.</summary>
    /// <param name="range">The nonempty run range.</param>
    /// <param name="format">The immutable character format.</param>
    public RichTextRun(RichTextRange range, RichTextCharacterFormat format)
    {
        Range = range;
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    internal RichTextRun(int start, int length, RichTextCharacterFormat format)
        : this(new RichTextRange(start, length), format)
    {
    }

    /// <summary>Gets the half-open UTF-16 run range.</summary>
    public RichTextRange Range { get; init; }
    /// <summary>Gets the run's character format.</summary>
    public RichTextCharacterFormat Format { get; init; }
    internal int Start { get => Range.Start; init => Range = new(value, Range.Length); }
    internal int Length { get => Range.Length; init => Range = new(Range.Start, value); }
    internal int End => Range.End;
}

/// <summary>Defines one paragraph tab stop.</summary>
/// <param name="Position">The position in points from the paragraph origin.</param>
/// <param name="Alignment">The alignment applied at the stop.</param>
/// <param name="Leader">The leader drawn before the stop.</param>
public sealed record RichTextTabStop(
    double Position,
    RichTextTabAlignment Alignment = RichTextTabAlignment.Left,
    RichTextTabLeader Leader = RichTextTabLeader.None);

internal sealed record RichTextListFormat
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

/// <summary>Represents an owned image used as a list marker.</summary>
public sealed record RichTextListPicture
{
    /// <summary>Gets the unique document-local picture identifier.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Gets the image media type.</summary>
    public string MediaType { get; init; } = "image/png";
    /// <summary>Gets the immutable encoded image payload.</summary>
    public ImmutableArray<byte> Data { get; init; } = [];
    /// <summary>Gets an optional source identifier or URI.</summary>
    public string? Source { get; init; }
    /// <summary>Gets the requested width in points.</summary>
    public double Width { get; init; }
    /// <summary>Gets the requested height in points.</summary>
    public double Height { get; init; }
    /// <summary>Gets alternative text for the marker image.</summary>
    public string? AlternativeText { get; init; }

    /// <summary>Creates a list picture by copying encoded bytes.</summary>
    /// <param name="id">The nonempty document-local identifier.</param>
    /// <param name="mediaType">The image media type.</param>
    /// <param name="data">The encoded image payload.</param>
    /// <param name="width">The requested width in points.</param>
    /// <param name="height">The requested height in points.</param>
    /// <param name="alternativeText">Optional alternative text.</param>
    /// <returns>An immutable list-picture value.</returns>
    public static RichTextListPicture FromBytes(
        string id,
        string mediaType,
        ReadOnlySpan<byte> data,
        double width,
        double height,
        string? alternativeText = null) =>
        new()
        {
            Id = id,
            MediaType = mediaType,
            Data = ImmutableArray.CreateRange(data.ToArray()),
            Width = width,
            Height = height,
            AlternativeText = alternativeText,
        };
}

/// <summary>Defines a paragraph border.</summary>
/// <param name="Sides">The bordered sides.</param>
/// <param name="Style">The line style.</param>
/// <param name="Width">The line width in points.</param>
/// <param name="Color">The line color, or null to inherit.</param>
public sealed record RichTextBorder(
    RichTextBorderSides Sides,
    RichTextBorderStyle Style,
    double Width,
    Color? Color = null);

/// <summary>Defines immutable paragraph formatting.</summary>
public sealed record RichTextParagraphFormat
{
    /// <summary>Gets the neutral default paragraph format.</summary>
    public static RichTextParagraphFormat Default { get; } = new();
    /// <summary>Gets paragraph alignment.</summary>
    public RichTextAlignment Alignment { get; init; }
    /// <summary>Gets paragraph direction.</summary>
    public RichTextDirection Direction { get; init; }
    /// <summary>Gets leading indent in points.</summary>
    public double LeadingIndent { get; init; }
    /// <summary>Gets trailing indent in points.</summary>
    public double TrailingIndent { get; init; }
    /// <summary>Gets first-line or hanging indent in points.</summary>
    public double FirstLineIndent { get; init; }
    /// <summary>Gets space before the paragraph in points.</summary>
    public double SpaceBefore { get; init; }
    /// <summary>Gets space after the paragraph in points.</summary>
    public double SpaceAfter { get; init; }
    /// <summary>Gets the line-spacing rule.</summary>
    public RichTextLineSpacingRule LineSpacingRule { get; init; }
    /// <summary>Gets the line-spacing rule value.</summary>
    public double LineSpacing { get; init; }
    /// <summary>Gets an optional minimum line height in points.</summary>
    public double? MinimumLineHeight { get; init; }
    /// <summary>Gets an optional maximum line height in points.</summary>
    public double? MaximumLineHeight { get; init; }
    /// <summary>Gets ordered paragraph tab stops.</summary>
    public ImmutableArray<RichTextTabStop> TabStops { get; init; } = [];
    /// <summary>Gets whether hyphenation is requested.</summary>
    public bool Hyphenation { get; init; }
    /// <summary>Gets the solid paragraph background color.</summary>
    public Color? BackgroundColor { get; init; }
    /// <summary>Gets the RTF paragraph-shading amount from 0 through 10000.</summary>
    public int Shading { get; init; }
    /// <summary>Gets the RTF paragraph-shading foreground color.</summary>
    public Color? ShadingForegroundColor { get; init; }
    /// <summary>Gets the RTF paragraph-shading background color.</summary>
    public Color? ShadingBackgroundColor { get; init; }
    /// <summary>Gets paragraph border formatting.</summary>
    public RichTextBorder? Border { get; init; }
    /// <summary>Gets the named paragraph style, or null when unspecified.</summary>
    public string? StyleName { get; init; }
    /// <summary>Gets document-list item state, or null for an ordinary paragraph.</summary>
    public RichTextListItemFormat? List { get; init; }

    internal RichTextListFormat? NativeList { get; init; }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

/// <summary>Associates one paragraph range with its paragraph format.</summary>
public sealed record RichTextParagraph
{
    /// <summary>Initializes a paragraph entry.</summary>
    /// <param name="range">The paragraph's UTF-16 range.</param>
    /// <param name="format">The immutable paragraph format.</param>
    public RichTextParagraph(RichTextRange range, RichTextParagraphFormat format)
    {
        Range = range;
        Format = format ?? throw new ArgumentNullException(nameof(format));
    }

    internal RichTextParagraph(int start, RichTextParagraphFormat format)
        : this(new RichTextRange(start, 0), format)
    {
    }

    /// <summary>Gets the paragraph's half-open UTF-16 range.</summary>
    public RichTextRange Range { get; init; }
    /// <summary>Gets the paragraph format.</summary>
    public RichTextParagraphFormat Format { get; init; }
    internal int Start { get => Range.Start; init => Range = new(value, Range.Length); }
}

/// <summary>Associates a document range with a hyperlink.</summary>
public sealed record RichTextLink
{
    /// <summary>Initializes a hyperlink.</summary>
    /// <param name="range">The nonempty display-text range.</param>
    /// <param name="target">The application-defined target.</param>
    /// <param name="toolTip">Optional tooltip text.</param>
    public RichTextLink(RichTextRange range, string target, string? toolTip = null)
    {
        Range = range;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        ToolTip = toolTip;
    }

    internal RichTextLink(int start, int length, string target, string? toolTip = null)
        : this(new RichTextRange(start, length), target, toolTip)
    {
    }

    /// <summary>Gets the display-text range.</summary>
    public RichTextRange Range { get; init; }
    /// <summary>Gets the application-defined target.</summary>
    public string Target { get; init; }
    /// <summary>Gets optional tooltip text.</summary>
    public string? ToolTip { get; init; }
    internal int Start { get => Range.Start; init => Range = new(value, Range.Length); }
    internal int Length { get => Range.Length; init => Range = new(Range.Start, value); }
    internal int End => Range.End;
}

/// <summary>Identifies one field within a rich-text document.</summary>
public readonly record struct RichTextFieldId
{
    /// <summary>Initializes a field identifier.</summary>
    /// <param name="value">The positive document-local identifier.</param>
    public RichTextFieldId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    /// <summary>Gets the positive document-local identifier.</summary>
    public int Value { get; }
}

/// <summary>Associates a visible result range with an RTF field instruction.</summary>
public sealed record RichTextField
{
    /// <summary>Initializes a field.</summary>
    /// <param name="range">The visible result range.</param>
    /// <param name="instruction">The nonempty RTF field instruction.</param>
    public RichTextField(RichTextRange range, string instruction)
        : this(default, range, instruction)
    {
    }

    /// <summary>Initializes an identified field.</summary>
    /// <param name="id">
    /// A positive document-local identity, or the default value when a snapshot should
    /// allocate one.
    /// </param>
    /// <param name="range">The visible result range.</param>
    /// <param name="instruction">The nonempty RTF field instruction.</param>
    public RichTextField(RichTextFieldId id, RichTextRange range, string instruction)
    {
        Id = id;
        Range = range;
        Instruction = instruction ?? throw new ArgumentNullException(nameof(instruction));
    }

    internal RichTextField(int start, int length, string instruction)
        : this(default, new RichTextRange(start, length), instruction)
    {
    }

    /// <summary>Gets the stable document-local field identity.</summary>
    public RichTextFieldId Id { get; init; }
    /// <summary>Gets the visible result range.</summary>
    public RichTextRange Range { get; init; }
    /// <summary>Gets the RTF field instruction.</summary>
    public string Instruction { get; init; }
    internal int Start { get => Range.Start; init => Range = new(value, Range.Length); }
    internal int Length { get => Range.Length; init => Range = new(Range.Start, value); }
    internal int End => Range.End;
}

/// <summary>Defines cropping amounts in points for an inline image.</summary>
/// <param name="Left">The amount cropped from the left edge, in points.</param>
/// <param name="Top">The amount cropped from the top edge, in points.</param>
/// <param name="Right">The amount cropped from the right edge, in points.</param>
/// <param name="Bottom">The amount cropped from the bottom edge, in points.</param>
public readonly record struct RichTextImageCrop(
    double Left,
    double Top,
    double Right,
    double Bottom);

/// <summary>Represents an owned inline image and its presentation metadata.</summary>
public sealed record RichTextImage
{
    /// <summary>Gets the U+FFFC position occupied by the image.</summary>
    public int Position { get; init; }
    /// <summary>Gets the image media type.</summary>
    public string MediaType { get; init; } = "image/png";
    /// <summary>Gets the immutable encoded image payload.</summary>
    public ImmutableArray<byte> Data { get; init; } = [];
    /// <summary>Gets an optional source identifier or URI.</summary>
    public string? Source { get; init; }
    /// <summary>Gets the requested width in points.</summary>
    public double Width { get; init; }
    /// <summary>Gets the requested height in points.</summary>
    public double Height { get; init; }
    /// <summary>Gets vertical inline alignment.</summary>
    public RichTextImageVerticalAlignment VerticalAlignment { get; init; }
    /// <summary>Gets alternative text.</summary>
    public string? AlternativeText { get; init; }
    /// <summary>Gets clockwise rotation in degrees.</summary>
    public double Rotation { get; init; }
    /// <summary>Gets crop amounts in points.</summary>
    public RichTextImageCrop Crop { get; init; }

    /// <summary>Creates an inline image by copying encoded bytes.</summary>
    /// <param name="position">The U+FFFC document position.</param>
    /// <param name="mediaType">The image media type.</param>
    /// <param name="data">The encoded image payload.</param>
    /// <param name="width">The requested width in points.</param>
    /// <param name="height">The requested height in points.</param>
    /// <param name="verticalAlignment">Vertical inline alignment.</param>
    /// <param name="alternativeText">Optional alternative text.</param>
    /// <returns>An immutable inline-image value.</returns>
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
