using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Exposes bindable authored and effective character formatting for a selection.
/// </summary>
public sealed class RichTextSelectionCharacterFormat : BindableObject
{
    private readonly RichTextSelection _selection;

    internal RichTextSelectionCharacterFormat(RichTextSelection selection)
    {
        _selection = selection;
    }

    /// <summary>Gets or sets the authored font family.</summary>
    public string? FontFamily
    {
        get => GetState(static format => format.FontFamily).Value;
        set => Update(format => format with { FontFamily = value });
    }

    /// <summary>Gets whether selected authored font families differ.</summary>
    public bool IsFontFamilyMixed => GetState(static format => format.FontFamily).IsMixed;

    /// <summary>Gets whether all selected font families are inherited.</summary>
    public bool IsFontFamilyInherited =>
        !IsFontFamilyMixed && FontFamily is null;

    /// <summary>Gets the resolved font-family fallback.</summary>
    public string? EffectiveFontFamily =>
        GetEffectiveState(format =>
            format.FontFamily ??
            _selection.Editor.Document.DefaultCharacterFormat.FontFamily ??
            _selection.Editor.FontFamily).Value;

    /// <summary>Gets whether resolved selected font families differ.</summary>
    public bool IsEffectiveFontFamilyMixed =>
        GetEffectiveState(format =>
            format.FontFamily ??
            _selection.Editor.Document.DefaultCharacterFormat.FontFamily ??
            _selection.Editor.FontFamily).IsMixed;

    /// <summary>Gets or sets the authored font size in points.</summary>
    public double? FontSize
    {
        get => GetState(static format => format.FontSize).Value;
        set => Update(format => format with { FontSize = value });
    }

    /// <summary>Gets whether selected authored font sizes differ.</summary>
    public bool IsFontSizeMixed => GetState(static format => format.FontSize).IsMixed;

    /// <summary>Gets whether all selected font sizes are inherited.</summary>
    public bool IsFontSizeInherited => !IsFontSizeMixed && FontSize is null;

    /// <summary>Gets the resolved font size in points.</summary>
    public double? EffectiveFontSize =>
        GetEffectiveState(format =>
            format.FontSize ??
            _selection.Editor.Document.DefaultCharacterFormat.FontSize ??
            _selection.Editor.FontSize).Value;

    /// <summary>Gets whether resolved selected font sizes differ.</summary>
    public bool IsEffectiveFontSizeMixed =>
        GetEffectiveState(format =>
            format.FontSize ??
            _selection.Editor.Document.DefaultCharacterFormat.FontSize ??
            _selection.Editor.FontSize).IsMixed;

    /// <summary>Gets or sets the numeric font weight.</summary>
    public int FontWeight
    {
        get => GetState(static format => format.FontWeight).Value;
        set => Update(format => format with { FontWeight = value });
    }

    /// <summary>Gets whether selected font weights differ.</summary>
    public bool IsFontWeightMixed => GetState(static format => format.FontWeight).IsMixed;

    /// <summary>Gets or sets whether text is bold.</summary>
    public bool Bold
    {
        get => GetState(static format => format.Bold).Value;
        set => Update(format => format with
        {
            FontWeight = value ? Math.Max(format.FontWeight, 700) : 400,
        });
    }

    /// <summary>Gets whether selected bold states differ.</summary>
    public bool IsBoldMixed => GetState(static format => format.Bold).IsMixed;

    /// <summary>Gets or sets whether text is italic.</summary>
    public bool Italic
    {
        get => GetState(static format => format.Italic).Value;
        set => Update(format => format with { Italic = value });
    }

    /// <summary>Gets whether selected italic states differ.</summary>
    public bool IsItalicMixed => GetState(static format => format.Italic).IsMixed;

    /// <summary>Gets or sets the underline style.</summary>
    public RichTextUnderlineStyle Underline
    {
        get => GetState(static format => format.Underline).Value;
        set => Update(format => format with { Underline = value });
    }

