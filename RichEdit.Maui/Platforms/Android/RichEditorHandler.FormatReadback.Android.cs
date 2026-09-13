using System.Collections.Immutable;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;
using TextAlignment = Android.Text.Layout.Alignment;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichTextCharacterFormat ReadCharacterFormat(
        ISpanned text,
        int position,
        RichTextCharacterFormat defaultFormat)
    {
        var metadata = GetSpans<RichCharacterMetadataSpan>(text, position, position + 1).LastOrDefault();
        var format = metadata?.Format ?? defaultFormat;
        var expected = DisplaySourceSnapshot.ResolveCharacterFormat(format);
        var styles = GetSpans<StyleSpan>(text, position, position + 1).ToArray();
        if (metadata is not null)
        {
            var bold = styles.Any(span => (span.Style & TypefaceStyle.Bold) != 0);
            format = format with
            {
                FontWeight = bold == format.Bold ? format.FontWeight : bold ? 700 : 400,
                Italic = styles.Any(span => (span.Style & TypefaceStyle.Italic) != 0),
            };
        }

        foreach (var span in styles)
        {
            if (span.Style == TypefaceStyle.Normal)
            {
                format = format with { FontWeight = 400, Italic = false };
                continue;
            }

            if ((span.Style & TypefaceStyle.Bold) != 0)
            {
                format = format with { FontWeight = Math.Max(format.FontWeight, 700) };
            }

            if ((span.Style & TypefaceStyle.Italic) != 0)
            {
                format = format with { Italic = true };
            }
        }

        if (GetSpans<UnderlineSpan>(text, position, position + 1).Any())
        {
            format = format with
            {
                Underline = format.Underline == RichTextUnderlineStyle.None
                    ? RichTextUnderlineStyle.Single
                    : format.Underline,
            };
        }
        else if (metadata is not null)
            format = format with { Underline = RichTextUnderlineStyle.None };

        if (GetSpans<StrikethroughSpan>(text, position, position + 1).Any())
        {
            format = format with
            {
                Strikethrough = format.Strikethrough == RichTextStrikethroughStyle.None
                    ? RichTextStrikethroughStyle.Single
                    : format.Strikethrough,
            };
        }
        else if (metadata is not null)
            format = format with { Strikethrough = RichTextStrikethroughStyle.None };

        var family = GetSpans<TypefaceSpan>(text, position, position + 1)
            .Select(span => span.Family)
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (family is not null && (metadata is null || family != expected.FontFamily))
        {
            format = format with { FontFamily = family };
        }

        var absoluteSize = GetSpans<AbsoluteSizeSpan>(text, position, position + 1).LastOrDefault();
        if (absoluteSize is not null)
        {
            var size = absoluteSize.Dip
                ? absoluteSize.Size
                : FromPixels(absoluteSize.Size);
            format = format with { FontSize = Math.Max(size, 1) };
        }

        var ownedSize = GetSpans<RichFontSizeSpan>(text, position, position + 1).LastOrDefault();
        if (ownedSize is not null && (metadata is null || ownedSize.Size != expected.FontSize))
        {
            format = format with { FontSize = ownedSize.Size };
        }

        foreach (var span in GetSpans<RelativeSizeSpan>(text, position, position + 1))
        {
            format = format with
            {
                FontSize = Math.Max(ResolveFontSize(format.FontSize) * span.SizeChange, 1),
            };
        }

        if (GetSpans<SuperscriptSpan>(text, position, position + 1).Any())
        {
            format = format with { Script = RichTextScript.Superscript };
        }
        else if (GetSpans<SubscriptSpan>(text, position, position + 1).Any())
        {
            format = format with { Script = RichTextScript.Subscript };
        }

        var foreground = GetSpans<ForegroundColorSpan>(text, position, position + 1).LastOrDefault();
        if (foreground is not null && !format.Hidden &&
            (metadata is null || expected.ForegroundColor?.ToPlatform().ToArgb() != foreground.ForegroundColor))
        {
            format = format with
            {
                ForegroundColor = FromAndroidColor(
                    new Android.Graphics.Color(foreground.ForegroundColor)),
            };
        }

        var background = GetSpans<BackgroundColorSpan>(text, position, position + 1).LastOrDefault();
        if (background is not null &&
            (metadata is null || format.BackgroundColor?.ToPlatform().ToArgb() != background.BackgroundColor))
        {
            format = format with
            {
                BackgroundColor = FromAndroidColor(
                    new Android.Graphics.Color(background.BackgroundColor)),
            };
        }

        var scale = GetSpans<ScaleXSpan>(text, position, position + 1).LastOrDefault();
        if (scale is not null && scale.ScaleX > 0 && (metadata is null || scale.ScaleX != (float)format.HorizontalScale))
        {
            format = format with { HorizontalScale = scale.ScaleX };
        }

        var spacing = GetSpans<RichLetterSpacingSpan>(text, position, position + 1).LastOrDefault();
        if (spacing is not null && metadata is null)
        {
            format = format with
            {
                CharacterSpacing = spacing.Em * ResolveFontSize(format.FontSize),
            };
        }

        var baseline = GetSpans<RichBaselineOffsetSpan>(text, position, position + 1).LastOrDefault();
        if (baseline is not null && metadata is null)
        {
            format = format with { BaselineOffset = FromPixels(baseline.Pixels) };
        }

        var locale = GetSpans<LocaleSpan>(text, position, position + 1).LastOrDefault()?.Locale;
        if (locale is not null)
        {
            format = format with { LanguageTag = locale.ToLanguageTag() };
        }

        return format;
    }

    private RichTextParagraphFormat ReadParagraphFormat(
        ISpanned text,
        int start,
        int end,
        RichTextParagraphFormat defaultFormat)
    {
        var metadata = GetSpans<RichParagraphMetadataSpan>(text, start, end).LastOrDefault();
        var format = metadata?.Format ?? defaultFormat;
        var alignment = GetSpans<AlignmentSpanStandard>(text, start, end).LastOrDefault();
        if (alignment is not null)
        {
            var nativeAlignment = alignment.Alignment;
            format = format with
            {
                Alignment = nativeAlignment?.Equals(TextAlignment.AlignCenter) == true
                    ? RichTextAlignment.Center
                    : nativeAlignment?.Equals(TextAlignment.AlignOpposite) == true
                        ? RichTextAlignment.Right
                        : RichTextAlignment.Left,
            };
        }

        var margin = GetSpans<LeadingMarginSpanStandard>(text, start, end)
            .LastOrDefault(span => text.GetSpanStart(span) <= start && text.GetSpanEnd(span) > start);
        if (margin is not null && (metadata is null ||
            margin.GetLeadingMargin(true) != ToPixels(format.LeadingIndent + format.FirstLineIndent) ||
            margin.GetLeadingMargin(false) != ToPixels(format.LeadingIndent)))
        {
            var first = FromPixels(margin.GetLeadingMargin(true));
            var rest = FromPixels(margin.GetLeadingMargin(false));
            format = format with
            {
                LeadingIndent = rest,
                FirstLineIndent = first - rest,
            };
        }

        var richLineHeight = GetSpans<RichLineHeightSpan>(text, start, end).LastOrDefault();
        if (richLineHeight is not null && metadata is null)
        {
            format = format with
            {
                LineSpacingRule = richLineHeight.Rule,
                LineSpacing = richLineHeight.Rule is
                    RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly
                        ? FromPixels(richLineHeight.Value)
                        : richLineHeight.Value,
                MinimumLineHeight = richLineHeight.MinimumHeight is { } minimum
                    ? FromPixels(minimum)
                    : null,
                MaximumLineHeight = richLineHeight.MaximumHeight is { } maximum
                    ? FromPixels(maximum)
                    : null,
                SpaceBefore = FromPixels(richLineHeight.SpaceBefore),
                SpaceAfter = FromPixels(richLineHeight.SpaceAfter),
            };
        }
        else if (richLineHeight is null && OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var lineHeight = GetSpans<LineHeightSpanStandard>(text, start, end).LastOrDefault();
            if (lineHeight is not null)
            {
                format = format with
                {
                    LineSpacingRule = RichTextLineSpacingRule.Exactly,
                    LineSpacing = FromPixels(lineHeight.Height),
                };
            }
        }

        var tabs = GetSpans<TabStopSpanStandard>(text, start, end)
            .Select(span => new RichTextTabStop(FromPixels(span.TabStop)))
            .OrderBy(tab => tab.Position)
            .DistinctBy(tab => tab.Position)
            .ToImmutableArray();
        var expectedTabs = format.TabStops.Where(tab => tab.Alignment == RichTextTabAlignment.Left)
            .Select(tab => FromPixels(ToPixels(tab.Position))).Order().Distinct();
        if (!tabs.IsDefaultOrEmpty && (metadata is null || !tabs.Select(tab => tab.Position).SequenceEqual(expectedTabs)))
        {
            format = format with { TabStops = tabs };
        }

        var listMarker = GetSpans<RichListMarkerSpan>(text, start, end).LastOrDefault();
        if (listMarker is not null)
        {
            format = format with
            {
                NativeList = listMarker.ListFormat,
                List = RichTextListConversions.ToItem(listMarker.ListFormat),
            };
        }
        else if (GetSpans<BulletSpan>(text, start, end).Any())
        {
            format = format with
            {
                NativeList = (format.NativeList ?? new RichTextListFormat { Id = 0 }) with
                {
                    Kind = RichListKind.Bulleted,
                },
            };
        }
        else
        {
            // RichParagraphMetadataSpan preserves model-only paragraph values,
            // but the marker span is the native authority for active list state.
            // Android removes or splits that marker as the user edits paragraph
            // boundaries, so never resurrect a list from stale metadata.
            format = format with { List = null, NativeList = null };
        }

        return format;
    }

    private static IReadOnlyList<RichTextLink> ReadLinks(ISpanned text, int textLength)
    {
        var links = new List<RichTextLink>();
        var previousEnd = 0;
        foreach (var span in GetSpans<URLSpan>(text, 0, textLength)
                     .OrderBy(span => text.GetSpanStart(span)))
        {
            var start = text.GetSpanStart(span);
            var end = text.GetSpanEnd(span);
            if (start < previousEnd || start < 0 || end <= start || end > textLength ||
                string.IsNullOrWhiteSpace(span.URL))
            {
                continue;
            }

            links.Add(new RichTextLink(start, end - start, span.URL));
            previousEnd = end;
        }

        return links;
    }

    private IReadOnlyList<RichTextImage> ReadImages(ISpanned text, string plainText)
    {
        var images = new Dictionary<int, RichTextImage>();
        foreach (var span in GetSpans<RichImageMetadataSpan>(text, 0, plainText.Length))
        {
            var position = text.GetSpanStart(span);
            if (position >= 0 && position < plainText.Length &&
                plainText[position] == RichTextDocument.ObjectReplacementCharacter)
            {
                images[position] = span.Image with { Position = position };
            }
        }

        foreach (var span in GetSpans<ImageSpan>(text, 0, plainText.Length))
        {
            var position = text.GetSpanStart(span);
            if (images.ContainsKey(position) || position < 0 || position >= plainText.Length ||
                plainText[position] != RichTextDocument.ObjectReplacementCharacter)
            {
                continue;
            }

            var bounds = span.Drawable?.Bounds;
            images[position] = new RichTextImage
            {
                Position = position,
                MediaType = "application/octet-stream",
                Source = span.Source,
                Width = bounds is null ? 0 : FromPixels(bounds.Width()),
                Height = bounds is null ? 0 : FromPixels(bounds.Height()),
                AlternativeText = OperatingSystem.IsAndroidVersionAtLeast(30)
                    ? span.ContentDescription
                    : null,
            };
        }

        return [.. images.Values.OrderBy(image => image.Position)];
    }
}
