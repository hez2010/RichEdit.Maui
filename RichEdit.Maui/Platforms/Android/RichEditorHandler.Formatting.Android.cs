using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;
using TextAlignment = Android.Text.Layout.Alignment;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyCharacterFormatsIncrementally(
        ISpannable editable,
        RichTextDocumentSnapshot snapshot,
        RichTextRange range,
        RichTextDocumentSnapshot? previousSnapshot = null)
    {
        if (range.IsEmpty || snapshot.Length == 0)
        {
            return;
        }

        var ranges = new List<RichTextRange>();
        if (previousSnapshot is null)
            ranges.Add(ExpandCharacterSpanRange(editable, range, snapshot.Length));
        else
        {
            foreach (var change in GetCharacterFormatChanges(snapshot, range, previousSnapshot))
            {
                if (ranges.Count > 0 && ranges[^1].End >= change.Range.End)
                    continue;

                var expanded = ExpandCharacterSpanRange(editable, change.Range, snapshot.Length);
                while (ranges.Count > 0 && ranges[^1].End >= expanded.Start)
                {
                    var previous = ranges[^1];
                    var start = Math.Min(previous.Start, expanded.Start);
                    expanded = new(start, Math.Max(previous.End, expanded.End) - start);
                    ranges.RemoveAt(ranges.Count - 1);
                }

                ranges.Add(expanded);
            }
        }

        var metadataRanges = new List<RichTextRange>();
        foreach (var affected in ranges)
        {
            // Rebuild whole intersecting spans so their ordering and unaffected tails survive.
            RemoveCharacterSpans(editable, affected.Start, affected.End);
            for (var index = snapshot.FindRunIndex(affected.Start); index < snapshot.Runs.Length; index++)
            {
                var run = snapshot.Runs[index];
                if (run.Start >= affected.End)
                    break;

                var start = Math.Max(run.Start, affected.Start);
                var end = Math.Min(run.End, affected.End);
                if (end > start)
                    ApplyCharacterFormat(editable, start, end, run.Format, snapshot.DefaultCharacterFormat, includeMetadata: false);
            }

            // Keep metadata aligned with normalized model runs when formatting boundaries disappear.
            // This also avoids leaving thousands of equivalent metadata fragments after clearing a layer.
            var metadataStart = snapshot.Runs[snapshot.FindRunIndex(affected.Start)].Start;
            var metadataEnd = snapshot.Runs[snapshot.FindRunIndex(affected.End - 1)].End;
            if (metadataRanges.Count > 0 && metadataRanges[^1].End >= metadataStart)
                metadataRanges[^1] = new(metadataRanges[^1].Start, metadataEnd - metadataRanges[^1].Start);
            else
                metadataRanges.Add(new(metadataStart, metadataEnd - metadataStart));
        }

        foreach (var affected in metadataRanges)
        {
            RemoveSpans<RichCharacterMetadataSpan>(editable, affected.Start, affected.End);
            for (var index = snapshot.FindRunIndex(affected.Start); index < snapshot.Runs.Length; index++)
            {
                var run = snapshot.Runs[index];
                if (run.Start >= affected.End)
                    break;

                editable.SetSpan(new RichCharacterMetadataSpan(run.Format), run.Start, run.End, SpanTypes.ExclusiveExclusive);
            }
        }
    }

    private void ApplyParagraphFormatsIncrementally(
        ISpannable editable,
        RichTextDocumentSnapshot snapshot,
        RichTextRange range)
    {
        // Marker text contains its counter value. Recompute subsequent items when
        // a paragraph is inserted, removed, restarted, or taken out of a list.
        var listIds = GetSpans<RichListMarkerSpan>(editable, range.Start, range.End)
            .Select(span => span.ListFormat.Id).ToHashSet();
        foreach (var paragraph in snapshot.Paragraphs.Where(paragraph =>
            paragraph.Range.Start <= range.End && paragraph.Range.End >= range.Start))
        {
            if (paragraph.Format.List is { } item)
            {
                listIds.Add(item.ListId.Value);
            }
        }

        foreach (var paragraph in snapshot.Paragraphs.Where(paragraph =>
            paragraph.Format.List is { } item && listIds.Contains(item.ListId.Value)))
        {
            var start = Math.Min(range.Start, paragraph.Range.Start);
            range = new RichTextRange(start, Math.Max(range.End, paragraph.Range.End) - start);
        }

        RemoveParagraphSpans(editable, range.Start, range.End);
        var firstIndex = snapshot.FindParagraphIndex(range.Start);
        var counters = new Dictionary<(int Id, int Level), int>();
        for (var index = 0; index < firstIndex; index++)
        {
            _ = AdvanceListMarker(snapshot.Paragraphs[index].Format.NativeList, counters);
        }

        var pictures = new Dictionary<string, Drawable?>(StringComparer.Ordinal);
        for (var index = firstIndex;
             index < snapshot.Paragraphs.Length;
             index++)
        {
            var paragraph = snapshot.Paragraphs[index];
            if (paragraph.Range.Start > range.End ||
                (!range.IsEmpty && paragraph.Range.Start == range.End))
            {
                break;
            }

            if (paragraph.Range.End < range.Start)
            {
                continue;
            }

            var end = GetParagraphEnd(snapshot.Text, paragraph.Start);
            var list = paragraph.Format.NativeList;
            var marker = AdvanceListMarker(list, counters);
            Drawable? picture = null;
            if (list?.PictureId is { } pictureId &&
                !pictures.TryGetValue(pictureId, out picture) &&
                snapshot.ListPictures.TryGetValue(pictureId, out var modelPicture))
            {
                picture = CreateBitmapDrawable(
                    modelPicture.Data,
                    modelPicture.Width,
                    modelPicture.Height);
                pictures.Add(pictureId, picture);
            }

            ApplyParagraphFormat(
                editable,
                paragraph.Start,
                end,
                paragraph.Format,
                marker,
                picture);
        }
    }

    private static string? AdvanceListMarker(
        RichTextListFormat? list,
        Dictionary<(int Id, int Level), int> counters)
    {
        if (list is null)
        {
            return null;
        }

        if (list.Kind == RichListKind.Bulleted)
        {
            return list.BulletText;
        }

        var key = (list.Id, list.Level);
        if (list.Restart || !counters.TryGetValue(key, out var number))
        {
            number = list.StartAt;
        }

        var marker = RichTextListFormatter.FormatMarker(list, number);
        counters[key] = number == int.MaxValue ? number : number + 1;
        return marker;
    }

    private void UpdateGlobalParagraphProjection(RichTextDocumentSnapshot snapshot)
    {
        var allJustified = snapshot.Paragraphs.All(paragraph =>
            paragraph.Format.Alignment is RichTextAlignment.Justified or
                RichTextAlignment.Distributed);
        var allHyphenated = snapshot.Paragraphs.All(static paragraph =>
            paragraph.Format.Hyphenation);
        var allLeftToRight = snapshot.Paragraphs.All(static paragraph =>
            paragraph.Format.Direction == RichTextDirection.LeftToRight);
        var allRightToLeft = snapshot.Paragraphs.All(static paragraph =>
            paragraph.Format.Direction == RichTextDirection.RightToLeft);
        var justification = allJustified
            ? JustificationMode.InterWord
            : JustificationMode.None;
        var hyphenation = allHyphenated
            ? global::Android.Text.HyphenationFrequency.Normal
            : global::Android.Text.HyphenationFrequency.None;
        var direction = allRightToLeft
            ? TextDirection.Rtl
            : allLeftToRight
                ? TextDirection.Ltr
                : TextDirection.FirstStrong;
        // These TextView setters discard DynamicLayout even when the value is unchanged.
        if (PlatformView.JustificationMode != justification)
            PlatformView.JustificationMode = justification;
        if (PlatformView.HyphenationFrequency != hyphenation)
            PlatformView.HyphenationFrequency = hyphenation;
        if (PlatformView.TextDirection != direction)
            PlatformView.TextDirection = direction;
    }

    private static RichTextRange GetAffectedRange(
        RichTextChangeSet changes,
        int documentLength) =>
            changes.GetAffectedRange(documentLength);

    private static RichTextRange GetAffectedParagraphRange(
        RichTextChangeSet changes,
        string text) =>
            GetAffectedParagraphRange(GetAffectedRange(changes, text.Length), text);

    private static RichTextRange GetAffectedParagraphRange(
        RichTextRange range,
        string text)
    {
        range = range.Clamp(text.Length);
        var start = range.Start == 0 ? 0 : text.LastIndexOf('\n', range.Start - 1) + 1;
        var newline = text.IndexOf('\n', range.End);
        var end = newline < 0 ? text.Length : newline + 1;
        return new RichTextRange(start, end - start);
    }

    private static RichTextRange ExpandCharacterSpanRange(
        ISpanned text,
        RichTextRange range,
        int documentLength)
    {
        var start = range.Start;
        var end = range.End;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var span in EnumerateCharacterSpans(text, start, end))
            {
                var spanStart = text.GetSpanStart(span);
                var spanEnd = text.GetSpanEnd(span);
                if (spanStart >= 0 && spanStart < start)
                {
                    start = spanStart;
                    changed = true;
                }

                if (spanEnd > end)
                {
                    end = spanEnd;
                    changed = true;
                }
            }
        }

        start = Math.Clamp(start, 0, documentLength);
        end = Math.Clamp(end, start, documentLength);
        return new RichTextRange(start, end - start);
    }

    private static IEnumerable<Java.Lang.Object> EnumerateCharacterSpans(
        ISpanned text,
        int start,
        int end) =>
            GetSpans<Java.Lang.Object>(text, start, end)
                .Where(static span => span is
                    RichCharacterMetadataSpan or
                    RichCharacterEffectsSpan or
                    RichSmallCapsSpan or
                    StyleSpan or
                    UnderlineSpan or
                    StrikethroughSpan or
                    TypefaceSpan or
                    AbsoluteSizeSpan or
                    RichFontSizeSpan or
                    RelativeSizeSpan or
                    SuperscriptSpan or
                    SubscriptSpan or
                    ForegroundColorSpan or
                    BackgroundColorSpan or
                    ScaleXSpan or
                    RichLetterSpacingSpan or
                    RichBaselineOffsetSpan or
                    LocaleSpan);

    private static void RemoveCharacterSpans(ISpannable text, int start, int end)
    {
        foreach (var span in EnumerateCharacterSpans(text, start, end).Where(static span => span is not RichCharacterMetadataSpan).ToArray())
        {
            text.RemoveSpan(span);
        }
    }

    private static void RemoveParagraphSpans(ISpannable text, int start, int end)
    {
        RemoveIntersectingParagraphSpans<RichParagraphMetadataSpan>(text, start, end);
        RemoveIntersectingParagraphSpans<AlignmentSpanStandard>(text, start, end);
        RemoveIntersectingParagraphSpans<LeadingMarginSpanStandard>(text, start, end);
        RemoveIntersectingParagraphSpans<RichLineHeightSpan>(text, start, end);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            RemoveIntersectingParagraphSpans<LineHeightSpanStandard>(text, start, end);
        }

        RemoveIntersectingParagraphSpans<TabStopSpanStandard>(text, start, end);
        RemoveIntersectingParagraphSpans<RichListMarkerSpan>(text, start, end);
        RemoveIntersectingParagraphSpans<BulletSpan>(text, start, end);
        RemoveIntersectingParagraphSpans<RichParagraphDecorationSpan>(text, start, end);
    }

    private static void RemoveIntersectingParagraphSpans<T>(
        ISpannable text,
        int start,
        int end)
        where T : Java.Lang.Object
    {
        foreach (var span in GetSpans<T>(text, start, end).ToArray())
        {
            var spanStart = text.GetSpanStart(span);
            var spanEnd = text.GetSpanEnd(span);
            var intersects = start == end
                ? spanStart == start || spanStart < start && spanEnd > start
                : spanStart < end && spanEnd > start ||
                  // Android can collapse a paragraph span onto the edit boundary
                  // when the paragraph delimiter is inserted or removed.
                  spanStart == spanEnd && spanStart >= start && spanStart <= end;
            if (intersects)
            {
                text.RemoveSpan(span);
            }
        }
    }

    private static void RemoveSpans<T>(ISpannable text, int start, int end)
        where T : Java.Lang.Object
    {
        foreach (var span in GetSpans<T>(text, start, end).ToArray())
        {
            text.RemoveSpan(span);
        }
    }

    private void ApplyCharacterFormat(
        ISpannable text,
        int start,
        int end,
        RichTextCharacterFormat format,
        RichTextCharacterFormat? inheritedFormat = null,
        bool includeMetadata = true)
    {
        if (end <= start)
        {
            return;
        }

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

        if (includeMetadata)
            text.SetSpan(new RichCharacterMetadataSpan(authoredFormat), start, end, SpanTypes.ExclusiveExclusive);

        if (format.SmallCaps)
            text.SetSpan(new RichSmallCapsSpan(), start, end, SpanTypes.ExclusiveExclusive);
        if (format.Hidden || format.Shadow || format.Outline)
            text.SetSpan(new RichCharacterEffectsSpan(format), start, end, SpanTypes.ExclusiveExclusive);

        var style = TypefaceStyle.Normal;
        if (format.Bold)
        {
            style |= TypefaceStyle.Bold;
        }

        if (format.Italic)
        {
            style |= TypefaceStyle.Italic;
        }

        if (style != TypefaceStyle.Normal)
        {
            text.SetSpan(new StyleSpan(style), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (format.Underline != RichTextUnderlineStyle.None)
        {
            text.SetSpan(new UnderlineSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (format.Strikethrough != RichTextStrikethroughStyle.None)
        {
            text.SetSpan(new StrikethroughSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (!string.IsNullOrWhiteSpace(format.FontFamily))
        {
            text.SetSpan(
                new TypefaceSpan(format.FontFamily),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.FontSize is > 0)
        {
            text.SetSpan(
                new RichFontSizeSpan(format.FontSize.Value,
                    TypedValue.ApplyDimension(ComplexUnitType.Sp, (float)format.FontSize.Value,
                        PlatformView.Resources!.DisplayMetrics)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.Script == RichTextScript.Superscript)
        {
            text.SetSpan(new SuperscriptSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }
        else if (format.Script == RichTextScript.Subscript)
        {
            text.SetSpan(new SubscriptSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (format.ForegroundColor is not null && !format.Hidden)
        {
            text.SetSpan(
                new ForegroundColorSpan(format.ForegroundColor.ToPlatform()),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.BackgroundColor is not null)
        {
            text.SetSpan(
                new BackgroundColorSpan(format.BackgroundColor.ToPlatform()),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.HorizontalScale != 1d)
        {
            text.SetSpan(
                new ScaleXSpan((float)format.HorizontalScale),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.CharacterSpacing != 0)
        {
            var fontSize = ResolveFontSize(format.FontSize);
            text.SetSpan(
                new RichLetterSpacingSpan((float)(format.CharacterSpacing / fontSize)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.BaselineOffset != 0)
        {
            text.SetSpan(
                new RichBaselineOffsetSpan(ToPixels(format.BaselineOffset)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (!string.IsNullOrWhiteSpace(format.LanguageTag))
        {
            text.SetSpan(
                new LocaleSpan(Java.Util.Locale.ForLanguageTag(format.LanguageTag)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }
    }

    private void ApplyParagraphFormat(
        ISpannable text,
        int start,
        int end,
        RichTextParagraphFormat format,
        string? listMarker,
        Drawable? listPicture)
    {
        if (end <= start)
        {
            return;
        }

        text.SetSpan(
            new RichParagraphMetadataSpan(format),
            start,
            end,
            SpanTypes.Paragraph);

        var alignment = format.Alignment switch
        {
            RichTextAlignment.Center => TextAlignment.AlignCenter,
            RichTextAlignment.Right => TextAlignment.AlignOpposite,
            RichTextAlignment.Justified or RichTextAlignment.Distributed => TextAlignment.AlignNormal,
            _ => TextAlignment.AlignNormal,
        };
        if (alignment != TextAlignment.AlignNormal)
        {
            text.SetSpan(
                new AlignmentSpanStandard(alignment!),
                start,
                end,
                SpanTypes.Paragraph);
        }

        var firstMargin = ToPixels(format.LeadingIndent + format.FirstLineIndent);
        var remainingMargin = ToPixels(format.LeadingIndent);
        if (format.NativeList is null && (firstMargin != 0 || remainingMargin != 0))
        {
            ApplyParagraphMargins(text, start, end, firstMargin, remainingMargin);
        }

        if (RichLineHeightSpan.IsNeeded(format))
        {
            text.SetSpan(
                new RichLineHeightSpan(
                    start,
                    end,
                    format.LineSpacingRule,
                    format.LineSpacingRule is
                        RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly
                            ? ToPixels(format.LineSpacing)
                            : format.LineSpacing,
                    format.MinimumLineHeight is > 0 ? ToPixels(format.MinimumLineHeight.Value) : null,
                    format.MaximumLineHeight is > 0 ? ToPixels(format.MaximumLineHeight.Value) : null,
                    ToPixels(format.SpaceBefore),
                    ToPixels(format.SpaceAfter)),
                start,
                end,
                SpanTypes.Paragraph);
        }

        foreach (var tab in format.TabStops.Where(tab => tab.Alignment == RichTextTabAlignment.Left))
        {
            text.SetSpan(
                new TabStopSpanStandard(ToPixels(tab.Position)),
                start,
                end,
                SpanTypes.Paragraph);
        }

        if (format.NativeList is { } list && !string.IsNullOrEmpty(listMarker))
        {
            var markerEnd = FindSoftLineEnd(text, start, end);
            ApplyListMarkerSpan(text, start, markerEnd, format, list, listMarker, listPicture);
            if (markerEnd < end)
                ApplyParagraphMargins(text, markerEnd, end, remainingMargin, remainingMargin);
        }

        if (format.BackgroundColor is not null ||
            format.Border is { Sides: not RichTextBorderSides.None, Style: not RichTextBorderStyle.None })
        {
            var border = format.Border;
            text.SetSpan(
                new RichParagraphDecorationSpan(
                    start,
                    end,
                    format.BackgroundColor?.ToPlatform(),
                    border?.Sides ?? RichTextBorderSides.None,
                    border?.Style ?? RichTextBorderStyle.None,
                    border is null
                        ? 0
                        : Math.Max(
                            (float)(border.Width *
                                (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f)),
                            1f),
                    ResolveTextColor(border?.Color)),
                start,
                end,
                SpanTypes.Paragraph);
        }
    }

    private void ApplyListMarkerSpan(
        ISpannable text,
        int start,
        int end,
        RichTextParagraphFormat format,
        RichTextListFormat list,
        string marker,
        Drawable? picture)
    {
        if (end <= start)
        {
            return;
        }

        var markerTab = format.TabStops.FirstOrDefault(tab => tab.Alignment == RichTextTabAlignment.Left)?.Position ?? 0;
        end = FindSoftLineEnd(text, start, end);
        text.SetSpan(
            new RichListMarkerSpan(
                list,
                marker,
                picture,
                ToPixels(markerTab > 0 ? markerTab : format.LeadingIndent),
                ToPixels(format.LeadingIndent),
                ToPixels(format.LeadingIndent + format.FirstLineIndent)),
            start,
            end,
            // Keep the marker anchored when typing at the start of the item.
            SpanTypes.InclusiveExclusive);
    }

    private static int FindSoftLineEnd(Java.Lang.ICharSequence text, int start, int end)
    {
        var next = TextUtils.IndexOf(text, RichTextDocument.SoftLineBreakCharacter, start, end);
        return next < 0 ? end : next + 1;
    }

    private static void ApplyParagraphMargins(ISpannable text, int start, int end, int firstMargin, int remainingMargin)
    {
        var first = true;
        for (var segment = start; segment < end;)
        {
            var next = FindSoftLineEnd(text, segment, end);
            text.SetSpan(new LeadingMarginSpanStandard(first ? firstMargin : remainingMargin, remainingMargin), segment, next, SpanTypes.ExclusiveExclusive);
            first = false;
            segment = next;
        }
    }

    private static IEnumerable<T> GetSpans<T>(ISpanned text, int start, int end)
        where T : Java.Lang.Object
    {
        foreach (var value in text.GetSpans(
            Math.Max(start, 0),
            Math.Max(end, start),
            SpanType<T>.Value) ?? [])
        {
            if (value is T span)
            {
                yield return span;
            }
        }
    }

    private static class SpanType<T>
        where T : Java.Lang.Object
    {
        public static readonly Java.Lang.Class Value = Java.Lang.Class.FromType(typeof(T));
    }

    private static Microsoft.Maui.Graphics.Color FromAndroidColor(Android.Graphics.Color color) =>
        Microsoft.Maui.Graphics.Color.FromRgba(color.R, color.G, color.B, color.A);

    private Android.Graphics.Color ResolveTextColor(Microsoft.Maui.Graphics.Color? color)
    {
        if (color is not null)
        {
            return color.ToPlatform();
        }

        if (VirtualView.TextColor is { } textColor)
        {
            return textColor.ToPlatform();
        }

        return new Android.Graphics.Color(PlatformView.CurrentTextColor);
    }

    private double ResolveFontSize(double? fontSize)
    {
        if (fontSize is not null)
        {
            return fontSize.Value;
        }

        if (VirtualView.FontSize is { } viewFontSize)
        {
            return viewFontSize;
        }

        var density = PlatformView.Resources?.DisplayMetrics?.Density ?? 1f;
        return Math.Max(PlatformView.TextSize / density, 1f);
    }

    private static int GetParagraphStart(string text, int position) =>
        position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

    private static int GetParagraphEnd(string text, int start)
    {
        var newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }

    private int ToPixels(double value)
    {
        var pixels = Math.Round(
            value * (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f));
        return pixels switch
        {
            >= int.MaxValue => int.MaxValue,
            <= int.MinValue => int.MinValue,
            _ => (int)pixels,
        };
    }

    private double FromPixels(double value) =>
        value / (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f);
}