    /// <summary>Gets whether selected underline styles differ.</summary>
    public bool IsUnderlineMixed => GetState(static format => format.Underline).IsMixed;

    /// <summary>Gets or sets the underline color.</summary>
    public Color? UnderlineColor
    {
        get => GetState(static format => format.UnderlineColor).Value;
        set => Update(format => format with { UnderlineColor = value });
    }

    /// <summary>Gets whether selected underline colors differ.</summary>
    public bool IsUnderlineColorMixed => GetState(static format => format.UnderlineColor).IsMixed;

    /// <summary>Gets or sets the strikethrough style.</summary>
    public RichTextStrikethroughStyle Strikethrough
    {
        get => GetState(static format => format.Strikethrough).Value;
        set => Update(format => format with { Strikethrough = value });
    }

    /// <summary>Gets whether selected strikethrough styles differ.</summary>
    public bool IsStrikethroughMixed => GetState(static format => format.Strikethrough).IsMixed;

    /// <summary>Gets or sets the strikethrough color.</summary>
    public Color? StrikethroughColor
    {
        get => GetState(static format => format.StrikethroughColor).Value;
        set => Update(format => format with { StrikethroughColor = value });
    }

    /// <summary>Gets whether selected strikethrough colors differ.</summary>
    public bool IsStrikethroughColorMixed =>
        GetState(static format => format.StrikethroughColor).IsMixed;

    /// <summary>Gets or sets the authored foreground color.</summary>
    public Color? ForegroundColor
    {
        get => GetState(static format => format.ForegroundColor).Value;
        set => Update(format => format with { ForegroundColor = value });
    }

    /// <summary>Gets whether selected authored foreground colors differ.</summary>
    public bool IsForegroundColorMixed => GetState(static format => format.ForegroundColor).IsMixed;

    /// <summary>Gets whether all selected foreground colors are inherited.</summary>
    public bool IsForegroundColorInherited =>
        !IsForegroundColorMixed && ForegroundColor is null;

    /// <summary>Gets the resolved foreground-color fallback.</summary>
    public Color? EffectiveForegroundColor =>
        GetEffectiveState(format =>
            format.ForegroundColor ??
            _selection.Editor.Document.DefaultCharacterFormat.ForegroundColor ??
            _selection.Editor.TextColor).Value;

    /// <summary>Gets whether resolved selected foreground colors differ.</summary>
    public bool IsEffectiveForegroundColorMixed =>
        GetEffectiveState(format =>
            format.ForegroundColor ??
            _selection.Editor.Document.DefaultCharacterFormat.ForegroundColor ??
            _selection.Editor.TextColor).IsMixed;

    /// <summary>Gets or sets the solid text background color.</summary>
    public Color? BackgroundColor
    {
        get => GetState(static format => format.BackgroundColor).Value;
        set => Update(format => format with { BackgroundColor = value });
    }

    /// <summary>Gets whether selected text background colors differ.</summary>
    public bool IsBackgroundColorMixed => GetState(static format => format.BackgroundColor).IsMixed;

    /// <summary>Gets or sets superscript or subscript state.</summary>
    public RichTextScript Script
    {
        get => GetState(static format => format.Script).Value;
        set => Update(format => format with { Script = value });
    }

    /// <summary>Gets whether selected script states differ.</summary>
    public bool IsScriptMixed => GetState(static format => format.Script).IsMixed;

    /// <summary>Gets or sets baseline offset in points.</summary>
    public double BaselineOffset
    {
        get => GetState(static format => format.BaselineOffset).Value;
        set => Update(format => format with { BaselineOffset = value });
    }

    /// <summary>Gets whether selected baseline offsets differ.</summary>
    public bool IsBaselineOffsetMixed => GetState(static format => format.BaselineOffset).IsMixed;

    /// <summary>Gets or sets character spacing in points.</summary>
    public double CharacterSpacing
    {
        get => GetState(static format => format.CharacterSpacing).Value;
        set => Update(format => format with { CharacterSpacing = value });
    }

