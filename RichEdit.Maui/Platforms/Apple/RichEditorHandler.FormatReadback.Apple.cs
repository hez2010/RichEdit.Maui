#if IOS || MACCATALYST
using System.Collections.Immutable;
using Foundation;
using UIKit;


namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichTextCharacterFormat ReadCharacterFormat(
        NSDictionary dictionary,
        RichTextCharacterFormat defaultFormat)
    {
        var metadata = dictionary[CharacterMetadataKey] as CharacterMetadata;
        var format = metadata?.Format ?? defaultFormat;
        var attributes = new UIStringAttributes(dictionary);
        if (attributes.Font is { } font)
        {
            var traits = font.FontDescriptor.SymbolicTraits;
            // UIKit can rebuild attributes without our metadata during a
            // selection update. Equivalent defaults must remain inherited.
            var expectedFont = ResolveFont(
                DisplaySourceSnapshot.ResolveCharacterFormat(format));
            var bold = traits.HasFlag(UIFontDescriptorSymbolicTraits.Bold);
            format = format with
            {
                FontFamily = font.FamilyName != expectedFont.FamilyName
                    ? font.FamilyName
                    : format.FontFamily,
                FontSize = font.PointSize != expectedFont.PointSize
                    ? font.PointSize
                    : format.FontSize,
                FontWeight = bold == format.Bold ? format.FontWeight : bold ? 700 : 400,
                Italic = traits.HasFlag(UIFontDescriptorSymbolicTraits.Italic),
            };
        }

        if (attributes.ForegroundColor is { } foreground && !format.Hidden)
        {
            var expected = format.ForegroundColor ?? VirtualView.Document.DefaultCharacterFormat.ForegroundColor ??
                VirtualView.TextColor ?? FromUIColor(_defaultTextColor);
            var color = FromUIColor(foreground);
            if (!color.Equals(expected))
            {
                format = format with { ForegroundColor = color };
            }
        }

        format = format with
        {
            BackgroundColor = attributes.BackgroundColor is { } background ? FromUIColor(background) : null,
        };

        var underline = attributes.UnderlineStyle ?? NSUnderlineStyle.None;
        if (underline != ToNativeUnderline(format.Underline))
        {
            format = format with
            {
                Underline = FromNativeUnderline(underline),
            };
        }

        var strikethrough = attributes.StrikethroughStyle ?? NSUnderlineStyle.None;
        var expectedStrikethrough = format.Strikethrough switch
        {
            RichTextStrikethroughStyle.Double => NSUnderlineStyle.Double,
            RichTextStrikethroughStyle.Single => NSUnderlineStyle.Single,
            _ => NSUnderlineStyle.None,
        };
        if (strikethrough != expectedStrikethrough)
        {
            format = format with
            {
                Strikethrough = strikethrough switch
                {
                    NSUnderlineStyle.Double => RichTextStrikethroughStyle.Double,
                    NSUnderlineStyle.None => RichTextStrikethroughStyle.None,
                    _ => RichTextStrikethroughStyle.Single,
                },
            };
        }

        format = format with
        {
            UnderlineColor = attributes.UnderlineColor is { } underlineColor ? FromUIColor(underlineColor) : null,
            StrikethroughColor = attributes.StrikethroughColor is { } strikeColor ? FromUIColor(strikeColor) : null,
        };

        if (metadata is null)
        {
            var baseline = attributes.BaselineOffset ?? 0;
            var preservesBaseline = Math.Abs(baseline - GetNativeBaselineOffset(format,
                (double)(attributes.Font?.PointSize ?? _defaultFont.PointSize))) < 0.01d;
            var kerning = attributes.KerningAdjustment ?? 0;
            var expansion = attributes.Expansion ?? 0;
            format = format with
            {
                // A native baseline alone cannot distinguish script from an explicit offset.
                // Retain a matching authored projection, otherwise record the shift once.
                BaselineOffset = preservesBaseline ? format.BaselineOffset : baseline,
                Script = preservesBaseline ? format.Script : RichTextScript.Normal,
                CharacterSpacing = kerning,
                HorizontalScale = Math.Max(1d + expansion, 0.01d),
                Outline = attributes.StrokeWidth is not null and not 0,
                Shadow = attributes.Shadow is not null,
            };
        }

        format = format with
        {
            Direction = attributes.WritingDirectionInt?.FirstOrDefault()?.Int32Value switch
            {
                1 or 3 => RichTextDirection.RightToLeft,
                0 or 2 => RichTextDirection.LeftToRight,
                _ => RichTextDirection.Automatic,
            },
        };

        return format;
    }

    private static RichTextParagraphFormat ReadParagraphFormat(
        NSDictionary dictionary,
        RichTextParagraphFormat defaultFormat)
    {
        var metadata = dictionary[ParagraphMetadataKey] as ParagraphMetadata;
        var format = metadata?.Format ?? defaultFormat;
        var style = new UIStringAttributes(dictionary).ParagraphStyle;
        if (style is null)
        {
            return OperatingSystem.IsIOSVersionAtLeast(16) ||
                OperatingSystem.IsMacCatalystVersionAtLeast(16)
                ? format with { List = null, NativeList = null }
                : format;
        }

        var projectedList = GetNativeListMetadata(style);
        if (projectedList is not null)
        {
            // UIKit can retain another paragraph's custom attributes after editing.
            // The NSTextList owner identifies the actual list; restarts and authored
            // paragraph indents are reusable only within that same list and level.
            var priorList = format.NativeList;
            var sameList = priorList is not null && priorList.Id == projectedList.Format.Id && priorList.Level == projectedList.Format.Level;
            var projectedFormat = sameList && priorList is { Restart: true }
                ? projectedList.Format with { Restart = true, StartAt = priorList.StartAt }
                : projectedList.Format;
            format = format with
            {
                List = RichTextListConversions.ToItem(projectedFormat),
                NativeList = projectedFormat,
                FirstLineIndent = sameList ? format.FirstLineIndent : projectedList.FirstLineIndent,
            };
        }

        RichTextListFormat? list = format.NativeList;
        if (OperatingSystem.IsIOSVersionAtLeast(16) ||
            OperatingSystem.IsMacCatalystVersionAtLeast(16))
        {
            var nativeList = ReadNativeTextList(style);
            // A paragraph can retain inherited metadata after its native list is removed.
            // Native list membership is authoritative; metadata preserves richer details.
            list = nativeList is null
                ? null
                : list ?? nativeList;
        }

        var lineSpacingRule = RichTextLineSpacingRule.Automatic;
        var lineSpacing = (double)style.LineSpacing;
        if (style.MinimumLineHeight > 0 && style.MaximumLineHeight == style.MinimumLineHeight)
        {
            lineSpacingRule = RichTextLineSpacingRule.Exactly;
            lineSpacing = style.MinimumLineHeight;
        }
        else if (style.MinimumLineHeight > 0)
        {
            lineSpacingRule = RichTextLineSpacingRule.AtLeast;
            lineSpacing = style.MinimumLineHeight;
        }
        else if (style.LineHeightMultiple > 0)
        {
            lineSpacingRule = Math.Abs((double)style.LineHeightMultiple - 1.5d) < 0.001d
                ? RichTextLineSpacingRule.OneAndHalf
                : Math.Abs((double)style.LineHeightMultiple - 2d) < 0.001d
                    ? RichTextLineSpacingRule.Double
                    : RichTextLineSpacingRule.Multiple;
            lineSpacing = style.LineHeightMultiple;
        }

        var expectedMinimum = format.MinimumLineHeight ?? 0;
        var expectedMaximum = format.MaximumLineHeight ?? 0;
        var expectedMultiple = 0d;
        var expectedSpacing = 0d;
        switch (format.LineSpacingRule)
        {
            case RichTextLineSpacingRule.Exactly:
                expectedMinimum = expectedMaximum = format.LineSpacing;
                break;
            case RichTextLineSpacingRule.AtLeast:
                expectedMinimum = format.LineSpacing;
                break;
            case RichTextLineSpacingRule.OneAndHalf:
                expectedMultiple = 1.5;
                break;
            case RichTextLineSpacingRule.Double:
                expectedMultiple = 2;
                break;
            case RichTextLineSpacingRule.Multiple:
                expectedMultiple = format.LineSpacing;
                break;
            default:
                expectedSpacing = format.LineSpacing;
                break;
        }

        var preservesLineSpacing =
            style.MinimumLineHeight == expectedMinimum && style.MaximumLineHeight == expectedMaximum &&
            style.LineHeightMultiple == expectedMultiple && style.LineSpacing == expectedSpacing;
        var nativeTabs = style.TabStops ?? [];
        var defaultTabs = NSParagraphStyle.Default.TabStops ?? [];
        var preservesTabs = format.TabStops.IsDefaultOrEmpty
            ? nativeTabs.Length == defaultTabs.Length && nativeTabs.Zip(defaultTabs).All(pair =>
                pair.First.Location == pair.Second.Location && pair.First.Alignment == pair.Second.Alignment)
            : nativeTabs.Length == format.TabStops.Length && nativeTabs.Zip(format.TabStops).All(pair =>
                pair.First.Location == pair.Second.Position && pair.First.Alignment == (pair.Second.Alignment switch
                {
                    RichTextTabAlignment.Center => UITextAlignment.Center,
                    RichTextTabAlignment.Right => UITextAlignment.Right,
                    _ => UITextAlignment.Left,
                }));

        return format with
        {
            Alignment = style.Alignment switch
            {
                UITextAlignment.Center => RichTextAlignment.Center,
                UITextAlignment.Right => RichTextAlignment.Right,
                UITextAlignment.Justified => format.Alignment == RichTextAlignment.Distributed
                    ? RichTextAlignment.Distributed : RichTextAlignment.Justified,
                _ => RichTextAlignment.Left,
            },
            Direction = style.BaseWritingDirection switch
            {
                NSWritingDirection.LeftToRight => RichTextDirection.LeftToRight,
                NSWritingDirection.RightToLeft => RichTextDirection.RightToLeft,
                _ => RichTextDirection.Automatic,
            },
            LeadingIndent = style.HeadIndent,
            FirstLineIndent = list is not null && (format.NativeList is not null && style.FirstLineHeadIndent == GetNativeFirstLineIndent(format) ||
                projectedList is not null && style.FirstLineHeadIndent == projectedList.TextIndent)
                ? format.FirstLineIndent : style.FirstLineHeadIndent - style.HeadIndent,
            TrailingIndent = style.TailIndent < 0 ? -style.TailIndent : 0,
            SpaceBefore = Math.Max(style.ParagraphSpacingBefore, 0),
            SpaceAfter = Math.Max(style.ParagraphSpacing, 0),
            LineSpacingRule = preservesLineSpacing ? format.LineSpacingRule : lineSpacingRule,
            LineSpacing = preservesLineSpacing ? format.LineSpacing : Math.Max(lineSpacing, 0),
            MinimumLineHeight = preservesLineSpacing ? format.MinimumLineHeight : style.MinimumLineHeight > 0 ? style.MinimumLineHeight : null,
            MaximumLineHeight = preservesLineSpacing ? format.MaximumLineHeight : style.MaximumLineHeight > 0 ? style.MaximumLineHeight : null,
            TabStops = preservesTabs ? format.TabStops : nativeTabs
                .Select(tab => new RichTextTabStop(
                    tab.Location,
                    tab.Alignment switch
                    {
                        UITextAlignment.Center => RichTextTabAlignment.Center,
                        UITextAlignment.Right => RichTextTabAlignment.Right,
                        _ => RichTextTabAlignment.Left,
                    }))
                .ToImmutableArray(),
            Hyphenation = style.HyphenationFactor > 0,
            List = list is null
                ? null
                : format.List ?? (list.Id > 0
                    ? RichTextListConversions.ToItem(list)
                    : null),
            NativeList = list,
        };
    }

}
#endif
