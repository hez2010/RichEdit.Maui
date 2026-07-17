using System.Collections.Immutable;
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
        editor.SetPadding(ToPixels(12), ToPixels(10), ToPixels(12), ToPixels(10));
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

            foreach (var paragraph in document.Paragraphs)
            {
                var end = GetParagraphEnd(document.Text, paragraph.Start);
                ApplyParagraphFormat(builder, paragraph.Start, end, paragraph.Format);
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
        PlatformView.Hint = editor.Placeholder;
        PlatformView.SetHintTextColor(editor.PlaceholderColor.ToPlatform());
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        PlatformView.SetTextColor(editor.TextColor.ToPlatform());
        PlatformView.SetTextSize(ComplexUnitType.Sp, (float)editor.FontSize);
        PlatformView.Typeface = string.IsNullOrWhiteSpace(editor.FontFamily)
            ? Typeface.Default
            : Typeface.Create(editor.FontFamily, TypefaceStyle.Normal);

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
            var fontSize = format.FontSize ?? VirtualView.FontSize;
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
        RichTextParagraphFormat format)
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

        if (format.LineSpacingRule == RichTextLineSpacingRule.Exactly &&
            format.LineSpacing > 0 &&
            OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            text.SetSpan(
                new LineHeightSpanStandard(ToPixels(format.LineSpacing)),
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

        if (format.List is { Kind: RichListKind.Bulleted })
        {
            text.SetSpan(
                new BulletSpan(ToPixels(8)),
                start,
                end,
                SpanTypes.Paragraph);
        }

        if (format.BackgroundColor is not null)
        {
            text.SetSpan(
                new BackgroundColorSpan(format.BackgroundColor.ToPlatform()),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }
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

        var bytes = image.Data.ToArray();
        var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        if (bitmap is null)
        {
            return;
        }

        var drawable = new BitmapDrawable(PlatformView.Resources, bitmap);
        var width = image.Width > 0 ? ToPixels(image.Width) : bitmap.Width;
        var height = image.Height > 0 ? ToPixels(image.Height) : bitmap.Height;
        drawable.SetBounds(0, 0, Math.Max(width, 1), Math.Max(height, 1));
        var alignment = image.VerticalAlignment == RichTextImageVerticalAlignment.Baseline
            ? SpanAlign.Baseline
            : SpanAlign.Bottom;
        text.SetSpan(
            new ImageSpan(drawable, image.Source ?? string.Empty, alignment),
            image.Position,
            image.Position + 1,
            SpanTypes.ExclusiveExclusive);
    }

    private RichTextDocument ReadDocumentFromPlatform()
    {
        var text = PlatformView.Text ?? string.Empty;
        if (PlatformView.EditableText is not { } editable)
        {
            return RichTextDocument.FromPlainText(text);
        }

        var previous = VirtualView.Document;
        var defaultCharacterFormat = previous.DefaultCharacterFormat with
        {
            FontFamily = previous.DefaultCharacterFormat.FontFamily ?? VirtualView.FontFamily,
            FontSize = previous.DefaultCharacterFormat.FontSize ?? VirtualView.FontSize,
            ForegroundColor = previous.DefaultCharacterFormat.ForegroundColor ??
                FromAndroidColor(new Android.Graphics.Color(PlatformView.CurrentTextColor)),
        };
        var runs = new List<RichTextRun>();
        for (var position = 0; position < text.Length; position++)
        {
            var format = ReadCharacterFormat(editable, position, defaultCharacterFormat);
            if (runs.Count > 0 && runs[^1].Format == format)
            {
                runs[^1] = runs[^1] with { Length = runs[^1].Length + 1 };
            }
            else
            {
                runs.Add(new RichTextRun(position, 1, format));
            }
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
                FontSize = Math.Max((format.FontSize ?? VirtualView.FontSize) * span.SizeChange, 1),
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
                CharacterSpacing = spacing.Em * (format.FontSize ?? VirtualView.FontSize),
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

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
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

        if (GetSpans<BulletSpan>(text, start, end).Any())
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

        var document = ReadDocumentFromPlatform();
        var insertedStart = Math.Clamp(eventArgs.Start, 0, document.Text.Length);
        var insertedLength = Math.Clamp(eventArgs.AfterCount, 0, document.Text.Length - insertedStart);
        var replacedTypingFormat = insertedLength > 0 &&
            !ContainsPastedRichContent(editable, insertedStart, insertedStart + insertedLength);
        if (replacedTypingFormat)
        {
            document = document.ApplyCharacterFormat(
                insertedStart..(insertedStart + insertedLength),
                _ => _nativeTypingFormat);
        }

        var start = Math.Clamp(PlatformView.SelectionStart, 0, document.Text.Length);
        var end = Math.Clamp(PlatformView.SelectionEnd, start, document.Text.Length);
        VirtualView.UpdateDocumentFromPlatform(document, start, end - start);
        if (replacedTypingFormat)
        {
            ApplyDocumentCore(document, start, end - start);
        }
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

    private static IEnumerable<T> GetSpans<T>(ISpanned text, int start, int end)
        where T : Java.Lang.Object
    {
        foreach (var value in text.GetSpans(
                     Math.Max(start, 0),
                     Math.Max(end, start),
                     Java.Lang.Class.FromType(typeof(T))) ?? [])
        {
            if (value is T span)
            {
                yield return span;
            }
        }
    }

    private static Microsoft.Maui.Graphics.Color FromAndroidColor(Android.Graphics.Color color) =>
        Microsoft.Maui.Graphics.Color.FromRgba(color.R, color.G, color.B, color.A);

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