    /// <summary>Gets whether selected character-spacing values differ.</summary>
    public bool IsCharacterSpacingMixed => GetState(static format => format.CharacterSpacing).IsMixed;

    /// <summary>Gets or sets horizontal glyph scale.</summary>
    public double HorizontalScale
    {
        get => GetState(static format => format.HorizontalScale).Value;
        set => Update(format => format with { HorizontalScale = value });
    }

    /// <summary>Gets whether selected horizontal scales differ.</summary>
    public bool IsHorizontalScaleMixed => GetState(static format => format.HorizontalScale).IsMixed;

    /// <summary>Gets or sets whether small-cap glyphs are requested.</summary>
    public bool SmallCaps
    {
        get => GetState(static format => format.SmallCaps).Value;
        set => Update(format => format with { SmallCaps = value });
    }

    /// <summary>Gets whether selected small-cap states differ.</summary>
    public bool IsSmallCapsMixed => GetState(static format => format.SmallCaps).IsMixed;

    /// <summary>Gets or sets whether all-cap glyphs are requested.</summary>
    public bool AllCaps
    {
        get => GetState(static format => format.AllCaps).Value;
        set => Update(format => format with { AllCaps = value });
    }

    /// <summary>Gets whether selected all-cap states differ.</summary>
    public bool IsAllCapsMixed => GetState(static format => format.AllCaps).IsMixed;

    /// <summary>Gets or sets the outline text effect.</summary>
    public bool Outline
    {
        get => GetState(static format => format.Outline).Value;
        set => Update(format => format with { Outline = value });
    }

    /// <summary>Gets whether selected outline states differ.</summary>
    public bool IsOutlineMixed => GetState(static format => format.Outline).IsMixed;

    /// <summary>Gets or sets the shadow text effect.</summary>
    public bool Shadow
    {
        get => GetState(static format => format.Shadow).Value;
        set => Update(format => format with { Shadow = value });
    }

    /// <summary>Gets whether selected shadow states differ.</summary>
    public bool IsShadowMixed => GetState(static format => format.Shadow).IsMixed;

    /// <summary>Gets or sets whether text is hidden.</summary>
    public bool Hidden
    {
        get => GetState(static format => format.Hidden).Value;
        set => Update(format => format with { Hidden = value });
    }

    /// <summary>Gets whether selected hidden states differ.</summary>
    public bool IsHiddenMixed => GetState(static format => format.Hidden).IsMixed;

    /// <summary>Gets or sets the BCP 47 language tag.</summary>
    public string? LanguageTag
    {
        get => GetState(static format => format.LanguageTag).Value;
        set => Update(format => format with { LanguageTag = value });
    }

    /// <summary>Gets whether selected language tags differ.</summary>
    public bool IsLanguageTagMixed => GetState(static format => format.LanguageTag).IsMixed;

    /// <summary>Gets or sets explicit character direction.</summary>
    public RichTextDirection Direction
    {
        get => GetState(static format => format.Direction).Value;
        set => Update(format => format with { Direction = value });
    }

    /// <summary>Gets whether selected character directions differ.</summary>
    public bool IsDirectionMixed => GetState(static format => format.Direction).IsMixed;

    /// <summary>Gets or sets the kerning preference.</summary>
    public RichTextFeatureMode Kerning
    {
        get => GetState(static format => format.Kerning).Value;
        set => Update(format => format with { Kerning = value });
    }

    /// <summary>Gets whether selected kerning preferences differ.</summary>
    public bool IsKerningMixed => GetState(static format => format.Kerning).IsMixed;

    /// <summary>Gets or sets the ligature preference.</summary>
    public RichTextFeatureMode Ligatures
    {
        get => GetState(static format => format.Ligatures).Value;
        set => Update(format => format with { Ligatures = value });
    }

    /// <summary>Gets whether selected ligature preferences differ.</summary>
    public bool IsLigaturesMixed => GetState(static format => format.Ligatures).IsMixed;

