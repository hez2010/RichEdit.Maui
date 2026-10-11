namespace RichEdit.Core.Rtf;

// These masks preserve authored inheritance/reset intent beside standard RTF
// properties. They contain no text, resources, or alternate document payload.
internal static class RtfStyleState
{
    internal const int CharacterPropertyCount = 18;
    internal const int ParagraphPropertyCount = 15;
    internal static (int Inherit, int Initial, int Null) Masks(RichCharacterStyle style)
    {
        var inherit = 0; var initial = 0; var nil = 0;
        if (style.FontFamily.Kind == StyleValueKind.Inherit) inherit |= 1 << 0;
        if (style.FontFamily.Kind == StyleValueKind.Initial) initial |= 1 << 0;
        if (style.FontFamily.Kind == StyleValueKind.Value && style.FontFamily.Value is null) nil |= 1 << 0;
        if (style.FontSize.Kind == StyleValueKind.Inherit) inherit |= 1 << 1;
        if (style.FontSize.Kind == StyleValueKind.Initial) initial |= 1 << 1;
        if (style.FontSize.Kind == StyleValueKind.Value && style.FontSize.Value is null) nil |= 1 << 1;
        if (style.Bold.Kind == StyleValueKind.Inherit) inherit |= 1 << 2;
        if (style.Bold.Kind == StyleValueKind.Initial) initial |= 1 << 2;
        if (style.Italic.Kind == StyleValueKind.Inherit) inherit |= 1 << 3;
        if (style.Italic.Kind == StyleValueKind.Initial) initial |= 1 << 3;
        if (style.Underline.Kind == StyleValueKind.Inherit) inherit |= 1 << 4;
        if (style.Underline.Kind == StyleValueKind.Initial) initial |= 1 << 4;
        if (style.UnderlineColor.Kind == StyleValueKind.Inherit) inherit |= 1 << 5;
        if (style.UnderlineColor.Kind == StyleValueKind.Initial) initial |= 1 << 5;
        if (style.UnderlineColor.Kind == StyleValueKind.Value && style.UnderlineColor.Value is null) nil |= 1 << 5;
        if (style.Strike.Kind == StyleValueKind.Inherit) inherit |= 1 << 6;
        if (style.Strike.Kind == StyleValueKind.Initial) initial |= 1 << 6;
        if (style.Foreground.Kind == StyleValueKind.Inherit) inherit |= 1 << 7;
        if (style.Foreground.Kind == StyleValueKind.Initial) initial |= 1 << 7;
        if (style.Foreground.Kind == StyleValueKind.Value && style.Foreground.Value is null) nil |= 1 << 7;
        if (style.Background.Kind == StyleValueKind.Inherit) inherit |= 1 << 8;
        if (style.Background.Kind == StyleValueKind.Initial) initial |= 1 << 8;
        if (style.Background.Kind == StyleValueKind.Value && style.Background.Value is null) nil |= 1 << 8;
        if (style.Script.Kind == StyleValueKind.Inherit) inherit |= 1 << 9;
        if (style.Script.Kind == StyleValueKind.Initial) initial |= 1 << 9;
        if (style.CharacterSpacing.Kind == StyleValueKind.Inherit) inherit |= 1 << 10;
        if (style.CharacterSpacing.Kind == StyleValueKind.Initial) initial |= 1 << 10;
        if (style.SmallCaps.Kind == StyleValueKind.Inherit) inherit |= 1 << 11;
        if (style.SmallCaps.Kind == StyleValueKind.Initial) initial |= 1 << 11;
        if (style.AllCaps.Kind == StyleValueKind.Inherit) inherit |= 1 << 12;
        if (style.AllCaps.Kind == StyleValueKind.Initial) initial |= 1 << 12;
        if (style.Outline.Kind == StyleValueKind.Inherit) inherit |= 1 << 13;
        if (style.Outline.Kind == StyleValueKind.Initial) initial |= 1 << 13;
        if (style.Shadow.Kind == StyleValueKind.Inherit) inherit |= 1 << 14;
        if (style.Shadow.Kind == StyleValueKind.Initial) initial |= 1 << 14;
        if (style.Hidden.Kind == StyleValueKind.Inherit) inherit |= 1 << 15;
        if (style.Hidden.Kind == StyleValueKind.Initial) initial |= 1 << 15;
        if (style.Language.Kind == StyleValueKind.Inherit) inherit |= 1 << 16;
        if (style.Language.Kind == StyleValueKind.Initial) initial |= 1 << 16;
        if (style.Language.Kind == StyleValueKind.Value && style.Language.Value is null) nil |= 1 << 16;
        if (style.Direction.Kind == StyleValueKind.Inherit) inherit |= 1 << 17;
        if (style.Direction.Kind == StyleValueKind.Initial) initial |= 1 << 17;
        return (inherit, initial, nil);
    }

