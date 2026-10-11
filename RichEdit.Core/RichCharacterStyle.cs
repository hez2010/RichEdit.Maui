namespace RichEdit.Core;

/// <summary>Typed cascading character properties. Unspecified properties inherit preceding formatting.</summary>
public sealed record RichCharacterStyle
{
    /// <summary>The authored font family, or null for an unresolved view fallback.</summary>
    public StyleValue<string?> FontFamily { get; init; }

    /// <summary>The authored font size, or null for an unresolved view fallback.</summary>
    public StyleValue<RichFontSize?> FontSize { get; init; }

    /// <summary>Whether bold type is authored.</summary>
    public StyleValue<bool> Bold { get; init; }

    /// <summary>Whether italic type is authored.</summary>
    public StyleValue<bool> Italic { get; init; }

    /// <summary>The authored underline style.</summary>
    public StyleValue<RichUnderline> Underline { get; init; }

    /// <summary>The underline color, or null to use the text color.</summary>
    public StyleValue<RichColor?> UnderlineColor { get; init; }

    /// <summary>The authored strike-through style.</summary>
    public StyleValue<RichStrike> Strike { get; init; }

    /// <summary>The foreground color, or null for an unresolved view fallback.</summary>
    public StyleValue<RichColor?> Foreground { get; init; }

    /// <summary>The highlight color, or null for no highlight.</summary>
    public StyleValue<RichColor?> Background { get; init; }

    /// <summary>Superscript or subscript presentation.</summary>
    public StyleValue<RichScript> Script
    {
        get => VerticalPosition.Kind switch { StyleValueKind.Inherit => default, StyleValueKind.Initial => StyleValue<RichScript>.Initial,
            _ => VerticalPosition.Value.Script };
        init => VerticalPosition = value.Kind switch { StyleValueKind.Inherit => default, StyleValueKind.Initial => StyleValue<RichVerticalPosition>.Initial,
            _ => RichVerticalPosition.FromScript(value.Value) };
    }
    /// <summary>Normal, script or manual baseline placement. Assigning Script sets this same cascading property.</summary>
    public StyleValue<RichVerticalPosition> VerticalPosition { get; init; }

    /// <summary>Additional character spacing in typographic points.</summary>
    public StyleValue<RichLength> CharacterSpacing { get; init; }

    /// <summary>Whether small capitals are requested.</summary>
    public StyleValue<bool> SmallCaps { get; init; }

    /// <summary>Whether uppercase presentation is requested.</summary>
    public StyleValue<bool> AllCaps { get; init; }

    /// <summary>Whether outlined glyphs are requested.</summary>
    public StyleValue<bool> Outline { get; init; }

    /// <summary>Whether shadowed glyphs are requested.</summary>
    public StyleValue<bool> Shadow { get; init; }

    /// <summary>Whether text is hidden from ordinary presentation.</summary>
    public StyleValue<bool> Hidden { get; init; }

    /// <summary>The language tag, or null for an unspecified language.</summary>
    public StyleValue<string?> Language { get; init; }

    /// <summary>The authored inline writing direction.</summary>
    public StyleValue<RichDirection> Direction { get; init; }
    /// <summary>Character shading, independently of highlighting; null disables shading.</summary>
    public StyleValue<RichShading?> Shading { get; init; }
    /// <summary>Horizontal glyph scaling as a positive integer percentage; 100 is natural width.</summary>
    public StyleValue<int> HorizontalScalePercent { get; init; }

    internal RichCharacterStyle Overlay(RichCharacterStyle child) => new()
    {
        FontFamily = child.FontFamily.Kind == StyleValueKind.Inherit ? FontFamily : child.FontFamily,
        FontSize = child.FontSize.Kind == StyleValueKind.Inherit ? FontSize : child.FontSize,
        Bold = child.Bold.Kind == StyleValueKind.Inherit ? Bold : child.Bold,
        Italic = child.Italic.Kind == StyleValueKind.Inherit ? Italic : child.Italic,
        Underline = child.Underline.Kind == StyleValueKind.Inherit ? Underline : child.Underline,
        UnderlineColor = child.UnderlineColor.Kind == StyleValueKind.Inherit ? UnderlineColor : child.UnderlineColor,
        Strike = child.Strike.Kind == StyleValueKind.Inherit ? Strike : child.Strike,
        Foreground = child.Foreground.Kind == StyleValueKind.Inherit ? Foreground : child.Foreground,
        Background = child.Background.Kind == StyleValueKind.Inherit ? Background : child.Background,
        VerticalPosition = child.VerticalPosition.Kind == StyleValueKind.Inherit ? VerticalPosition : child.VerticalPosition,
        CharacterSpacing = child.CharacterSpacing.Kind == StyleValueKind.Inherit ? CharacterSpacing : child.CharacterSpacing,
        SmallCaps = child.SmallCaps.Kind == StyleValueKind.Inherit ? SmallCaps : child.SmallCaps,
        AllCaps = child.AllCaps.Kind == StyleValueKind.Inherit ? AllCaps : child.AllCaps,
        Outline = child.Outline.Kind == StyleValueKind.Inherit ? Outline : child.Outline,
        Shadow = child.Shadow.Kind == StyleValueKind.Inherit ? Shadow : child.Shadow,
        Hidden = child.Hidden.Kind == StyleValueKind.Inherit ? Hidden : child.Hidden,
        Language = child.Language.Kind == StyleValueKind.Inherit ? Language : child.Language,
        Direction = child.Direction.Kind == StyleValueKind.Inherit ? Direction : child.Direction,
        Shading = child.Shading.Kind == StyleValueKind.Inherit ? Shading : child.Shading,
        HorizontalScalePercent = child.HorizontalScalePercent.Kind == StyleValueKind.Inherit ? HorizontalScalePercent : child.HorizontalScalePercent,
    };