    /// <summary>Gets or sets the RTF character-shading amount.</summary>
    public int Shading
    {
        get => GetState(static format => format.Shading).Value;
        set => Update(format => format with { Shading = value });
    }

    /// <summary>Gets whether selected character-shading amounts differ.</summary>
    public bool IsShadingMixed => GetState(static format => format.Shading).IsMixed;

    /// <summary>Gets or sets the character-shading foreground color.</summary>
    public Color? ShadingForegroundColor
    {
        get => GetState(static format => format.ShadingForegroundColor).Value;
        set => Update(format => format with { ShadingForegroundColor = value });
    }

    /// <summary>Gets whether selected shading foreground colors differ.</summary>
    public bool IsShadingForegroundColorMixed =>
        GetState(static format => format.ShadingForegroundColor).IsMixed;

    /// <summary>Gets or sets the character-shading background color.</summary>
    public Color? ShadingBackgroundColor
    {
        get => GetState(static format => format.ShadingBackgroundColor).Value;
        set => Update(format => format with { ShadingBackgroundColor = value });
    }

    /// <summary>Gets whether selected shading background colors differ.</summary>
    public bool IsShadingBackgroundColorMixed =>
        GetState(static format => format.ShadingBackgroundColor).IsMixed;

    /// <summary>Gets or sets the named character style.</summary>
    public string? StyleName
    {
        get => GetState(static format => format.StyleName).Value;
        set => Update(format => format with { StyleName = value });
    }

    /// <summary>Gets whether selected character style names differ.</summary>
    public bool IsStyleNameMixed => GetState(static format => format.StyleName).IsMixed;

    internal void Refresh() => OnPropertyChanged(string.Empty);

    private void Update(Func<RichTextCharacterFormat, RichTextCharacterFormat> update) =>
        _selection.UpdateCharacterFormat(update);

    private ValueState<T> GetState<T>(Func<RichTextCharacterFormat, T> selector) =>
        CreateState(_selection.GetCharacterFormats(), selector);

    private ValueState<T> GetEffectiveState<T>(Func<RichTextCharacterFormat, T> selector) =>
        CreateState(_selection.GetCharacterFormats(), selector);

    private static ValueState<T> CreateState<T>(
        IReadOnlyList<RichTextCharacterFormat> formats,
        Func<RichTextCharacterFormat, T> selector)
    {
        var value = selector(formats[0]);
        var comparer = EqualityComparer<T>.Default;
        return new ValueState<T>(
            value,
            formats.Skip(1).Any(format => !comparer.Equals(value, selector(format))));
    }

    private readonly record struct ValueState<T>(T Value, bool IsMixed);
}

/// <summary>
/// Exposes bindable paragraph formatting for a selection.
/// </summary>
public sealed class RichTextSelectionParagraphFormat : BindableObject
{
    private readonly RichTextSelection _selection;

    internal RichTextSelectionParagraphFormat(RichTextSelection selection)
    {
        _selection = selection;
    }

    /// <summary>Gets or sets paragraph alignment.</summary>
    public RichTextAlignment Alignment
    {
        get => GetState(static format => format.Alignment).Value;
        set => Update(format => format with { Alignment = value });
    }

    /// <summary>Gets whether selected paragraph alignments differ.</summary>
    public bool IsAlignmentMixed => GetState(static format => format.Alignment).IsMixed;

    /// <summary>Gets or sets paragraph direction.</summary>
    public RichTextDirection Direction
    {
        get => GetState(static format => format.Direction).Value;
        set => Update(format => format with { Direction = value });
    }

    /// <summary>Gets whether selected paragraph directions differ.</summary>
    public bool IsDirectionMixed => GetState(static format => format.Direction).IsMixed;

    /// <summary>Gets or sets leading indent in points.</summary>
    public double LeadingIndent
    {
        get => GetState(static format => format.LeadingIndent).Value;
        set => Update(format => format with { LeadingIndent = value });
    }