    internal static RichCharacterStyle Restore(ResolvedCharacterStyle value, int inherit, int initial, int nil) => new()
    {
        FontFamily = Restore<string?>(0, inherit, initial, (nil & (1 << 0)) != 0 ? null : value.FontFamily),
        FontSize = Restore<RichFontSize?>(1, inherit, initial, (nil & (1 << 1)) != 0 ? null : value.FontSize),
        Bold = Restore<bool>(2, inherit, initial, value.Bold),
        Italic = Restore<bool>(3, inherit, initial, value.Italic),
        Underline = Restore<RichUnderline>(4, inherit, initial, value.Underline),
        UnderlineColor = Restore<RichColor?>(5, inherit, initial, (nil & (1 << 5)) != 0 ? null : value.UnderlineColor),
        Strike = Restore<RichStrike>(6, inherit, initial, value.Strike),
        Foreground = Restore<RichColor?>(7, inherit, initial, (nil & (1 << 7)) != 0 ? null : value.Foreground),
        Background = Restore<RichColor?>(8, inherit, initial, (nil & (1 << 8)) != 0 ? null : value.Background),
        Script = Restore<RichScript>(9, inherit, initial, value.Script),
        CharacterSpacing = Restore<RichLength>(10, inherit, initial, value.CharacterSpacing),
        SmallCaps = Restore<bool>(11, inherit, initial, value.SmallCaps),
        AllCaps = Restore<bool>(12, inherit, initial, value.AllCaps),
        Outline = Restore<bool>(13, inherit, initial, value.Outline),
        Shadow = Restore<bool>(14, inherit, initial, value.Shadow),
        Hidden = Restore<bool>(15, inherit, initial, value.Hidden),
        Language = Restore<string?>(16, inherit, initial, (nil & (1 << 16)) != 0 ? null : value.Language),
        Direction = Restore<RichDirection>(17, inherit, initial, value.Direction),
    };

