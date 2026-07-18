using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;
using TextAlignment = Android.Text.Layout.Alignment;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private bool _applyingDocument;
    private ColorStateList? _defaultHintTextColors;
    private ColorStateList? _defaultTextColors;
    private float _defaultTextSize;
    private Typeface? _defaultTypeface;
    private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
    private IKeyListener? _editableKeyListener;

    protected override RichEditText CreatePlatformView()
    {
        var editor = new RichEditText(MauiContext!.Context!)
        {
            Gravity = GravityFlags.Top | GravityFlags.Start,
            InputType = InputTypes.ClassText |
                        InputTypes.TextFlagMultiLine |
                        InputTypes.TextFlagCapSentences |
                        InputTypes.TextFlagAutoCorrect,
            OverScrollMode = OverScrollMode.Always,
            VerticalScrollBarEnabled = true,
        };

        editor.SetSingleLine(false);
        editor.SetHorizontallyScrolling(false);
        _defaultHintTextColors = editor.HintTextColors;
        _defaultTextColors = editor.TextColors;
        _defaultTextSize = editor.TextSize;
        _defaultTypeface = editor.Typeface;
        var density = editor.Resources?.DisplayMetrics?.Density ?? 1f;
        editor.SetPadding(
            checked((int)Math.Round(12 * density)),
            checked((int)Math.Round(10 * density)),
            checked((int)Math.Round(12 * density)),
            checked((int)Math.Round(10 * density)));
        _editableKeyListener = editor.KeyListener;
        return editor;
    }

    protected override void ConnectHandler(RichEditText platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextChanged += OnNativeDocumentChanged;
        platformView.NativeSelectionChanged += OnNativeSelectionChanged;
    }

    protected override void DisconnectHandler(RichEditText platformView)
    {
        platformView.TextChanged -= OnNativeDocumentChanged;
        platformView.NativeSelectionChanged -= OnNativeSelectionChanged;
        base.DisconnectHandler(platformView);
    }

    private partial void ApplyDocumentCore(
        RichTextDocument document,
        int selectionStart,
        int selectionLength)
    {
        if (PlatformView is null)
        {
            return;
        }

        _applyingDocument = true;
        try
        {
            var builder = new SpannableStringBuilder(document.Text);
            foreach (var run in document.Runs)
            {
                ApplyCharacterFormat(builder, run.Start, run.End, run.Format);
            }

            var listPictures = new Dictionary<string, Drawable?>(StringComparer.Ordinal);

            var listNumbers = new Dictionary<(int Id, int Level), int>();
            var allJustified = true;
            var allHyphenated = true;
            var allLeftToRight = true;
            var allRightToLeft = true;
            foreach (var paragraph in document.Paragraphs)
            {
                allJustified &= paragraph.Format.Alignment is
                    RichTextAlignment.Justified or RichTextAlignment.Distributed;
                allHyphenated &= paragraph.Format.Hyphenation;
                allLeftToRight &= paragraph.Format.Direction == RichTextDirection.LeftToRight;
                allRightToLeft &= paragraph.Format.Direction == RichTextDirection.RightToLeft;
                var end = GetParagraphEnd(document.Text, paragraph.Start);
                string? listMarker = null;
                if (paragraph.Format.List is { } list)
                {
                    if (list.Kind == RichListKind.Bulleted)
                    {
                        listMarker = list.BulletText;
                    }
                    else
                    {
                        var key = (list.Id, list.Level);
                        if (list.Restart || !listNumbers.TryGetValue(key, out var number))
                        {
                            number = list.StartAt;
                        }

                        listMarker = RichTextListFormatter.FormatMarker(list, number);
                        listNumbers[key] = number == int.MaxValue ? number : number + 1;
                    }
                }

                Drawable? listPicture = null;
                if (paragraph.Format.List?.PictureId is { } pictureId &&
                    !listPictures.TryGetValue(pictureId, out listPicture))
                {
                    var picture = document.ListPictures[pictureId];
                    listPicture = CreateBitmapDrawable(
                        picture.Data,
                        picture.Width,
                        picture.Height);
                    listPictures.Add(pictureId, listPicture);
                }

                ApplyParagraphFormat(
                    builder,
                    paragraph.Start,
                    end,
                    paragraph.Format,
                    listMarker,
                    listPicture);
            }

            foreach (var link in document.Links)
            {
                builder.SetSpan(
                    new URLSpan(link.Target),
                    link.Start,
                    link.End,
                    SpanTypes.ExclusiveExclusive);
            }

            foreach (var image in document.Images)
            {
                ApplyImage(builder, image);
            }

            PlatformView.JustificationMode = allJustified
                ? JustificationMode.InterWord
                : JustificationMode.None;
            PlatformView.HyphenationFrequency = allHyphenated
                ? global::Android.Text.HyphenationFrequency.Normal
                : global::Android.Text.HyphenationFrequency.None;
            PlatformView.TextDirection = allRightToLeft
                ? TextDirection.Rtl
                : allLeftToRight
                    ? TextDirection.Ltr
                    : TextDirection.FirstStrong;
            PlatformView.SetText(builder, TextView.BufferType.Spannable);
            SetSelectionCore(selectionStart, selectionLength);
        }
        finally
        {
            _applyingDocument = false;
        }
    }

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _nativeTypingFormat = characterFormat;
        _nativeTypingParagraphFormat = paragraphFormat;
    }

    private partial void SetSelectionCore(int start, int length)
    {
        if (PlatformView is null)
        {
            return;
        }

        var textLength = PlatformView.Text?.Length ?? 0;
        start = Math.Clamp(start, 0, textLength);
        length = Math.Clamp(length, 0, textLength - start);
        PlatformView.SetSelection(start, start + length);
    }

    private partial void UpdatePlaceholder(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.PlaceholderProperty))
        {
            PlatformView.Hint = editor.Placeholder;
        }

        if (editor.IsSet(RichEditor.PlaceholderColorProperty))
        {
            if (editor.PlaceholderColor is { } placeholderColor)
            {
                PlatformView.SetHintTextColor(placeholderColor.ToPlatform());
            }
            else if (_defaultHintTextColors is not null)
            {
                PlatformView.SetHintTextColor(_defaultHintTextColors);
            }
        }
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.TextColorProperty) && editor.TextColor is { } textColor)
        {
            PlatformView.SetTextColor(textColor.ToPlatform());
        }
        else if (editor.IsSet(RichEditor.TextColorProperty) && _defaultTextColors is not null)
        {
            PlatformView.SetTextColor(_defaultTextColors);
        }

        if (editor.IsSet(RichEditor.FontSizeProperty))
        {
            if (editor.FontSize is { } fontSize)
            {
                PlatformView.SetTextSize(ComplexUnitType.Sp, (float)fontSize);
            }
            else
            {
                PlatformView.SetTextSize(ComplexUnitType.Px, _defaultTextSize);
            }
        }

        if (editor.IsSet(RichEditor.FontFamilyProperty))
        {
            PlatformView.Typeface = string.IsNullOrWhiteSpace(editor.FontFamily)
                ? _defaultTypeface
                : Typeface.Create(editor.FontFamily, TypefaceStyle.Normal);
        }

        if (!_applyingDocument)
        {
            ApplyDocumentCore(editor.Document, editor.SelectionStart, editor.SelectionLength);
            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
    }

    private partial void UpdateIsReadOnly(RichEditor editor)
    {
        if (_editableKeyListener is null && PlatformView.KeyListener is not null)
        {
            _editableKeyListener = PlatformView.KeyListener;
        }

        PlatformView.KeyListener = editor.IsReadOnly ? null : _editableKeyListener;
        PlatformView.SetTextIsSelectable(editor.IsReadOnly);
        PlatformView.SetCursorVisible(!editor.IsReadOnly);
    }

    private void ApplyCharacterFormat(
        ISpannable text,
        int start,
        int end,
        RichTextCharacterFormat format)
    {
        if (end <= start)
        {
            return;
        }

        text.SetSpan(
            new RichCharacterMetadataSpan(format),
            start,
            end,
            SpanTypes.ExclusiveExclusive);

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
                new AbsoluteSizeSpan(checked((int)Math.Round(format.FontSize.Value)), true),
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

        if (format.ForegroundColor is not null)
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
        if (firstMargin != 0 || remainingMargin != 0)
        {
            text.SetSpan(
                new LeadingMarginSpanStandard(firstMargin, remainingMargin),
                start,
                end,
                SpanTypes.Paragraph);
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

        if (format.List is { } list && !string.IsNullOrEmpty(listMarker))
        {
            ApplyListMarkerSpan(text, start, end, list, listMarker, listPicture);
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
        RichTextListFormat list,
        string marker,
        Drawable? picture)
    {
        if (end <= start)
        {
            return;
        }

        var markerWidth = picture?.Bounds.Width() ??
            (PlatformView is { Paint: { } paint }
                ? checked((int)Math.Ceiling(paint.MeasureText(marker)))
                : 16);
        text.SetSpan(
            new RichListMarkerSpan(
                list,
                marker,
                picture,
                markerWidth,
                ToPixels(8),
                ToPixels(18 * list.Level)),
            start,
            end,
            SpanTypes.Paragraph);
    }

    private void ApplyImage(ISpannable text, RichTextImage image)
    {
        if (image.Position < 0 || image.Position >= text.Length())
        {
            return;
        }

        text.SetSpan(
            new RichImageMetadataSpan(image),
            image.Position,
            image.Position + 1,
            SpanTypes.ExclusiveExclusive);
        if (image.Data.IsDefaultOrEmpty)
        {
            return;
        }

        var drawable = CreateBitmapDrawable(image.Data, image.Width, image.Height);
        if (drawable is null)
        {
            return;
        }

        var alignment = image.VerticalAlignment == RichTextImageVerticalAlignment.Baseline
            ? SpanAlign.Baseline
            : SpanAlign.Bottom;
        var imageSpan = new ImageSpan(drawable, image.Source ?? string.Empty, alignment);
        if (!string.IsNullOrEmpty(image.AlternativeText) &&
            OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            imageSpan.ContentDescription = image.AlternativeText;
        }

        text.SetSpan(
            imageSpan,
            image.Position,
            image.Position + 1,
            SpanTypes.ExclusiveExclusive);
    }

    private BitmapDrawable? CreateBitmapDrawable(
        ImmutableArray<byte> data,
        double width,
        double height)
    {
        if (data.IsDefaultOrEmpty)
        {
            return null;
        }

        var bytes = ImmutableCollectionsMarshal.AsArray(data);
        if (bytes is null)
        {
            return null;
        }

        var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        if (bitmap is null)
        {
            return null;
        }

        var drawable = new BitmapDrawable(PlatformView.Resources, bitmap);
        var pixelWidth = width > 0 ? ToPixels(width) : bitmap.Width;
        var pixelHeight = height > 0 ? ToPixels(height) : bitmap.Height;
        drawable.SetBounds(0, 0, Math.Max(pixelWidth, 1), Math.Max(pixelHeight, 1));
        return drawable;
    }

    private RichTextDocument ReadDocumentFromPlatform(string text)
    {
        if (PlatformView.EditableText is not { } editable)
        {
            return RichTextDocument.FromPlainText(text);
        }

        var previous = VirtualView.Document;
        var defaultCharacterFormat = previous.DefaultCharacterFormat;
        var runs = new List<RichTextRun>();
        for (var position = 0; position < text.Length;)
        {
            var end = editable.NextSpanTransition(
                position,
                text.Length,
                SpanType<CharacterStyle>.Value);
            if (end <= position)
            {
                end = position + 1;
            }

            var format = ReadCharacterFormat(editable, position, defaultCharacterFormat);
            if (runs.Count > 0 && runs[^1].Format == format)
            {
                runs[^1] = runs[^1] with { Length = runs[^1].Length + end - position };
            }
            else
            {
                runs.Add(new RichTextRun(position, end - position, format));
            }

            position = end;
        }

        var paragraphs = new List<RichTextParagraph>();
        RichTextListFormat? previousList = null;
        var nextListId = 1;
        for (var start = 0; ;)
        {
            var end = GetParagraphEnd(text, start);
            var format = ReadParagraphFormat(
                editable,
                start,
                end,
                previous.DefaultParagraphFormat);
            if (format.List is { } list)
            {
                if (list.Id <= 0)
                {
                    var continues = previousList is not null &&
                        previousList.Kind == list.Kind &&
                        previousList.Level == list.Level;
                    list = list with { Id = continues ? previousList!.Id : nextListId++ };
                    format = format with { List = list };
                }

                previousList = list;
            }
            else
            {
                previousList = null;
            }

            paragraphs.Add(new RichTextParagraph(start, format));
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }

        var links = ReadLinks(editable, text.Length);
        var images = ReadImages(editable, text);
        return previous.MergeNativeSnapshot(
            text,
            runs,
            paragraphs,
            links,
            images,
            defaultCharacterFormat,
            previous.DefaultParagraphFormat);
    }

    private RichTextCharacterFormat ReadCharacterFormat(
        ISpanned text,
        int position,
        RichTextCharacterFormat defaultFormat)
    {
        var format = GetSpans<RichCharacterMetadataSpan>(text, position, position + 1)
            .LastOrDefault()?.Format ?? defaultFormat;

        foreach (var span in GetSpans<StyleSpan>(text, position, position + 1))
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

        if (GetSpans<StrikethroughSpan>(text, position, position + 1).Any())
        {
            format = format with
            {
                Strikethrough = format.Strikethrough == RichTextStrikethroughStyle.None
                    ? RichTextStrikethroughStyle.Single
                    : format.Strikethrough,
            };
        }

        var family = GetSpans<TypefaceSpan>(text, position, position + 1)
            .Select(span => span.Family)
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (family is not null)
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
        if (foreground is not null)
        {
            format = format with
            {
                ForegroundColor = FromAndroidColor(
                    new Android.Graphics.Color(foreground.ForegroundColor)),
            };
        }

        var background = GetSpans<BackgroundColorSpan>(text, position, position + 1).LastOrDefault();
        if (background is not null)
        {
            format = format with
            {
                BackgroundColor = FromAndroidColor(
                    new Android.Graphics.Color(background.BackgroundColor)),
            };
        }

        var scale = GetSpans<ScaleXSpan>(text, position, position + 1).LastOrDefault();
        if (scale is not null && scale.ScaleX > 0)
        {
            format = format with { HorizontalScale = scale.ScaleX };
        }

        var spacing = GetSpans<RichLetterSpacingSpan>(text, position, position + 1).LastOrDefault();
        if (spacing is not null)
        {
            format = format with
            {
                CharacterSpacing = spacing.Em * ResolveFontSize(format.FontSize),
            };
        }

        var baseline = GetSpans<RichBaselineOffsetSpan>(text, position, position + 1).LastOrDefault();
        if (baseline is not null)
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
        var format = GetSpans<RichParagraphMetadataSpan>(text, start, end)
            .LastOrDefault()?.Format ?? defaultFormat;
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

        var margin = GetSpans<LeadingMarginSpanStandard>(text, start, end).LastOrDefault();
        if (margin is not null)
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
        if (richLineHeight is not null)
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
        else if (OperatingSystem.IsAndroidVersionAtLeast(29))
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
        if (!tabs.IsDefaultOrEmpty)
        {
            format = format with { TabStops = tabs };
        }

        var listMarker = GetSpans<RichListMarkerSpan>(text, start, end).LastOrDefault();
        if (listMarker is not null)
        {
            format = format with { List = listMarker.ListFormat };
        }
        else if (GetSpans<BulletSpan>(text, start, end).Any())
        {
            format = format with
            {
                List = (format.List ?? new RichTextListFormat { Id = 0 }) with
                {
                    Kind = RichListKind.Bulleted,
                },
            };
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

    private void OnNativeDocumentChanged(object? sender, Android.Text.TextChangedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null || PlatformView?.EditableText is not { } editable)
        {
            return;
        }

        var previousDocument = VirtualView.Document;
        var previousText = previousDocument.Text;
        var removedStart = Math.Clamp(eventArgs.Start, 0, previousText.Length);
        var removedLength = Math.Clamp(
            eventArgs.BeforeCount,
            0,
            previousText.Length - removedStart);
        var nativeText = PlatformView.Text ?? string.Empty;
        var insertedStart = Math.Clamp(eventArgs.Start, 0, nativeText.Length);
        var insertedLength = Math.Clamp(eventArgs.AfterCount, 0, nativeText.Length - insertedStart);
        if (IsEmptyListParagraphBreak(
                previousDocument,
                nativeText,
                removedStart,
                removedLength,
                insertedStart,
                insertedLength))
        {
            ExitEmptyListParagraph(editable, previousDocument, removedStart);
            return;
        }

        var paragraphStructureChanged = previousText
            .AsSpan(removedStart, removedLength)
            .Contains('\n');

        var document = ReadDocumentFromPlatform(nativeText);
        insertedStart = Math.Clamp(insertedStart, 0, document.Text.Length);
        insertedLength = Math.Clamp(insertedLength, 0, document.Text.Length - insertedStart);
        paragraphStructureChanged |= document.Text
            .AsSpan(insertedStart, insertedLength)
            .Contains('\n');
        var containsPastedRichContent = insertedLength > 0 &&
            ContainsPastedRichContent(editable, insertedStart, insertedStart + insertedLength);
        var containsPastedListContent = insertedLength > 0 &&
            ContainsPastedListContent(editable, insertedStart, insertedStart + insertedLength);
        var replacedTypingFormat = insertedLength > 0 &&
            !containsPastedRichContent &&
            document.GetUniformCharacterFormat(
                insertedStart..(insertedStart + insertedLength)) != _nativeTypingFormat;
        if (replacedTypingFormat)
        {
            document = document.ApplyCharacterFormat(
                insertedStart..(insertedStart + insertedLength),
                _ => _nativeTypingFormat);
        }

        var touchesList = TouchesList(
            previousDocument,
            document,
            removedStart,
            removedLength,
            insertedStart,
            insertedLength);
        if (touchesList && !containsPastedListContent)
        {
            var expected = previousDocument.Replace(
                removedStart..(removedStart + removedLength),
                document.Text.Substring(insertedStart, insertedLength),
                replacedTypingFormat ? _nativeTypingFormat : null);
            if (string.Equals(expected.Text, document.Text, StringComparison.Ordinal))
            {
                document = document.With(paragraphs: document.Paragraphs.Select(paragraph =>
                    paragraph with
                    {
                        Format = paragraph.Format with
                        {
                            List = expected.GetParagraphFormat(paragraph.Start).List,
                        },
                    }));
            }
        }

        var repairListMarkers = touchesList &&
            (paragraphStructureChanged ||
             insertedLength > 0 &&
             removedStart == GetParagraphStart(previousText, removedStart));

        var start = Math.Clamp(PlatformView.SelectionStart, 0, document.Text.Length);
        var end = Math.Clamp(PlatformView.SelectionEnd, start, document.Text.Length);
        VirtualView.UpdateDocumentFromPlatform(document, start, end - start);
        _nativeTypingFormat = VirtualView.TypingCharacterFormat;
        _nativeTypingParagraphFormat = VirtualView.TypingParagraphFormat;
        if (repairListMarkers)
        {
            RebuildListMarkerSpans(editable, document);
        }
        else if (replacedTypingFormat && !paragraphStructureChanged)
        {
            ApplyDocumentCore(document, start, end - start);
        }
    }

    private static bool IsEmptyListParagraphBreak(
        RichTextDocument previous,
        string currentText,
        int removedStart,
        int removedLength,
        int insertedStart,
        int insertedLength)
    {
        if (removedLength != 0 || insertedLength != 1 || insertedStart != removedStart ||
            currentText[insertedStart] != '\n')
        {
            return false;
        }

        var paragraphStart = GetParagraphStart(previous.Text, removedStart);
        if (removedStart != paragraphStart ||
            previous.GetParagraphFormat(paragraphStart).List is null)
        {
            return false;
        }

        var paragraphEnd = GetParagraphEnd(previous.Text, paragraphStart);
        if (paragraphEnd > paragraphStart && previous.Text[paragraphEnd - 1] == '\n')
        {
            paragraphEnd--;
        }

        return paragraphEnd == paragraphStart;
    }

    private void ExitEmptyListParagraph(
        IEditable text,
        RichTextDocument previous,
        int paragraphStart)
    {
        var document = previous.ApplyParagraphFormat(
            paragraphStart..paragraphStart,
            format => format with { List = null });
        var paragraphEnd = GetParagraphEnd(document.Text, paragraphStart);

        _applyingDocument = true;
        try
        {
            text.Delete(paragraphStart, paragraphStart + 1);
            foreach (var span in GetSpans<RichListMarkerSpan>(text, paragraphStart, paragraphEnd)
                         .Where(span => text.GetSpanStart(span) == paragraphStart)
                         .ToArray())
            {
                text.RemoveSpan(span);
            }

            foreach (var span in GetSpans<BulletSpan>(text, paragraphStart, paragraphEnd)
                         .Where(span => text.GetSpanStart(span) == paragraphStart)
                         .ToArray())
            {
                text.RemoveSpan(span);
            }

            foreach (var span in GetSpans<RichParagraphMetadataSpan>(text, paragraphStart, paragraphEnd)
                         .Where(span => text.GetSpanStart(span) == paragraphStart)
                         .ToArray())
            {
                text.RemoveSpan(span);
            }

            if (paragraphEnd > paragraphStart)
            {
                text.SetSpan(
                    new RichParagraphMetadataSpan(document.GetParagraphFormat(paragraphStart)),
                    paragraphStart,
                    paragraphEnd,
                    SpanTypes.Paragraph);
            }

            PlatformView.SetSelection(paragraphStart);
            PlatformView.RequestLayout();
            PlatformView.Invalidate();
        }
        finally
        {
            _applyingDocument = false;
        }

        VirtualView.UpdateDocumentFromPlatform(document, paragraphStart, 0);
        _nativeTypingFormat = VirtualView.TypingCharacterFormat;
        _nativeTypingParagraphFormat = VirtualView.TypingParagraphFormat;
    }

    private void OnNativeSelectionChanged(object? sender, NativeSelectionChangedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var start = Math.Clamp(
            Math.Min(eventArgs.Start, eventArgs.End),
            0,
            VirtualView.Document.Text.Length);
        var end = Math.Clamp(
            Math.Max(eventArgs.Start, eventArgs.End),
            start,
            VirtualView.Document.Text.Length);
        VirtualView.UpdateSelectionFromPlatform(start, end - start);
        _nativeTypingFormat = VirtualView.TypingCharacterFormat;
        _nativeTypingParagraphFormat = VirtualView.TypingParagraphFormat;
    }

    private static bool ContainsPastedRichContent(ISpanned text, int start, int end)
    {
        if (end <= start)
        {
            return false;
        }

        return GetSpans<CharacterStyle>(text, start, end)
            .Where(span => span is not RichCharacterMetadataSpan)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end);
    }

    private static bool ContainsPastedListContent(ISpanned text, int start, int end) =>
        GetSpans<RichListMarkerSpan>(text, start, end)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end) ||
        GetSpans<BulletSpan>(text, start, end)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end);

    private void RebuildListMarkerSpans(ISpannable text, RichTextDocument document)
    {
        _applyingDocument = true;
        try
        {
            foreach (var span in GetSpans<RichListMarkerSpan>(text, 0, text.Length()).ToArray())
            {
                text.RemoveSpan(span);
            }

            foreach (var span in GetSpans<BulletSpan>(text, 0, text.Length()).ToArray())
            {
                text.RemoveSpan(span);
            }

            var listPictures = new Dictionary<string, Drawable?>(StringComparer.Ordinal);
            var listNumbers = new Dictionary<(int Id, int Level), int>();
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.Format.List is not { } list)
                {
                    continue;
                }

                string marker;
                if (list.Kind == RichListKind.Bulleted)
                {
                    marker = list.BulletText;
                }
                else
                {
                    var key = (list.Id, list.Level);
                    if (list.Restart || !listNumbers.TryGetValue(key, out var number))
                    {
                        number = list.StartAt;
                    }

                    marker = RichTextListFormatter.FormatMarker(list, number);
                    listNumbers[key] = number == int.MaxValue ? number : number + 1;
                }

                Drawable? pictureDrawable = null;
                if (list.PictureId is { } pictureId &&
                    !listPictures.TryGetValue(pictureId, out pictureDrawable) &&
                    document.ListPictures.TryGetValue(pictureId, out var picture))
                {
                    pictureDrawable = CreateBitmapDrawable(
                        picture.Data,
                        picture.Width,
                        picture.Height);
                    listPictures.Add(pictureId, pictureDrawable);
                }

                ApplyListMarkerSpan(
                    text,
                    paragraph.Start,
                    GetParagraphEnd(document.Text, paragraph.Start),
                    list,
                    marker,
                    pictureDrawable);
            }

            PlatformView.RequestLayout();
            PlatformView.Invalidate();
        }
        finally
        {
            _applyingDocument = false;
        }
    }

    private static bool TouchesList(
        RichTextDocument previous,
        RichTextDocument current,
        int removedStart,
        int removedLength,
        int insertedStart,
        int insertedLength)
    {
        var previousParagraphStart = GetParagraphStart(previous.Text, removedStart);
        var removedEnd = removedStart + removedLength;
        var currentParagraphStart = GetParagraphStart(current.Text, insertedStart);
        var insertedEnd = insertedStart + insertedLength;
        return previous.Paragraphs.Any(paragraph =>
                paragraph.Start >= previousParagraphStart &&
                paragraph.Start <= removedEnd &&
                paragraph.Format.List is not null) ||
            current.Paragraphs.Any(paragraph =>
                paragraph.Start >= currentParagraphStart &&
                paragraph.Start <= insertedEnd &&
                paragraph.Format.List is not null);
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

    private int ToPixels(double value) =>
        checked((int)Math.Round(value * (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f)));

    private double FromPixels(double value) =>
        value / (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f);
}