    /// <summary>Gets whether selected leading indents differ.</summary>
    public bool IsLeadingIndentMixed => GetState(static format => format.LeadingIndent).IsMixed;

    /// <summary>Gets or sets trailing indent in points.</summary>
    public double TrailingIndent
    {
        get => GetState(static format => format.TrailingIndent).Value;
        set => Update(format => format with { TrailingIndent = value });
    }

    /// <summary>Gets whether selected trailing indents differ.</summary>
    public bool IsTrailingIndentMixed => GetState(static format => format.TrailingIndent).IsMixed;

    /// <summary>Gets or sets first-line indent in points.</summary>
    public double FirstLineIndent
    {
        get => GetState(static format => format.FirstLineIndent).Value;
        set => Update(format => format with { FirstLineIndent = value });
    }

    /// <summary>Gets whether selected first-line indents differ.</summary>
    public bool IsFirstLineIndentMixed => GetState(static format => format.FirstLineIndent).IsMixed;

    /// <summary>Gets or sets space before paragraphs in points.</summary>
    public double SpaceBefore
    {
        get => GetState(static format => format.SpaceBefore).Value;
        set => Update(format => format with { SpaceBefore = value });
    }

    /// <summary>Gets whether selected before-spacing values differ.</summary>
    public bool IsSpaceBeforeMixed => GetState(static format => format.SpaceBefore).IsMixed;

    /// <summary>Gets or sets space after paragraphs in points.</summary>
    public double SpaceAfter
    {
        get => GetState(static format => format.SpaceAfter).Value;
        set => Update(format => format with { SpaceAfter = value });
    }

    /// <summary>Gets whether selected after-spacing values differ.</summary>
    public bool IsSpaceAfterMixed => GetState(static format => format.SpaceAfter).IsMixed;

    /// <summary>Gets or sets the line-spacing rule.</summary>
    public RichTextLineSpacingRule LineSpacingRule
    {
        get => GetState(static format => format.LineSpacingRule).Value;
        set => Update(format => format with { LineSpacingRule = value });
    }

    /// <summary>Gets whether selected line-spacing rules differ.</summary>
    public bool IsLineSpacingRuleMixed => GetState(static format => format.LineSpacingRule).IsMixed;

    /// <summary>Gets or sets the line-spacing value.</summary>
    public double LineSpacing
    {
        get => GetState(static format => format.LineSpacing).Value;
        set => Update(format => format with { LineSpacing = value });
    }

    /// <summary>Gets whether selected line-spacing values differ.</summary>
    public bool IsLineSpacingMixed => GetState(static format => format.LineSpacing).IsMixed;

    /// <summary>Gets or sets minimum line height in points.</summary>
    public double? MinimumLineHeight
    {
        get => GetState(static format => format.MinimumLineHeight).Value;
        set => Update(format => format with { MinimumLineHeight = value });
    }

    /// <summary>Gets whether selected minimum line heights differ.</summary>
    public bool IsMinimumLineHeightMixed =>
        GetState(static format => format.MinimumLineHeight).IsMixed;

    /// <summary>Gets or sets maximum line height in points.</summary>
    public double? MaximumLineHeight
    {
        get => GetState(static format => format.MaximumLineHeight).Value;
        set => Update(format => format with { MaximumLineHeight = value });
    }

    /// <summary>Gets whether selected maximum line heights differ.</summary>
    public bool IsMaximumLineHeightMixed =>
        GetState(static format => format.MaximumLineHeight).IsMixed;

    /// <summary>Gets or sets ordered paragraph tab stops.</summary>
    public ImmutableArray<RichTextTabStop> TabStops
    {
        get => GetState(static format => format.TabStops).Value;
        set => Update(format => format with { TabStops = value });
    }

    /// <summary>Gets whether selected tab-stop collections differ.</summary>
    public bool IsTabStopsMixed => GetState(static format => format.TabStops).IsMixed;