    internal static (int Inherit, int Initial, int Null) Masks(RichParagraphStyle style)
    {
        var inherit = 0; var initial = 0; var nil = 0;
        if (style.Alignment.Kind == StyleValueKind.Inherit) inherit |= 1 << 0;
        if (style.Alignment.Kind == StyleValueKind.Initial) initial |= 1 << 0;
        if (style.Direction.Kind == StyleValueKind.Inherit) inherit |= 1 << 1;
        if (style.Direction.Kind == StyleValueKind.Initial) initial |= 1 << 1;
        if (style.LeadingIndent.Kind == StyleValueKind.Inherit) inherit |= 1 << 2;
        if (style.LeadingIndent.Kind == StyleValueKind.Initial) initial |= 1 << 2;
        if (style.TrailingIndent.Kind == StyleValueKind.Inherit) inherit |= 1 << 3;
        if (style.TrailingIndent.Kind == StyleValueKind.Initial) initial |= 1 << 3;
        if (style.FirstLineIndent.Kind == StyleValueKind.Inherit) inherit |= 1 << 4;
        if (style.FirstLineIndent.Kind == StyleValueKind.Initial) initial |= 1 << 4;
        if (style.SpaceBefore.Kind == StyleValueKind.Inherit) inherit |= 1 << 5;
        if (style.SpaceBefore.Kind == StyleValueKind.Initial) initial |= 1 << 5;
        if (style.SpaceAfter.Kind == StyleValueKind.Inherit) inherit |= 1 << 6;
        if (style.SpaceAfter.Kind == StyleValueKind.Initial) initial |= 1 << 6;
        if (style.HeadingLevel.Kind == StyleValueKind.Inherit) inherit |= 1 << 7;
        if (style.HeadingLevel.Kind == StyleValueKind.Initial) initial |= 1 << 7;
        if (style.HeadingLevel.Kind == StyleValueKind.Value && style.HeadingLevel.Value is null) nil |= 1 << 7;
        if (style.KeepWithNext.Kind == StyleValueKind.Inherit) inherit |= 1 << 8;
        if (style.KeepWithNext.Kind == StyleValueKind.Initial) initial |= 1 << 8;
        if (style.KeepTogether.Kind == StyleValueKind.Inherit) inherit |= 1 << 9;
        if (style.KeepTogether.Kind == StyleValueKind.Initial) initial |= 1 << 9;
        if (style.WidowControl.Kind == StyleValueKind.Inherit) inherit |= 1 << 10;
        if (style.WidowControl.Kind == StyleValueKind.Initial) initial |= 1 << 10;
        if (style.Background.Kind == StyleValueKind.Inherit) inherit |= 1 << 11;
        if (style.Background.Kind == StyleValueKind.Initial) initial |= 1 << 11;
        if (style.Background.Kind == StyleValueKind.Value && style.Background.Value is null) nil |= 1 << 11;
        if (style.LineSpacing.Kind == StyleValueKind.Inherit) inherit |= 1 << 12;
        if (style.LineSpacing.Kind == StyleValueKind.Initial) initial |= 1 << 12;
        if (style.TabStops.Kind == StyleValueKind.Inherit) inherit |= 1 << 13;
        if (style.TabStops.Kind == StyleValueKind.Initial) initial |= 1 << 13;
        if (style.Hyphenation.Kind == StyleValueKind.Inherit) inherit |= 1 << 14;
        if (style.Hyphenation.Kind == StyleValueKind.Initial) initial |= 1 << 14;
        return (inherit, initial, nil);
    }

    internal static RichParagraphStyle Restore(ResolvedParagraphStyle value, int inherit, int initial, int nil) => new()
    {
        Alignment = Restore<RichAlignment>(0, inherit, initial, value.Alignment),
        Direction = Restore<RichDirection>(1, inherit, initial, value.Direction),
        LeadingIndent = Restore<RichLength>(2, inherit, initial, value.LeadingIndent),
        TrailingIndent = Restore<RichLength>(3, inherit, initial, value.TrailingIndent),
        FirstLineIndent = Restore<RichLength>(4, inherit, initial, value.FirstLineIndent),
        SpaceBefore = Restore<RichLength>(5, inherit, initial, value.SpaceBefore),
        SpaceAfter = Restore<RichLength>(6, inherit, initial, value.SpaceAfter),
        HeadingLevel = Restore<int?>(7, inherit, initial, (nil & (1 << 7)) != 0 ? null : value.HeadingLevel),
        KeepWithNext = Restore<bool>(8, inherit, initial, value.KeepWithNext),
        KeepTogether = Restore<bool>(9, inherit, initial, value.KeepTogether),
        WidowControl = Restore<bool>(10, inherit, initial, value.WidowControl),
        Background = Restore<RichColor?>(11, inherit, initial, (nil & (1 << 11)) != 0 ? null : value.Background),
        LineSpacing = Restore<RichLineSpacing>(12, inherit, initial, value.LineSpacing),
        TabStops = Restore<RichTabStops>(13, inherit, initial, value.TabStops),
        Hyphenation = Restore<bool>(14, inherit, initial, value.Hyphenation),
    };

    private static StyleValue<T> Restore<T>(int index, int inherit, int initial, T value) =>
        (inherit & (1 << index)) != 0 ? StyleValue<T>.Inherit :
        (initial & (1 << index)) != 0 ? StyleValue<T>.Initial : StyleValue<T>.FromValue(value);
}
