using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Text;
using Windows.Storage.Streams;
using WinRT;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyCharacterFormatsIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextRange affectedRange,
        RichTextDocumentSnapshot? previousSnapshot = null)
    {
        if (snapshot.Length == 0 || affectedRange.IsEmpty)
        {
            return;
        }

        var nativeDocument = PlatformView.Document;
        // Without hidden hyperlink instructions, native and logical UTF-16 offsets match.
        var positions = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        var reset = nativeDocument.GetDefaultCharacterFormat();
        var characterFormats = new Dictionary<RichTextCharacterFormat, ITextCharacterFormat>();
        // TOM notifies every live range on formatting changes. Reuse one range so
        // thousands of token ranges do not accumulate until their wrappers are collected.
        var nativeRange = nativeDocument.GetRange(0, 0);
        var rangeFormat = nativeRange.CharacterFormat;
        foreach (var change in GetCharacterFormatChanges(snapshot, affectedRange, previousSnapshot))
        {
            var format = snapshot.ResolveCharacterFormat(change.Format);
            var previousFormat = change.PreviousFormat is { } previous ? previousSnapshot!.ResolveCharacterFormat(previous) : null;
            if (format != previousFormat)
            {
                nativeRange.SetRange(positions?.ToNativePosition(change.Range.Start) ?? change.Range.Start,
                    positions?.ToNativePosition(change.Range.End) ?? change.Range.End);
                // WinUI's live LanguageTag setter also changes TextScript, unlike SetClone.
                // Keep the existing projection semantics when the language changes.
                if (previousFormat is not null && previousFormat.LanguageTag == format.LanguageTag)
                {
                    ApplyCharacterFormat(rangeFormat, format, previousFormat, reset);
                }
                else
                {
                    if (!characterFormats.TryGetValue(format, out var nativeFormat))
                    {
                        nativeFormat = reset.GetClone();
                        ApplyCharacterFormat(nativeFormat, format);
                        characterFormats.Add(format, nativeFormat);
                    }

                    rangeFormat.SetClone(nativeFormat);
                }
            }
        }
    }

    private void ApplyParagraphFormatsIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextRange affectedRange)
    {
        var positions = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        var nativeDocument = PlatformView.Document;
        var reset = nativeDocument.GetDefaultParagraphFormat();
        for (var index = snapshot.FindParagraphIndex(affectedRange.Start);
             index < snapshot.Paragraphs.Length;
             index++)
        {
            var paragraph = snapshot.Paragraphs[index];
            if (paragraph.Range.Start > affectedRange.End)
            {
                break;
            }

            if (paragraph.Range.End < affectedRange.Start)
            {
                continue;
            }

            var end = GetParagraphEnd(snapshot.Text, paragraph.Start);
            var nativeFormat = reset.GetClone();
            ApplyParagraphFormat(nativeFormat, paragraph.Format);
            var nativeRange = nativeDocument.GetRange(
                positions?.ToNativePosition(paragraph.Start) ?? paragraph.Start,
                positions?.ToNativePosition(end) ?? end);
            nativeRange.ParagraphFormat.SetClone(nativeFormat);
        }
    }

    private void ApplyLinksIncrementally(RichTextDocumentSnapshot snapshot, RichTextRange affectedRange)
    {
        var nativeDocument = PlatformView.Document;
        foreach (var link in snapshot.Links.Where(link =>
            link.End > affectedRange.Start && link.Start < affectedRange.End))
        {
            var positions = GetNativeTextSnapshot();
            var range = nativeDocument.GetRange(
                positions.ToNativePosition(link.Start),
                positions.ToNativePosition(link.End));
            try
            {
                range.Link = ToNativeLink(link.Target);
            }
            catch (Exception exception) when (exception is ArgumentException or COMException)
            {
                // Preserve the managed link when TOM rejects its target.
            }

            _nativeTextSnapshot = null;
        }

        _hasNativeLinks = snapshot.Links.Length != 0;
    }

    private static bool RequiresRtfListProjection(
        RichTextDocumentSnapshot snapshot,
        RichTextRange affectedRange)
    {
        var paragraphRange = GetAffectedParagraphRange(affectedRange, snapshot.Text);
        return snapshot.Paragraphs.Any(paragraph =>
            (paragraphRange.IsEmpty
                ? paragraph.Start == paragraphRange.Start
                : paragraph.Start >= paragraphRange.Start &&
                    paragraph.Start < paragraphRange.End) &&
            paragraph.Format.NativeList is { } list &&
            !CanProjectListWithTom(list));
    }

    private static bool CanProjectListWithTom(RichTextListFormat list)
    {
        if (list.PictureId is not null || !string.IsNullOrEmpty(list.Prefix))
        {
            return false;
        }

        return list.Kind == RichListKind.Bulleted
            ? string.Equals(list.BulletText, "•", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(list.Suffix)
            : list.Suffix is "." or ")" or "-" or "";
    }

    private static RichTextRange GetAffectedRange(
        RichTextChangeSet changes,
        int documentLength) =>
            changes.GetAffectedRange(documentLength);

    private static RichTextRange GetAffectedParagraphRange(
        RichTextChangeSet changes,
        string text)
    {
        return GetAffectedParagraphRange(GetAffectedRange(changes, text.Length), text);
    }

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

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _nativeTypingFormat = characterFormat;
        _nativeTypingParagraphFormat = paragraphFormat;
        if (PlatformView is null || PlatformView.Document.Selection.Length != 0)
        {
            return;
        }

        var wasApplyingDocument = _applyingDocument;
        _applyingDocument = true;
        try
        {
            var nativeDocument = PlatformView.Document;
            var nativeCharacterFormat = nativeDocument.GetDefaultCharacterFormat().GetClone();
            ApplyCharacterFormat(
                nativeCharacterFormat,
                DisplaySourceSnapshot.ResolveCharacterFormat(characterFormat));
            nativeDocument.Selection.CharacterFormat.SetClone(nativeCharacterFormat);
            var nativeParagraphFormat = nativeDocument.GetDefaultParagraphFormat().GetClone();
            ApplyParagraphFormat(nativeParagraphFormat, paragraphFormat);
            nativeDocument.Selection.ParagraphFormat.SetClone(nativeParagraphFormat);
        }
        finally
        {
            PlatformView.Document.ClearUndoRedoHistory();
            _applyingDocument = wasApplyingDocument;
        }
    }

    private static void LoadRtfDocument(RichEditTextDocument nativeDocument, string rtf)
    {
        // RichEdit consumes the last paragraph mark as its mandatory story
        // terminator. Supply it separately from logical trailing paragraph breaks.
        rtf = rtf.Insert(rtf.Length - 1, @"\par");
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(Encoding.ASCII.GetBytes(rtf));
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }

        stream.Seek(0);
        nativeDocument.LoadFromStream(TextSetOptions.FormatRtf, stream);
    }

    private void ApplyCharacterFormat(
        ITextCharacterFormat native,
        RichTextCharacterFormat format,
        RichTextCharacterFormat? previous = null,
        ITextCharacterFormat? reset = null)
    {
        if (previous is null || previous.FontFamily != format.FontFamily)
        {
            if ((format.FontFamily ?? VirtualView.FontFamily ?? reset?.Name) is { } fontFamily)
                native.Name = fontFamily;
        }

        if (previous is null || previous.FontSize != format.FontSize)
        {
            if ((format.FontSize ?? VirtualView.FontSize ?? reset?.Size) is { } fontSize)
                native.Size = (float)fontSize;
        }
        if (previous?.FontWeight != format.FontWeight)
            native.Weight = format.FontWeight;
        if (previous?.Italic != format.Italic)
            native.Italic = format.Italic ? FormatEffect.On : FormatEffect.Off;
        if (previous?.Underline != format.Underline)
            native.Underline = ToNativeUnderline(format.Underline);
        if (previous?.Strikethrough != format.Strikethrough)
            native.Strikethrough = format.Strikethrough == RichTextStrikethroughStyle.None ? FormatEffect.Off : FormatEffect.On;
        if (previous is null || previous.ForegroundColor != format.ForegroundColor)
        {
            if ((format.ForegroundColor ?? ResolveTextColor()) is { } foregroundColor)
                native.ForegroundColor = ToWindowsColor(foregroundColor);
        }
        if (previous is null || previous.BackgroundColor != format.BackgroundColor)
        {
            if (format.BackgroundColor is { Alpha: > 0 } backgroundColor)
                native.BackgroundColor = ToWindowsColor(backgroundColor);
            else if (previous is not null)
                native.BackgroundColor = TextConstants.AutoColor;
        }

        if (previous?.BaselineOffset != format.BaselineOffset || previous?.Script != format.Script)
        {
            native.Position = (float)format.BaselineOffset;
            // TOM treats subscript and superscript as mutually exclusive effects.
            // Clear the opposite effect first so the requested effect is the final write.
            switch (format.Script)
            {
                case RichTextScript.Subscript:
                    native.Superscript = FormatEffect.Off;
                    native.Subscript = FormatEffect.On;
                    break;
                case RichTextScript.Superscript:
                    native.Subscript = FormatEffect.Off;
                    native.Superscript = FormatEffect.On;
                    break;
                default:
                    native.Subscript = FormatEffect.Off;
                    native.Superscript = FormatEffect.Off;
                    break;
            }
        }

        if (previous?.CharacterSpacing != format.CharacterSpacing)
            native.Spacing = (float)format.CharacterSpacing;
        if (previous?.HorizontalScale != format.HorizontalScale)
            native.FontStretch = ToNativeFontStretch(format.HorizontalScale);
        if (previous?.SmallCaps != format.SmallCaps || previous?.AllCaps != format.AllCaps)
        {
            native.SmallCaps = format.SmallCaps ? FormatEffect.On : FormatEffect.Off;
            native.AllCaps = format.AllCaps ? FormatEffect.On : FormatEffect.Off;
        }
        if (previous?.Outline != format.Outline)
            native.Outline = format.Outline ? FormatEffect.On : FormatEffect.Off;
        if (previous?.Hidden != format.Hidden)
            native.Hidden = format.Hidden ? FormatEffect.On : FormatEffect.Off;
        if (previous is null && !string.IsNullOrWhiteSpace(format.LanguageTag))
        {
            native.LanguageTag = format.LanguageTag;
        }

        if (previous?.Kerning != format.Kerning)
        {
            if (format.Kerning != RichTextFeatureMode.Automatic)
                native.Kerning = format.Kerning == RichTextFeatureMode.Enabled ? 1f : 0f;
            else if (reset is not null)
                native.Kerning = reset.Kerning;
        }
    }

    private static void ApplyParagraphFormat(
        ITextParagraphFormat native,
        RichTextParagraphFormat format)
    {
        native.Alignment = format.Alignment switch
        {
            RichTextAlignment.Center => ParagraphAlignment.Center,
            RichTextAlignment.Right => ParagraphAlignment.Right,
            RichTextAlignment.Justified or RichTextAlignment.Distributed => ParagraphAlignment.Justify,
            _ => ParagraphAlignment.Left,
        };
        native.RightToLeft = format.Direction == RichTextDirection.RightToLeft
            ? FormatEffect.On
            : FormatEffect.Off;
        native.SetIndents(
            (float)format.FirstLineIndent,
            (float)format.LeadingIndent,
            (float)format.TrailingIndent);
        native.SpaceBefore = (float)format.SpaceBefore;
        native.SpaceAfter = (float)format.SpaceAfter;
        native.SetLineSpacing(ToNativeLineSpacing(format.LineSpacingRule), (float)format.LineSpacing);
        native.ClearAllTabs();
        var tabCount = 0;
        foreach (var tab in format.TabStops)
        {
            if (!TryConvertWindowsTabPosition(tab.Position, out var position))
            {
                continue;
            }

            native.AddTab(
                position,
                ToNativeTabAlignment(tab.Alignment),
                ToNativeTabLeader(tab.Leader));
            if (++tabCount == 63)
            {
                break;
            }
        }

        if (format.NativeList is not { } list)
        {
            native.ListType = MarkerType.None;
            native.ListLevelIndex = 0;
            return;
        }

        native.ListType = list.Kind == RichListKind.Bulleted
            ? MarkerType.Bullet
            : list.NumberStyle switch
            {
                RichListNumberStyle.UpperRoman => MarkerType.UppercaseRoman,
                RichListNumberStyle.LowerRoman => MarkerType.LowercaseRoman,
                RichListNumberStyle.UpperLetter => MarkerType.UppercaseEnglishLetter,
                RichListNumberStyle.LowerLetter => MarkerType.LowercaseEnglishLetter,
                _ => MarkerType.Arabic,
            };
        native.ListStyle = list.Kind == RichListKind.Bulleted
            ? MarkerStyle.Plain
            : ToNativeListStyle(list.Suffix);
        native.ListStart = list.StartAt;
        native.ListLevelIndex = list.Level + 1;
    }

    private RichTextCharacterFormat ReadCharacterFormat(ITextCharacterFormat native) => new()
    {
        FontFamily = string.IsNullOrWhiteSpace(native.Name) ? null : native.Name,
        FontSize = native.Size > 0 ? native.Size : null,
        FontWeight = Math.Clamp(native.Weight, 1, 1000),
        Italic = native.Italic == FormatEffect.On,
        Underline = FromNativeUnderline(native.Underline),
        Strikethrough = native.Strikethrough == FormatEffect.On
            ? RichTextStrikethroughStyle.Single
            : RichTextStrikethroughStyle.None,
        ForegroundColor = FromWindowsColor(native.ForegroundColor),
        BackgroundColor = FromWindowsColor(native.BackgroundColor, transparentAsNull: true),
        Script = native.Superscript == FormatEffect.On
            ? RichTextScript.Superscript
            : native.Subscript == FormatEffect.On
                ? RichTextScript.Subscript
                : RichTextScript.Normal,
        BaselineOffset = native.Position,
        CharacterSpacing = native.Spacing,
        HorizontalScale = FromNativeFontStretch(native.FontStretch),
        SmallCaps = native.SmallCaps == FormatEffect.On,
        AllCaps = native.AllCaps == FormatEffect.On,
        Outline = native.Outline == FormatEffect.On,
        Hidden = native.Hidden == FormatEffect.On,
        LanguageTag = ReadLanguageTag(native),
        Kerning = native.Kerning > 0
            ? RichTextFeatureMode.Enabled
            : RichTextFeatureMode.Disabled,
    };

    private string? ReadLanguageTag(ITextCharacterFormat native)
    {
        if (!_canReadLanguageTag)
        {
            return null;
        }

        try
        {
            var languageTag = native.LanguageTag;
            return string.IsNullOrWhiteSpace(languageTag) ? null : languageTag;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // RichEdit can report E_FAIL for range language tags. Do not pay the
            // exception cost again for every character in the document snapshot.
            _canReadLanguageTag = false;
            return null;
        }
    }

    internal static RichTextCharacterFormat MergeWindowsCharacterFormat(
        RichTextCharacterFormat native,
        RichTextCharacterFormat previous) =>
            MergeWindowsCharacterFormat(native, previous, null, null, null);

    private static RichTextCharacterFormat MergeWindowsCharacterFormat(
        RichTextCharacterFormat native,
        RichTextCharacterFormat previous,
        string? fontFamily,
        double? fontSize,
        Color? textColor)
    {
        var horizontalScale = ToNativeFontStretch(native.HorizontalScale) ==
            ToNativeFontStretch(previous.HorizontalScale)
                ? previous.HorizontalScale
                : native.HorizontalScale;
        return native with
        {
            FontFamily = previous.FontFamily is null &&
                string.Equals(native.FontFamily, fontFamily, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : native.FontFamily,
            FontSize = previous.FontSize is null &&
                native.FontSize is { } nativeFontSize &&
                fontSize is not null &&
                Math.Abs(nativeFontSize - fontSize.Value) < 0.01d
                    ? null
                    : native.FontSize,
            ForegroundColor = previous.ForegroundColor is null &&
                native.ForegroundColor is { } nativeForeground &&
                textColor is not null &&
                // TOM stores an opaque text color; theme brushes can carry opacity.
                (ToWindowsColor(nativeForeground) with { A = 255 }).Equals(ToWindowsColor(textColor) with { A = 255 })
                    ? null
                    : native.ForegroundColor,
            UnderlineColor = previous.UnderlineColor,
            Strikethrough = native.Strikethrough != RichTextStrikethroughStyle.None &&
                previous.Strikethrough == RichTextStrikethroughStyle.Double
                    ? RichTextStrikethroughStyle.Double
                    : native.Strikethrough,
            StrikethroughColor = previous.StrikethroughColor,
            HorizontalScale = horizontalScale,
            Shadow = previous.Shadow,
            LanguageTag = native.LanguageTag ?? previous.LanguageTag,
            Direction = previous.Direction,
            Kerning = previous.Kerning == RichTextFeatureMode.Automatic
                ? RichTextFeatureMode.Automatic
                : native.Kerning,
            Ligatures = previous.Ligatures,
            Shading = previous.Shading,
            ShadingForegroundColor = previous.ShadingForegroundColor,
            ShadingBackgroundColor = previous.ShadingBackgroundColor,
            StyleName = previous.StyleName,
        };
    }

    internal static RichTextParagraphFormat MergeWindowsParagraphFormat(
        RichTextParagraphFormat native,
        RichTextParagraphFormat previous)
    {
        var alignment = native.Alignment == RichTextAlignment.Justified &&
            previous.Alignment == RichTextAlignment.Distributed
                ? RichTextAlignment.Distributed
                : native.Alignment;
        var direction = native.Direction == RichTextDirection.LeftToRight &&
            previous.Direction == RichTextDirection.Automatic
                ? RichTextDirection.Automatic
                : native.Direction;
        RichTextListFormat? list = native.NativeList;
        if (list is not null && previous.NativeList is { } previousList)
        {
            list = list with
            {
                Id = previousList.Id,
                Restart = previousList.Restart,
                Prefix = previousList.Prefix,
                Suffix = HasEquivalentWindowsListSuffix(list.Suffix, previousList.Suffix)
                    ? previousList.Suffix
                    : list.Suffix,
                BulletText = previousList.BulletText,
                PictureId = list.Kind == RichListKind.Bulleted
                    ? previousList.PictureId
                    : null,
            };
        }

        return native with
        {
            Alignment = alignment,
            Direction = direction,
            MinimumLineHeight = previous.MinimumLineHeight,
            MaximumLineHeight = previous.MaximumLineHeight,
            Hyphenation = previous.Hyphenation,
            BackgroundColor = previous.BackgroundColor,
            Shading = previous.Shading,
            ShadingForegroundColor = previous.ShadingForegroundColor,
            ShadingBackgroundColor = previous.ShadingBackgroundColor,
            Border = previous.Border,
            StyleName = previous.StyleName,
            NativeList = list,
        };
    }

    internal static bool TryConvertWindowsTabPosition(double position, out float nativePosition)
    {
        nativePosition = (float)position;
        return nativePosition > 0 && float.IsFinite(nativePosition);
    }

    private static bool HasEquivalentWindowsListSuffix(string first, string second) =>
        ToNativeListStyle(first) == ToNativeListStyle(second);

    private static MarkerStyle ToNativeListStyle(string suffix) => suffix switch
    {
        ")" => MarkerStyle.Parenthesis,
        "-" => MarkerStyle.Minus,
        "" => MarkerStyle.Plain,
        _ => MarkerStyle.Period,
    };

    private static RichTextParagraphFormat ReadParagraphFormat(
        ITextParagraphFormat native,
        RichTextListFormat? previousList)
    {
        var tabs = ImmutableArray.CreateBuilder<RichTextTabStop>(Math.Max(native.TabCount, 0));
        for (var index = 0; index < native.TabCount; index++)
        {
            native.GetTab(index, out var position, out var alignment, out var leader);
            if (position > 0 && float.IsFinite(position))
            {
                tabs.Add(new RichTextTabStop(
                    position,
                    FromNativeTabAlignment(alignment),
                    FromNativeTabLeader(leader)));
            }
        }

        RichTextListFormat? list = null;
        if (native.ListType is not (MarkerType.None or MarkerType.Undefined))
        {
            var kind = native.ListType is MarkerType.Bullet or
                MarkerType.BlackCircleWingding or MarkerType.WhiteCircleWingding
                ? RichListKind.Bulleted
                : RichListKind.Numbered;
            list = new RichTextListFormat
            {
                Id = previousList?.Id ?? 1,
                Level = Math.Max(native.ListLevelIndex - 1, 0),
                Kind = kind,
                NumberStyle = native.ListType switch
                {
                    MarkerType.UppercaseRoman => RichListNumberStyle.UpperRoman,
                    MarkerType.LowercaseRoman => RichListNumberStyle.LowerRoman,
                    MarkerType.UppercaseEnglishLetter => RichListNumberStyle.UpperLetter,
                    MarkerType.LowercaseEnglishLetter => RichListNumberStyle.LowerLetter,
                    _ => RichListNumberStyle.Arabic,
                },
                StartAt = Math.Max(native.ListStart, 1),
                Suffix = native.ListStyle switch
                {
                    MarkerStyle.Parenthesis => ")",
                    MarkerStyle.Minus => "-",
                    MarkerStyle.Plain or MarkerStyle.NoNumber => string.Empty,
                    _ => ".",
                },
            };
        }

        return new RichTextParagraphFormat
        {
            Alignment = native.Alignment switch
            {
                ParagraphAlignment.Center => RichTextAlignment.Center,
                ParagraphAlignment.Right => RichTextAlignment.Right,
                ParagraphAlignment.Justify => RichTextAlignment.Justified,
                _ => RichTextAlignment.Left,
            },
            Direction = native.RightToLeft == FormatEffect.On
                ? RichTextDirection.RightToLeft
                : RichTextDirection.LeftToRight,
            LeadingIndent = native.LeftIndent,
            TrailingIndent = native.RightIndent,
            FirstLineIndent = native.FirstLineIndent,
            SpaceBefore = Math.Max(native.SpaceBefore, 0),
            SpaceAfter = Math.Max(native.SpaceAfter, 0),
            LineSpacingRule = FromNativeLineSpacing(native.LineSpacingRule),
            LineSpacing = Math.Max(native.LineSpacing, 0),
            TabStops = tabs.DrainToImmutable(),
            NativeList = list,
        };
    }

    private static UnderlineType ToNativeUnderline(RichTextUnderlineStyle value) => value switch
    {
        RichTextUnderlineStyle.None => UnderlineType.None,
        RichTextUnderlineStyle.Words => UnderlineType.Words,
        RichTextUnderlineStyle.Double => UnderlineType.Double,
        RichTextUnderlineStyle.Dotted => UnderlineType.Dotted,
        RichTextUnderlineStyle.Dash => UnderlineType.Dash,
        RichTextUnderlineStyle.DashDot => UnderlineType.DashDot,
        RichTextUnderlineStyle.DashDotDot => UnderlineType.DashDotDot,
        RichTextUnderlineStyle.Wave => UnderlineType.Wave,
        RichTextUnderlineStyle.Thick => UnderlineType.Thick,
        RichTextUnderlineStyle.DoubleWave => UnderlineType.DoubleWave,
        RichTextUnderlineStyle.HeavyWave => UnderlineType.HeavyWave,
        RichTextUnderlineStyle.LongDash => UnderlineType.LongDash,
        _ => UnderlineType.Single,
    };

    private static RichTextUnderlineStyle FromNativeUnderline(UnderlineType value) => value switch
    {
        UnderlineType.None or UnderlineType.Undefined => RichTextUnderlineStyle.None,
        UnderlineType.Words => RichTextUnderlineStyle.Words,
        UnderlineType.Double => RichTextUnderlineStyle.Double,
        UnderlineType.Dotted or UnderlineType.ThickDotted => RichTextUnderlineStyle.Dotted,
        UnderlineType.Dash or UnderlineType.ThickDash => RichTextUnderlineStyle.Dash,
        UnderlineType.DashDot or UnderlineType.ThickDashDot => RichTextUnderlineStyle.DashDot,
        UnderlineType.DashDotDot or UnderlineType.ThickDashDotDot => RichTextUnderlineStyle.DashDotDot,
        UnderlineType.Wave => RichTextUnderlineStyle.Wave,
        UnderlineType.Thick => RichTextUnderlineStyle.Thick,
        UnderlineType.DoubleWave => RichTextUnderlineStyle.DoubleWave,
        UnderlineType.HeavyWave => RichTextUnderlineStyle.HeavyWave,
        UnderlineType.LongDash or UnderlineType.ThickLongDash => RichTextUnderlineStyle.LongDash,
        _ => RichTextUnderlineStyle.Single,
    };

    private static LineSpacingRule ToNativeLineSpacing(RichTextLineSpacingRule value) => value switch
    {
        RichTextLineSpacingRule.OneAndHalf => LineSpacingRule.OneAndHalf,
        RichTextLineSpacingRule.Double => LineSpacingRule.Double,
        RichTextLineSpacingRule.AtLeast => LineSpacingRule.AtLeast,
        RichTextLineSpacingRule.Exactly => LineSpacingRule.Exactly,
        RichTextLineSpacingRule.Multiple => LineSpacingRule.Multiple,
        _ => LineSpacingRule.Single,
    };

    private static RichTextLineSpacingRule FromNativeLineSpacing(LineSpacingRule value) => value switch
    {
        LineSpacingRule.OneAndHalf => RichTextLineSpacingRule.OneAndHalf,
        LineSpacingRule.Double => RichTextLineSpacingRule.Double,
        LineSpacingRule.AtLeast => RichTextLineSpacingRule.AtLeast,
        LineSpacingRule.Exactly => RichTextLineSpacingRule.Exactly,
        LineSpacingRule.Multiple or LineSpacingRule.Percent => RichTextLineSpacingRule.Multiple,
        LineSpacingRule.Single => RichTextLineSpacingRule.Single,
        _ => RichTextLineSpacingRule.Automatic,
    };

    // Microsoft.UI.Text.ITextCharacterFormat exposes this Windows SDK value type;
    // WinAppSDK 1.8 does not define a Microsoft.UI.Text.FontStretch replacement.
    private static Windows.UI.Text.FontStretch ToNativeFontStretch(double scale) => scale switch
    {
        < 0.5625d => Windows.UI.Text.FontStretch.UltraCondensed,
        < 0.6875d => Windows.UI.Text.FontStretch.ExtraCondensed,
        < 0.8125d => Windows.UI.Text.FontStretch.Condensed,
        < 0.9375d => Windows.UI.Text.FontStretch.SemiCondensed,
        < 1.0625d => Windows.UI.Text.FontStretch.Normal,
        < 1.1875d => Windows.UI.Text.FontStretch.SemiExpanded,
        < 1.375d => Windows.UI.Text.FontStretch.Expanded,
        < 1.75d => Windows.UI.Text.FontStretch.ExtraExpanded,
        _ => Windows.UI.Text.FontStretch.UltraExpanded,
    };

    private static double FromNativeFontStretch(Windows.UI.Text.FontStretch stretch) => stretch switch
    {
        Windows.UI.Text.FontStretch.UltraCondensed => 0.5d,
        Windows.UI.Text.FontStretch.ExtraCondensed => 0.625d,
        Windows.UI.Text.FontStretch.Condensed => 0.75d,
        Windows.UI.Text.FontStretch.SemiCondensed => 0.875d,
        Windows.UI.Text.FontStretch.SemiExpanded => 1.125d,
        Windows.UI.Text.FontStretch.Expanded => 1.25d,
        Windows.UI.Text.FontStretch.ExtraExpanded => 1.5d,
        Windows.UI.Text.FontStretch.UltraExpanded => 2d,
        _ => 1d,
    };

    private static TabAlignment ToNativeTabAlignment(RichTextTabAlignment value) => value switch
    {
        RichTextTabAlignment.Center => TabAlignment.Center,
        RichTextTabAlignment.Right => TabAlignment.Right,
        RichTextTabAlignment.Decimal => TabAlignment.Decimal,
        _ => TabAlignment.Left,
    };

    private static RichTextTabAlignment FromNativeTabAlignment(TabAlignment value) => value switch
    {
        TabAlignment.Center => RichTextTabAlignment.Center,
        TabAlignment.Right => RichTextTabAlignment.Right,
        TabAlignment.Decimal => RichTextTabAlignment.Decimal,
        _ => RichTextTabAlignment.Left,
    };

    private static TabLeader ToNativeTabLeader(RichTextTabLeader value) => value switch
    {
        RichTextTabLeader.Dots => TabLeader.Dots,
        RichTextTabLeader.Hyphens => TabLeader.Dashes,
        RichTextTabLeader.Underline => TabLeader.Lines,
        RichTextTabLeader.ThickLine => TabLeader.ThickLines,
        RichTextTabLeader.Equals => TabLeader.Equals,
        _ => TabLeader.Spaces,
    };

    private static RichTextTabLeader FromNativeTabLeader(TabLeader value) => value switch
    {
        TabLeader.Dots => RichTextTabLeader.Dots,
        TabLeader.Dashes => RichTextTabLeader.Hyphens,
        TabLeader.Lines => RichTextTabLeader.Underline,
        TabLeader.ThickLines => RichTextTabLeader.ThickLine,
        TabLeader.Equals => RichTextTabLeader.Equals,
        _ => RichTextTabLeader.None,
    };

    // WinUI 3 text-format color properties likewise use the Windows SDK Color value type.
    private static Windows.UI.Color ToWindowsColor(Color color) => Windows.UI.Color.FromArgb(
        (byte)Math.Round(color.Alpha * byte.MaxValue),
        (byte)Math.Round(color.Red * byte.MaxValue),
        (byte)Math.Round(color.Green * byte.MaxValue),
        (byte)Math.Round(color.Blue * byte.MaxValue));

    private static Color? FromWindowsColor(
        Windows.UI.Color color,
        bool transparentAsNull = false) =>
            transparentAsNull && color.A == 0
                ? null
                : Color.FromRgba(color.R, color.G, color.B, color.A);

    [DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Media.SolidColorBrush))]
    private Color? ResolveTextColor()
    {
        if (VirtualView.TextColor is { } textColor)
        {
            return textColor;
        }

        if (PlatformView.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush foreground)
        {
            return FromWindowsColor(foreground.Color);
        }

        return FromWindowsColor(PlatformView.Document.GetDefaultCharacterFormat().ForegroundColor);
    }

    private string? ResolveFontFamily()
    {
        if (!string.IsNullOrWhiteSpace(VirtualView.FontFamily))
        {
            return VirtualView.FontFamily;
        }

        return PlatformView.FontFamily?.Source;
    }

    private double? ResolveFontSize()
    {
        if (VirtualView.FontSize is { } fontSize)
        {
            return fontSize;
        }

        return double.IsFinite(PlatformView.FontSize) && PlatformView.FontSize > 0
            ? PlatformView.FontSize
            : null;
    }

    private static int GetParagraphEnd(string text, int start)
    {
        var newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }
}
