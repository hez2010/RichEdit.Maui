#if IOS || MACCATALYST
using System.Collections.Immutable;
using System.Runtime.Versioning;
using CoreGraphics;
using CoreText;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;


namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyCharacterFormatsIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextRange range,
        RichTextDocumentSnapshot? previousSnapshot = null)
    {
        if (range.IsEmpty || snapshot.Length == 0)
        {
            return;
        }

        var formats = new Dictionary<(RichTextCharacterFormat? Before, RichTextCharacterFormat After),
            (NSMutableDictionary Attributes, NSObject[] Removed)>();
        try
        {
            foreach (var change in GetCharacterFormatChanges(snapshot, range, previousSnapshot))
            {
                var key = (change.PreviousFormat, change.Format);
                if (!formats.TryGetValue(key, out var update))
                {
                    var attributes = CreateCharacterAttributes(change.Format, snapshot.DefaultCharacterFormat);
                    NSObject[] removed;
                    if (change.PreviousFormat is { } previous)
                    {
                        using var oldAttributes = CreateCharacterAttributes(previous, previousSnapshot!.DefaultCharacterFormat);
                        removed = oldAttributes.Keys.Where(key => !attributes.ContainsKey(key)).ToArray();
                        foreach (var attribute in attributes.Keys)
                        {
                            // Metadata describes authored values; native attributes include inherited values.
                            var unchanged = attribute.Equals(CharacterMetadataKey)
                                ? previous == change.Format
                                : oldAttributes[attribute] is { } value && attributes[attribute].IsEqual(value);
                            if (unchanged)
                                attributes.Remove(attribute);
                        }
                    }
                    else
                    {
                        removed =
                        [
                            UIStringAttributeKey.BackgroundColor, UIStringAttributeKey.UnderlineColor,
                            UIStringAttributeKey.StrikethroughColor, UIStringAttributeKey.StrokeColor,
                            UIStringAttributeKey.StrokeWidth, UIStringAttributeKey.Shadow,
                            UIStringAttributeKey.Ligature, UIStringAttributeKey.WritingDirection,
                        ];
                    }

                    update = (attributes, removed);
                    formats.Add(key, update);
                }

                var nativeRange = new NSRange(change.Range.Start, change.Range.Length);
                foreach (var attribute in update.Removed)
                    PlatformView.TextStorage.RemoveAttribute((NSString)attribute, nativeRange);
                if (update.Attributes.Count != 0)
                    PlatformView.TextStorage.AddAttributes(update.Attributes, nativeRange);
            }
        }
        finally
        {
            foreach (var update in formats.Values)
                update.Attributes.Dispose();
        }
    }

    private NSMutableDictionary CreateCharacterAttributes(
        RichTextCharacterFormat format,
        RichTextCharacterFormat? inheritedFormat = null)
    {
        var authoredFormat = format;
        if (inheritedFormat is not null)
        {
            format = format with
            {
                FontFamily = format.FontFamily ?? inheritedFormat.FontFamily,
                FontSize = format.FontSize ?? inheritedFormat.FontSize,
                ForegroundColor = format.ForegroundColor ?? inheritedFormat.ForegroundColor,
            };
        }

        var foreground = format.Hidden
            ? UIColor.Clear
            : format.ForegroundColor?.ToPlatform() ??
              VirtualView.TextColor?.ToPlatform() ??
              _defaultTextColor;
        var font = ResolveFont(format);
        var attributes = new UIStringAttributes
        {
            Font = font,
            ForegroundColor = foreground,
            UnderlineStyle = ToNativeUnderline(format.Underline),
            StrikethroughStyle = format.Strikethrough switch
            {
                RichTextStrikethroughStyle.Double => NSUnderlineStyle.Double,
                RichTextStrikethroughStyle.Single => NSUnderlineStyle.Single,
                _ => NSUnderlineStyle.None,
            },
            BaselineOffset = (float)GetNativeBaselineOffset(format, font.PointSize),
            KerningAdjustment = (float)format.CharacterSpacing,
            Expansion = (float)(format.HorizontalScale - 1d),
        };

        if (format.BackgroundColor is not null)
        {
            attributes.BackgroundColor = format.BackgroundColor.ToPlatform();
        }

        if (format.UnderlineColor is not null)
        {
            attributes.UnderlineColor = format.UnderlineColor.ToPlatform();
        }

        if (format.StrikethroughColor is not null)
        {
            attributes.StrikethroughColor = format.StrikethroughColor.ToPlatform();
        }

        if (format.Outline)
        {
            attributes.StrokeColor = foreground;
            attributes.StrokeWidth = -3f;
        }

        if (format.Shadow)
        {
            attributes.Shadow = new NSShadow
            {
                ShadowBlurRadius = 1,
                ShadowColor = foreground.ColorWithAlpha(0.55f),
                ShadowOffset = new CGSize(1, 1),
            };
        }

        if (format.Ligatures != RichTextFeatureMode.Automatic)
        {
            attributes.Ligature = format.Ligatures == RichTextFeatureMode.Enabled
                ? NSLigatureType.Default
                : NSLigatureType.None;
        }

        if (format.Direction != RichTextDirection.Automatic)
        {
            attributes.WritingDirectionInt =
            [
                NSNumber.FromInt32(format.Direction == RichTextDirection.RightToLeft
                    ? (int)NSWritingDirection.RightToLeft
                    : (int)NSWritingDirection.LeftToRight),
            ];
        }

        var dictionary = new NSMutableDictionary(attributes.Dictionary);
        dictionary[CharacterMetadataKey] = new CharacterMetadata(authoredFormat);
        return dictionary;
    }

    private NSMutableDictionary CreateParagraphAttributes(
        RichTextParagraphFormat format,
        NSTextList[]? textLists = null)
    {
        var style = new NSMutableParagraphStyle
        {
            Alignment = format.Alignment switch
            {
                RichTextAlignment.Center => UITextAlignment.Center,
                RichTextAlignment.Right => UITextAlignment.Right,
                RichTextAlignment.Justified or RichTextAlignment.Distributed =>
                    UITextAlignment.Justified,
                _ => UITextAlignment.Left,
            },
            BaseWritingDirection = format.Direction switch
            {
                RichTextDirection.LeftToRight => NSWritingDirection.LeftToRight,
                RichTextDirection.RightToLeft => NSWritingDirection.RightToLeft,
                _ => NSWritingDirection.Natural,
            },
            HeadIndent = (nfloat)format.LeadingIndent,
            FirstLineHeadIndent = (nfloat)GetNativeFirstLineIndent(format),
            TailIndent = format.TrailingIndent == 0 ? 0 : (nfloat)(-format.TrailingIndent),
            ParagraphSpacingBefore = (nfloat)format.SpaceBefore,
            ParagraphSpacing = (nfloat)format.SpaceAfter,
            HyphenationFactor = format.Hyphenation ? 1f : 0f,
            MinimumLineHeight = (nfloat)(format.MinimumLineHeight ?? 0),
            MaximumLineHeight = (nfloat)(format.MaximumLineHeight ?? 0),
        };

        switch (format.LineSpacingRule)
        {
            case RichTextLineSpacingRule.OneAndHalf:
                style.LineHeightMultiple = 1.5f;
                break;
            case RichTextLineSpacingRule.Double:
                style.LineHeightMultiple = 2f;
                break;
            case RichTextLineSpacingRule.Multiple:
                style.LineHeightMultiple = (nfloat)format.LineSpacing;
                break;
            case RichTextLineSpacingRule.Exactly:
                style.MinimumLineHeight = (nfloat)format.LineSpacing;
                style.MaximumLineHeight = (nfloat)format.LineSpacing;
                break;
            case RichTextLineSpacingRule.AtLeast:
                style.MinimumLineHeight = (nfloat)format.LineSpacing;
                break;
            default:
                style.LineSpacing = (nfloat)format.LineSpacing;
                break;
        }

        if (!format.TabStops.IsDefaultOrEmpty)
        {
            style.TabStops = format.TabStops
                .Select(tab => new NSTextTab(
                    tab.Alignment switch
                    {
                        RichTextTabAlignment.Center => UITextAlignment.Center,
                        RichTextTabAlignment.Right => UITextAlignment.Right,
                        RichTextTabAlignment.Decimal => UITextAlignment.Natural,
                        _ => UITextAlignment.Left,
                    },
                    (nfloat)tab.Position,
                    new NSDictionary()))
                .ToArray();
        }

        if (format.NativeList is not null)
        {
            if (OperatingSystem.IsIOSVersionAtLeast(16) ||
                OperatingSystem.IsMacCatalystVersionAtLeast(16))
            {
                if (textLists is null)
                {
                    ApplyNativeTextList(style, format);
                }
                else
                {
                    style.TextLists = textLists;
                }
            }
        }

        var attributes = new UIStringAttributes { ParagraphStyle = style };
        var dictionary = new NSMutableDictionary(attributes.Dictionary);
        dictionary[ParagraphMetadataKey] = new ParagraphMetadata(format);
        return dictionary;
    }

    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    private static Dictionary<int, NSTextList[]> CreateNativeTextLists(
        RichTextDocumentSnapshot document,
        List<NSTextList> ownedTextLists,
        HashSet<int>? includedListIds = null)
    {
        var definitions = new Dictionary<(int Id, int Level), RichTextListFormat>();
        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.Format.NativeList is { } list &&
                (includedListIds is null || includedListIds.Contains(list.Id)))
            {
                definitions.TryAdd((list.Id, list.Level), list);
            }
        }

        var result = new Dictionary<int, NSTextList[]>();
        var activeLists = new Dictionary<int, NSTextList?[]>();
        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.Format.NativeList is not { } list ||
                includedListIds is not null && !includedListIds.Contains(list.Id))
            {
                continue;
            }

            var level = Math.Clamp(list.Level, 0, 8);
            if (!activeLists.TryGetValue(list.Id, out var levels))
            {
                levels = new NSTextList?[9];
                activeLists.Add(list.Id, levels);
            }

            if (list.Restart)
            {
                levels[level] = null;
                Array.Clear(levels, level + 1, levels.Length - level - 1);
            }

            for (var outerLevel = 0; outerLevel <= level; outerLevel++)
            {
                if (levels[outerLevel] is not null)
                {
                    continue;
                }

                var definition = definitions.GetValueOrDefault(
                    (list.Id, outerLevel),
                    list with
                    {
                        Level = outerLevel,
                        Restart = false,
                        StartAt = 1,
                    });
                if (outerLevel == level && list.Restart)
                {
                    definition = list;
                }

                var textList = CreateNativeTextList(definition, document.Lists[new RichTextListId(list.Id)].Levels[outerLevel]);
                levels[outerLevel] = textList;
                ownedTextLists.Add(textList);
            }

            var paragraphLists = new NSTextList[level + 1];
            for (var outerLevel = 0; outerLevel <= level; outerLevel++)
            {
                paragraphLists[outerLevel] = levels[outerLevel]!;
            }

            result.Add(paragraph.Start, paragraphLists);
        }

        return result;
    }

    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    private static void ApplyNativeTextList(
        NSMutableParagraphStyle style,
        RichTextParagraphFormat format)
    {
        var list = format.NativeList!;
        var level = RichTextListConversions.ToLevel(list) with
        {
            LeadingIndent = format.LeadingIndent,
            FirstLineIndent = format.FirstLineIndent,
            MarkerTab = GetNativeFirstLineIndent(format),
        };
        var textList = CreateNativeTextList(list, level);
        style.TextLists = Enumerable.Repeat(textList, list.Level + 1).ToArray();
        textList.Dispose();
    }

    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    private static NSTextList CreateNativeTextList(RichTextListFormat list, RichTextListLevelDefinition level)
    {
        var markerFormat = list.Kind == RichListKind.Bulleted
            ? (string.IsNullOrEmpty(list.BulletText) ? "{disc}" : list.BulletText)
            : string.Concat(
                list.Prefix,
                list.NumberStyle switch
                {
                    RichListNumberStyle.UpperRoman => "{upper-roman}",
                    RichListNumberStyle.LowerRoman => "{lower-roman}",
                    RichListNumberStyle.UpperLetter => "{upper-alpha}",
                    RichListNumberStyle.LowerLetter => "{lower-alpha}",
                    _ => "{decimal}",
                },
                list.Suffix);
        var textList = new NSTextList(
            markerFormat,
            NSTextListOptions.None,
            list.StartAt);
        if (list.Id > 0)
        {
            var metadata = new NativeListMetadata(list with
            {
                Restart = false,
                StartAt = level.Marker is RichTextListMarker.Number number ? number.StartAt : 1,
            }, level.FirstLineIndent, level.MarkerTab > 0 ? level.MarkerTab : level.LeadingIndent);
            SetAssociatedListMetadata(textList.Handle, ParagraphMetadataKey.Handle, metadata.Handle, 1); // OBJC_ASSOCIATION_RETAIN_NONATOMIC
            GC.KeepAlive(metadata);
        }

        return textList;
    }

    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("maccatalyst16.0")]
    private static RichTextListFormat? ReadNativeTextList(NSParagraphStyle style)
    {
        var textLists = style.TextLists;
        if (textLists is null || textLists.Length == 0)
        {
            return null;
        }

        var textList = textLists[^1];
        var level = textLists.Length - 1;
        var marker = textList.MarkerFormat;
        var kind = marker is NSTextListMarkerFormats.Disc or
            NSTextListMarkerFormats.Circle or
            NSTextListMarkerFormats.Square or
            NSTextListMarkerFormats.Diamond or
            NSTextListMarkerFormats.Box or
            NSTextListMarkerFormats.Check or
            NSTextListMarkerFormats.Hyphen
            ? RichListKind.Bulleted
            : RichListKind.Numbered;
        var numberStyle = marker == NSTextListMarkerFormats.UppercaseRoman
            ? RichListNumberStyle.UpperRoman
            : marker == NSTextListMarkerFormats.LowercaseRoman
                ? RichListNumberStyle.LowerRoman
                : marker is NSTextListMarkerFormats.UppercaseAlpha or
                    NSTextListMarkerFormats.UppercaseLatin
                    ? RichListNumberStyle.UpperLetter
                    : marker is NSTextListMarkerFormats.LowercaseAlpha or
                        NSTextListMarkerFormats.LowercaseLatin
                        ? RichListNumberStyle.LowerLetter
                        : RichListNumberStyle.Arabic;
        return new RichTextListFormat
        {
            Id = 0,
            Level = Math.Clamp(level, 0, 8),
            Kind = kind,
            NumberStyle = numberStyle,
            StartAt = Math.Max((int)textList.StartingItemNumber, 1),
            Suffix = kind == RichListKind.Numbered ? "." : string.Empty,
        };
    }

    private UIFont ResolveFont(RichTextCharacterFormat format)
    {
        var family = format.FontFamily ?? VirtualView.FontFamily;
        var size = (nfloat)(format.FontSize ?? VirtualView.FontSize ?? _defaultFont.PointSize);
        var font = (string.IsNullOrWhiteSpace(family)
            ? UIFont.FromDescriptor(_defaultFont.FontDescriptor, size) ?? _defaultFont
            : UIFont.FromName(family, size) ?? UIFont.SystemFontOfSize(size))!;

        var traits = (UIFontDescriptorSymbolicTraits)0;
        if (format.Bold)
        {
            traits |= UIFontDescriptorSymbolicTraits.Bold;
        }

        if (format.Italic)
        {
            traits |= UIFontDescriptorSymbolicTraits.Italic;
        }

        if (traits != 0 && font.FontDescriptor.CreateWithTraits(traits) is { } descriptor)
        {
            font = UIFont.FromDescriptor(descriptor, size) ?? font;
        }

        if (format.SmallCaps || format.AllCaps)
        {
            var attributes = font.FontDescriptor.FontAttributes;
#pragma warning disable CA1422 // The iOS 15-compatible feature-selector API is deprecated but still supported.
            attributes.FeatureSettings =
            [
                new UIFontFeature(format.AllCaps
                    ? CTFontFeatureLetterCase.Selector.AllCaps
                    : CTFontFeatureLetterCase.Selector.SmallCaps),
            ];
#pragma warning restore CA1422
            var featureDescriptor = font.FontDescriptor.CreateWithAttributes(attributes);
            font = UIFont.FromDescriptor(featureDescriptor, size) ?? font;
        }

        return font;
    }

    private static NSUnderlineStyle ToNativeUnderline(RichTextUnderlineStyle value) => value switch
    {
        RichTextUnderlineStyle.None => NSUnderlineStyle.None,
        RichTextUnderlineStyle.Words => NSUnderlineStyle.Single | NSUnderlineStyle.ByWord,
        RichTextUnderlineStyle.Double => NSUnderlineStyle.Double,
        RichTextUnderlineStyle.Dotted => NSUnderlineStyle.Single | NSUnderlineStyle.PatternDot,
        RichTextUnderlineStyle.Dash => NSUnderlineStyle.Single | NSUnderlineStyle.PatternDash,
        RichTextUnderlineStyle.DashDot => NSUnderlineStyle.Single | NSUnderlineStyle.PatternDashDot,
        RichTextUnderlineStyle.DashDotDot =>
            NSUnderlineStyle.Single | NSUnderlineStyle.PatternDashDotDot,
        RichTextUnderlineStyle.Thick => NSUnderlineStyle.Thick,
        _ => NSUnderlineStyle.Single,
    };

    private static RichTextUnderlineStyle FromNativeUnderline(NSUnderlineStyle value)
    {
        if (value == NSUnderlineStyle.None)
        {
            return RichTextUnderlineStyle.None;
        }

        if (value.HasFlag(NSUnderlineStyle.ByWord))
        {
            return RichTextUnderlineStyle.Words;
        }

        if (value.HasFlag(NSUnderlineStyle.Double))
        {
            return RichTextUnderlineStyle.Double;
        }

        // PatternDashDot contains the PatternDot bit; these are a masked
        // enum field, not independent flags.
        return ((long)value & 0x0F00) switch
        {
            (long)NSUnderlineStyle.PatternDot => RichTextUnderlineStyle.Dotted,
            (long)NSUnderlineStyle.PatternDash => RichTextUnderlineStyle.Dash,
            (long)NSUnderlineStyle.PatternDashDot => RichTextUnderlineStyle.DashDot,
            (long)NSUnderlineStyle.PatternDashDotDot => RichTextUnderlineStyle.DashDotDot,
            _ => value.HasFlag(NSUnderlineStyle.Thick) ? RichTextUnderlineStyle.Thick : RichTextUnderlineStyle.Single,
        };
    }

    private static double GetNativeBaselineOffset(
        RichTextCharacterFormat format,
        double defaultFontSize) =>
            format.BaselineOffset + format.Script switch
            {
                RichTextScript.Superscript => (format.FontSize ?? defaultFontSize) * 0.35d,
                RichTextScript.Subscript => (format.FontSize ?? defaultFontSize) * -0.15d,
                _ => 0,
            };

    private static Color FromUIColor(UIColor color)
    {
        color.GetRGBA(out var red, out var green, out var blue, out var alpha);
        return Color.FromRgba((float)red, (float)green, (float)blue, (float)alpha);
    }

    private static int GetParagraphStart(string text, int position) =>
        position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

    private static int GetParagraphEnd(string text, int start)
    {
        var newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }

    private sealed class CharacterMetadata(RichTextCharacterFormat format) : NSObject
    {
        public RichTextCharacterFormat Format { get; } = format;
    }

    private sealed class ParagraphMetadata(RichTextParagraphFormat format) : NSObject
    {
        public RichTextParagraphFormat Format { get; } = format;
    }

    private sealed class ImageMetadata(RichTextImage image) : NSObject
    {
        public RichTextImage Image { get; } = image;
    }
}
#endif