    /// <summary>Gets or sets the hyphenation preference.</summary>
    public bool Hyphenation
    {
        get => GetState(static format => format.Hyphenation).Value;
        set => Update(format => format with { Hyphenation = value });
    }

    /// <summary>Gets whether selected hyphenation preferences differ.</summary>
    public bool IsHyphenationMixed => GetState(static format => format.Hyphenation).IsMixed;

    /// <summary>Gets or sets solid paragraph background color.</summary>
    public Color? BackgroundColor
    {
        get => GetState(static format => format.BackgroundColor).Value;
        set => Update(format => format with { BackgroundColor = value });
    }

    /// <summary>Gets whether selected paragraph background colors differ.</summary>
    public bool IsBackgroundColorMixed => GetState(static format => format.BackgroundColor).IsMixed;

    /// <summary>Gets or sets the RTF paragraph-shading amount.</summary>
    public int Shading
    {
        get => GetState(static format => format.Shading).Value;
        set => Update(format => format with { Shading = value });
    }

    /// <summary>Gets whether selected paragraph-shading amounts differ.</summary>
    public bool IsShadingMixed => GetState(static format => format.Shading).IsMixed;

    /// <summary>Gets or sets paragraph-shading foreground color.</summary>
    public Color? ShadingForegroundColor
    {
        get => GetState(static format => format.ShadingForegroundColor).Value;
        set => Update(format => format with { ShadingForegroundColor = value });
    }

    /// <summary>Gets whether selected shading foreground colors differ.</summary>
    public bool IsShadingForegroundColorMixed =>
        GetState(static format => format.ShadingForegroundColor).IsMixed;

    /// <summary>Gets or sets paragraph-shading background color.</summary>
    public Color? ShadingBackgroundColor
    {
        get => GetState(static format => format.ShadingBackgroundColor).Value;
        set => Update(format => format with { ShadingBackgroundColor = value });
    }

    /// <summary>Gets whether selected shading background colors differ.</summary>
    public bool IsShadingBackgroundColorMixed =>
        GetState(static format => format.ShadingBackgroundColor).IsMixed;

    /// <summary>Gets or sets paragraph border formatting.</summary>
    public RichTextBorder? Border
    {
        get => GetState(static format => format.Border).Value;
        set => Update(format => format with { Border = value });
    }

    /// <summary>Gets whether selected paragraph borders differ.</summary>
    public bool IsBorderMixed => GetState(static format => format.Border).IsMixed;

    /// <summary>Gets or sets the named paragraph style.</summary>
    public string? StyleName
    {
        get => GetState(static format => format.StyleName).Value;
        set => Update(format => format with { StyleName = value });
    }

    /// <summary>Gets whether selected paragraph style names differ.</summary>
    public bool IsStyleNameMixed => GetState(static format => format.StyleName).IsMixed;

    /// <summary>Gets the selected list-item state, or null when not in a list.</summary>
    public RichTextListItemFormat? List
    {
        get => GetState(static format => format.List).Value;
        set
        {
            if (value is null)
            {
                _selection.ClearList();
            }
            else
            {
                _selection.SetList(value);
            }
        }
    }

    /// <summary>Gets whether selected list-item states differ.</summary>
    public bool IsListMixed => GetState(static format => format.List).IsMixed;

    internal void Refresh() => OnPropertyChanged(string.Empty);

    private void Update(Func<RichTextParagraphFormat, RichTextParagraphFormat> update) =>
        _selection.UpdateParagraphFormat(update);

    private ValueState<T> GetState<T>(Func<RichTextParagraphFormat, T> selector)
    {
        var formats = _selection.GetParagraphFormats();
        var value = selector(formats[0]);
        var comparer = EqualityComparer<T>.Default;
        return new ValueState<T>(
            value,
            formats.Skip(1).Any(format => !comparer.Equals(value, selector(format))));
    }

    private readonly record struct ValueState<T>(T Value, bool IsMixed);
}