    internal ResolvedCharacterStyle Apply(ResolvedCharacterStyle inherited) => new()
    {
        FontFamily = FontFamily.Resolve(inherited.FontFamily, null),
        FontSize = FontSize.Resolve(inherited.FontSize, null),
        Bold = Bold.Resolve(inherited.Bold, false),
        Italic = Italic.Resolve(inherited.Italic, false),
        Underline = Underline.Resolve(inherited.Underline, RichUnderline.None),
        UnderlineColor = UnderlineColor.Resolve(inherited.UnderlineColor, null),
        Strike = Strike.Resolve(inherited.Strike, RichStrike.None),
        Foreground = Foreground.Resolve(inherited.Foreground, null),
        Background = Background.Resolve(inherited.Background, null),
        VerticalPosition = VerticalPosition.Resolve(inherited.VerticalPosition, default),
        CharacterSpacing = CharacterSpacing.Resolve(inherited.CharacterSpacing, default),
        SmallCaps = SmallCaps.Resolve(inherited.SmallCaps, false),
        AllCaps = AllCaps.Resolve(inherited.AllCaps, false),
        Outline = Outline.Resolve(inherited.Outline, false),
        Shadow = Shadow.Resolve(inherited.Shadow, false),
        Hidden = Hidden.Resolve(inherited.Hidden, false),
        Language = Language.Resolve(inherited.Language, null),
        Direction = Direction.Resolve(inherited.Direction, RichDirection.Automatic),
        Shading = Shading.Resolve(inherited.Shading, null),
        HorizontalScalePercent = HorizontalScalePercent.Resolve(inherited.HorizontalScalePercent, 100),
    };
}

/// <summary>Resolved character properties after the authored style cascade.</summary>
public sealed record ResolvedCharacterStyle
{
    /// <summary>The authored font family, or null for an unresolved view fallback.</summary>
    public string? FontFamily { get; init; } = null;

    /// <summary>The authored font size, or null for an unresolved view fallback.</summary>
    public RichFontSize? FontSize { get; init; } = null;

    /// <summary>Whether bold type is authored.</summary>
    public bool Bold { get; init; } = false;

    /// <summary>Whether italic type is authored.</summary>
    public bool Italic { get; init; } = false;

    /// <summary>The authored underline style.</summary>
    public RichUnderline Underline { get; init; } = RichUnderline.None;

    /// <summary>The underline color, or null to use the text color.</summary>
    public RichColor? UnderlineColor { get; init; } = null;

    /// <summary>The authored strike-through style.</summary>
    public RichStrike Strike { get; init; } = RichStrike.None;

    /// <summary>The foreground color, or null for an unresolved view fallback.</summary>
    public RichColor? Foreground { get; init; } = null;

    /// <summary>The highlight color, or null for no highlight.</summary>
    public RichColor? Background { get; init; } = null;

    /// <summary>Superscript or subscript presentation.</summary>
    public RichScript Script { get => VerticalPosition.Script; init => VerticalPosition = RichVerticalPosition.FromScript(value); }
    /// <summary>Normal, script or manual baseline placement.</summary>
    public RichVerticalPosition VerticalPosition { get; init; }

    /// <summary>Additional character spacing in typographic points.</summary>
    public RichLength CharacterSpacing { get; init; } = default;

    /// <summary>Whether small capitals are requested.</summary>
    public bool SmallCaps { get; init; } = false;

    /// <summary>Whether uppercase presentation is requested.</summary>
    public bool AllCaps { get; init; } = false;

    /// <summary>Whether outlined glyphs are requested.</summary>
    public bool Outline { get; init; } = false;

    /// <summary>Whether shadowed glyphs are requested.</summary>
    public bool Shadow { get; init; } = false;

    /// <summary>Whether text is hidden from ordinary presentation.</summary>
    public bool Hidden { get; init; } = false;

    /// <summary>The language tag, or null for an unspecified language.</summary>
    public string? Language { get; init; } = null;

    /// <summary>The authored inline writing direction.</summary>
    public RichDirection Direction { get; init; } = RichDirection.Automatic;
    /// <summary>Character shading, independently of highlighting; null disables shading.</summary>
    public RichShading? Shading { get; init; }
    /// <summary>Horizontal glyph scaling as a percentage; 100 is natural width.</summary>
    public int HorizontalScalePercent { get; init; } = 100;

}
