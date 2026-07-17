#if IOS || MACCATALYST
using System.Collections.Immutable;
using System.Runtime.Versioning;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace RichEdit.Maui.Platforms.Apple
{
    public class RichTextView : UITextView
    {
        private readonly UILabel _placeholderLabel;

        public RichTextView()
        {
            _placeholderLabel = new UILabel
            {
                BackgroundColor = UIColor.Clear,
                Lines = 0,
                UserInteractionEnabled = false,
            };

            AddSubview(_placeholderLabel);
            TextContainerInset = new UIEdgeInsets(10, 8, 10, 8);
        }

        public void SetPlaceholder(string? text, UIColor color, UIFont font)
        {
            _placeholderLabel.Text = text;
            _placeholderLabel.TextColor = color;
            _placeholderLabel.Font = font;
            UpdatePlaceholderVisibility();
            SetNeedsLayout();
        }

        public void UpdatePlaceholderVisibility() =>
            _placeholderLabel.Hidden = !string.IsNullOrEmpty(Text);

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            var inset = TextContainerInset;
            var x = inset.Left + TextContainer.LineFragmentPadding;
            var width = Math.Max(0, Bounds.Width - x - inset.Right - TextContainer.LineFragmentPadding);
            var size = _placeholderLabel.SizeThatFits(new CGSize(width, nfloat.MaxValue));
            _placeholderLabel.Frame = new CGRect(x, inset.Top, width, size.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _placeholderLabel.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

namespace RichEdit.Maui
{
    using RichEdit.Maui.Platforms.Apple;

    public partial class RichEditorHandler
    {
        private static readonly NSString CharacterMetadataKey =
            new("RichEdit.Maui.CharacterFormat");
        private static readonly NSString ParagraphMetadataKey =
            new("RichEdit.Maui.ParagraphFormat");
        private static readonly NSString ImageMetadataKey =
            new("RichEdit.Maui.Image");

        private bool _applyingDocument;
        private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
        private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
        private RichTextViewDelegate? _textViewDelegate;

        protected override RichTextView CreatePlatformView() => new()
        {
            AllowsEditingTextAttributes = true,
            AutocorrectionType = UITextAutocorrectionType.Yes,
            Editable = true,
            ScrollEnabled = true,
            SpellCheckingType = UITextSpellCheckingType.Yes,
        };

        protected override void ConnectHandler(RichTextView platformView)
        {
            base.ConnectHandler(platformView);
            _textViewDelegate = new RichTextViewDelegate(this);
            platformView.Delegate = _textViewDelegate;
        }

        protected override void DisconnectHandler(RichTextView platformView)
        {
            platformView.Delegate = null!;
            _textViewDelegate?.Dispose();
            _textViewDelegate = null;
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
                var attributed = new NSMutableAttributedString(document.Text);
                if (document.Text.Length > 0)
                {
                    var fullRange = new NSRange(0, document.Text.Length);
                    using (var attributes = CreateCharacterAttributes(document.DefaultCharacterFormat))
                    {
                        attributed.SetAttributes(attributes, fullRange);
                    }

                    foreach (var run in document.Runs)
                    {
                        using var attributes = CreateCharacterAttributes(run.Format);
                        attributed.SetAttributes(
                            attributes,
                            new NSRange(run.Start, run.Length));
                    }

                    foreach (var paragraph in document.Paragraphs)
                    {
                        var end = GetParagraphEnd(document.Text, paragraph.Start);
                        if (end <= paragraph.Start)
                        {
                            continue;
                        }

                        using var attributes = CreateParagraphAttributes(paragraph.Format);
                        attributed.AddAttributes(
                            attributes,
                            new NSRange(paragraph.Start, end - paragraph.Start));
                    }

                    foreach (var link in document.Links)
                    {
                        attributed.AddAttribute(
                            UIStringAttributeKey.Link,
                            new NSString(link.Target),
                            new NSRange(link.Start, link.Length));
                    }

                    foreach (var image in document.Images)
                    {
                        ApplyImage(attributed, image);
                    }
                }

                PlatformView.AttributedText = attributed;
                SetSelectionCore(selectionStart, selectionLength);
                PlatformView.UpdatePlaceholderVisibility();
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
            if (PlatformView is null)
            {
                return;
            }

            using var characterAttributes = CreateCharacterAttributes(characterFormat);
            using var paragraphAttributes = CreateParagraphAttributes(paragraphFormat);
            var attributes = new NSMutableDictionary(characterAttributes);
            attributes.AddEntries(paragraphAttributes);
            PlatformView.TypingAttributes2 = attributes;
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
            PlatformView.SelectedRange = new NSRange(start, length);
        }

        private partial void UpdatePlaceholder(RichEditor editor) =>
            PlatformView.SetPlaceholder(
                editor.Placeholder,
                editor.PlaceholderColor.ToPlatform(),
                ResolveFont(RichTextCharacterFormat.Default));

        private partial void UpdateAppearance(RichEditor editor)
        {
            PlatformView.Font = ResolveFont(RichTextCharacterFormat.Default);
            PlatformView.TextColor = editor.TextColor.ToPlatform();
            PlatformView.TintColor = editor.TextColor.ToPlatform();
            UpdatePlaceholder(editor);

            if (!_applyingDocument)
            {
                ApplyDocumentCore(editor.Document, editor.SelectionStart, editor.SelectionLength);
                ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
            }
        }

        private partial void UpdateIsReadOnly(RichEditor editor)
        {
            PlatformView.Editable = !editor.IsReadOnly;
            PlatformView.Selectable = true;
        }

        private NSMutableDictionary CreateCharacterAttributes(RichTextCharacterFormat format)
        {
            var foreground = format.Hidden
                ? UIColor.Clear
                : (format.ForegroundColor ?? VirtualView.TextColor).ToPlatform();
            var attributes = new UIStringAttributes
            {
                Font = ResolveFont(format),
                ForegroundColor = foreground,
                UnderlineStyle = ToNativeUnderline(format.Underline),
                StrikethroughStyle = format.Strikethrough switch
                {
                    RichTextStrikethroughStyle.Double => NSUnderlineStyle.Double,
                    RichTextStrikethroughStyle.Single => NSUnderlineStyle.Single,
                    _ => NSUnderlineStyle.None,
                },
                BaselineOffset = (float)GetNativeBaselineOffset(format),
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

            var dictionary = new NSMutableDictionary(attributes.Dictionary);
            dictionary[CharacterMetadataKey] = new CharacterMetadata(format);
            return dictionary;
        }

        private NSMutableDictionary CreateParagraphAttributes(RichTextParagraphFormat format)
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
                FirstLineHeadIndent = (nfloat)(format.LeadingIndent + format.FirstLineIndent),
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

            if (format.List is { } list)
            {
                if (OperatingSystem.IsIOSVersionAtLeast(16) ||
                    OperatingSystem.IsMacCatalystVersionAtLeast(16))
                {
                    ApplyNativeTextList(style, list);
                }
            }

            var attributes = new UIStringAttributes { ParagraphStyle = style };
            var dictionary = new NSMutableDictionary(attributes.Dictionary);
            dictionary[ParagraphMetadataKey] = new ParagraphMetadata(format);
            return dictionary;
        }

        private void ApplyImage(NSMutableAttributedString attributed, RichTextImage image)
        {
            if (image.Position < 0 || image.Position >= attributed.Length)
            {
                return;
            }

            NSData? data = image.Data.IsDefaultOrEmpty ? null : NSData.FromArray(image.Data.ToArray());
            var attachment = data is null
                ? new NSTextAttachment()
                : new NSTextAttachment(data, image.MediaType);
            if (data is not null)
            {
                attachment.Image = UIImage.LoadFromData(data);
            }

            attachment.FileType = image.MediaType;
            attachment.Bounds = new CGRect(0, 0, image.Width, image.Height);
            var attributes = new UIStringAttributes { TextAttachment = attachment };
            var dictionary = new NSMutableDictionary(attributes.Dictionary);
            dictionary[ImageMetadataKey] = new ImageMetadata(image);
            attributed.AddAttributes(dictionary, new NSRange(image.Position, 1));
        }

        private RichTextDocument ReadDocumentFromPlatform()
        {
            var attributed = PlatformView.AttributedText ?? new NSAttributedString(string.Empty);
            var text = attributed.Value ?? string.Empty;
            var previous = VirtualView.Document;
            var defaultCharacterFormat = previous.DefaultCharacterFormat with
            {
                FontFamily = previous.DefaultCharacterFormat.FontFamily ?? VirtualView.FontFamily,
                FontSize = previous.DefaultCharacterFormat.FontSize ?? VirtualView.FontSize,
                ForegroundColor = previous.DefaultCharacterFormat.ForegroundColor ?? VirtualView.TextColor,
            };

            var runs = new List<RichTextRun>();
            var links = new List<RichTextLink>();
            var images = new List<RichTextImage>();
            string? activeLink = null;
            var activeLinkStart = 0;
            for (var position = 0; position < text.Length; position++)
            {
                var dictionary = attributed.GetAttributes(position, out _) ?? new NSDictionary();
                var format = ReadCharacterFormat(dictionary, defaultCharacterFormat);
                if (runs.Count > 0 && runs[^1].Format == format)
                {
                    runs[^1] = runs[^1] with { Length = runs[^1].Length + 1 };
                }
                else
                {
                    runs.Add(new RichTextRun(position, 1, format));
                }

                var attributes = new UIStringAttributes(dictionary);
                var link = GetLinkTarget(attributes.Link);
                if (!string.Equals(activeLink, link, StringComparison.Ordinal))
                {
                    if (activeLink is not null)
                    {
                        links.Add(new RichTextLink(
                            activeLinkStart,
                            position - activeLinkStart,
                            activeLink));
                    }

                    activeLink = link;
                    activeLinkStart = position;
                }

                if (text[position] == RichTextDocument.ObjectReplacementCharacter &&
                    attributes.TextAttachment is { } attachment)
                {
                    images.Add(ReadImage(dictionary, attachment, position));
                }
            }

            if (activeLink is not null)
            {
                links.Add(new RichTextLink(
                    activeLinkStart,
                    text.Length - activeLinkStart,
                    activeLink));
            }

            var paragraphs = new List<RichTextParagraph>();
            RichTextListFormat? previousList = null;
            var nextListId = 1;
            for (var start = 0; ;)
            {
                RichTextParagraphFormat format;
                if (text.Length == 0)
                {
                    format = previous.DefaultParagraphFormat;
                }
                else
                {
                    var index = Math.Min(start, text.Length - 1);
                    format = ReadParagraphFormat(
                        attributed.GetAttributes(index, out _) ?? new NSDictionary(),
                        previous.DefaultParagraphFormat);
                }

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
            NSDictionary dictionary,
            RichTextCharacterFormat defaultFormat)
        {
            var metadata = dictionary[CharacterMetadataKey] as CharacterMetadata;
            var format = metadata?.Format ?? defaultFormat;
            var attributes = new UIStringAttributes(dictionary);
            if (attributes.Font is { } font)
            {
                var traits = font.FontDescriptor.SymbolicTraits;
                format = format with
                {
                    FontFamily = font.FamilyName,
                    FontSize = font.PointSize,
                    FontWeight = traits.HasFlag(UIFontDescriptorSymbolicTraits.Bold)
                        ? Math.Max(format.FontWeight, 700)
                        : metadata is null ? 400 : format.FontWeight,
                    Italic = traits.HasFlag(UIFontDescriptorSymbolicTraits.Italic),
                };
            }

            if (attributes.ForegroundColor is { } foreground && !format.Hidden)
            {
                format = format with { ForegroundColor = FromUIColor(foreground) };
            }

            if (attributes.BackgroundColor is { } background)
            {
                format = format with { BackgroundColor = FromUIColor(background) };
            }

            if (metadata is null || metadata.Format.Underline == RichTextUnderlineStyle.None)
            {
                format = format with
                {
                    Underline = FromNativeUnderline(
                        attributes.UnderlineStyle ?? NSUnderlineStyle.None),
                };
            }

            if (metadata is null || metadata.Format.Strikethrough == RichTextStrikethroughStyle.None)
            {
                format = format with
                {
                    Strikethrough = attributes.StrikethroughStyle switch
                    {
                        NSUnderlineStyle.Double => RichTextStrikethroughStyle.Double,
                        NSUnderlineStyle.None => RichTextStrikethroughStyle.None,
                        _ => RichTextStrikethroughStyle.Single,
                    },
                };
            }

            if (attributes.UnderlineColor is { } underlineColor)
            {
                format = format with { UnderlineColor = FromUIColor(underlineColor) };
            }

            if (attributes.StrikethroughColor is { } strikeColor)
            {
                format = format with { StrikethroughColor = FromUIColor(strikeColor) };
            }

            if (metadata is null)
            {
                var baseline = attributes.BaselineOffset ?? 0;
                var kerning = attributes.KerningAdjustment ?? 0;
                var expansion = attributes.Expansion ?? 0;
                format = format with
                {
                    BaselineOffset = baseline,
                    Script = baseline > 0
                        ? RichTextScript.Superscript
                        : baseline < 0
                            ? RichTextScript.Subscript
                            : RichTextScript.Normal,
                    CharacterSpacing = kerning,
                    HorizontalScale = Math.Max(1d + expansion, 0.01d),
                    Outline = attributes.StrokeWidth is not null and not 0,
                    Shadow = attributes.Shadow is not null,
                };
            }

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
                return format;
            }

            RichTextListFormat? list = format.List;
            if (metadata is null &&
                (OperatingSystem.IsIOSVersionAtLeast(16) ||
                 OperatingSystem.IsMacCatalystVersionAtLeast(16)))
            {
                list = ReadNativeTextList(style);
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

            return format with
            {
                Alignment = style.Alignment switch
                {
                    UITextAlignment.Center => RichTextAlignment.Center,
                    UITextAlignment.Right => RichTextAlignment.Right,
                    UITextAlignment.Justified => RichTextAlignment.Justified,
                    _ => RichTextAlignment.Left,
                },
                Direction = style.BaseWritingDirection switch
                {
                    NSWritingDirection.LeftToRight => RichTextDirection.LeftToRight,
                    NSWritingDirection.RightToLeft => RichTextDirection.RightToLeft,
                    _ => RichTextDirection.Automatic,
                },
                LeadingIndent = style.HeadIndent,
                FirstLineIndent = style.FirstLineHeadIndent - style.HeadIndent,
                TrailingIndent = style.TailIndent < 0 ? -style.TailIndent : 0,
                SpaceBefore = Math.Max(style.ParagraphSpacingBefore, 0),
                SpaceAfter = Math.Max(style.ParagraphSpacing, 0),
                LineSpacingRule = lineSpacingRule,
                LineSpacing = Math.Max(lineSpacing, 0),
                MinimumLineHeight = style.MinimumLineHeight > 0 ? style.MinimumLineHeight : null,
                MaximumLineHeight = style.MaximumLineHeight > 0 ? style.MaximumLineHeight : null,
                TabStops = (style.TabStops ?? [])
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
                List = list,
            };
        }

        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        private static void ApplyNativeTextList(
            NSMutableParagraphStyle style,
            RichTextListFormat list)
        {
            var markerFormat = list.Kind == RichListKind.Bulleted
                ? "{disc}"
                : list.NumberStyle switch
                {
                    RichListNumberStyle.UpperRoman => "{upper-roman}",
                    RichListNumberStyle.LowerRoman => "{lower-roman}",
                    RichListNumberStyle.UpperLetter => "{upper-alpha}",
                    RichListNumberStyle.LowerLetter => "{lower-alpha}",
                    _ => "{decimal}",
                };
            markerFormat = string.Concat(list.Prefix, markerFormat, list.Suffix);
            var textList = new NSTextList(
                markerFormat,
                NSTextListOptions.PrependEnclosingMarker,
                list.StartAt);
            style.TextLists = Enumerable.Repeat(textList, list.Level + 1).ToArray();
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

        private static RichTextImage ReadImage(
            NSDictionary dictionary,
            NSTextAttachment attachment,
            int position)
        {
            if (dictionary[ImageMetadataKey] is ImageMetadata metadata)
            {
                return metadata.Image with { Position = position };
            }

            var data = attachment.Contents?.ToArray() ??
                attachment.Image?.AsPNG()?.ToArray() ?? [];
            return new RichTextImage
            {
                Position = position,
                MediaType = string.IsNullOrWhiteSpace(attachment.FileType)
                    ? "application/octet-stream"
                    : attachment.FileType,
                Data = ImmutableArray.CreateRange(data),
                Width = attachment.Bounds.Width,
                Height = attachment.Bounds.Height,
            };
        }

        private static string? GetLinkTarget(NSObject? value) => value switch
        {
            null => null,
            NSUrl url => url.AbsoluteString,
            NSString text when text.Length > 0 => text.ToString(),
            _ => value.ToString(),
        };

        private UIFont ResolveFont(RichTextCharacterFormat format)
        {
            var family = format.FontFamily ?? VirtualView.FontFamily;
            var size = (nfloat)(format.FontSize ?? VirtualView.FontSize);
            var font = (string.IsNullOrWhiteSpace(family)
                ? UIFont.SystemFontOfSize(size)
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

            if (value.HasFlag(NSUnderlineStyle.PatternDot))
            {
                return RichTextUnderlineStyle.Dotted;
            }

            if (value.HasFlag(NSUnderlineStyle.PatternDashDotDot))
            {
                return RichTextUnderlineStyle.DashDotDot;
            }

            if (value.HasFlag(NSUnderlineStyle.PatternDashDot))
            {
                return RichTextUnderlineStyle.DashDot;
            }

            if (value.HasFlag(NSUnderlineStyle.PatternDash))
            {
                return RichTextUnderlineStyle.Dash;
            }

            return value.HasFlag(NSUnderlineStyle.Thick)
                ? RichTextUnderlineStyle.Thick
                : RichTextUnderlineStyle.Single;
        }

        private static double GetNativeBaselineOffset(RichTextCharacterFormat format) =>
            format.BaselineOffset + format.Script switch
            {
                RichTextScript.Superscript => (format.FontSize ?? 12d) * 0.35d,
                RichTextScript.Subscript => (format.FontSize ?? 12d) * -0.15d,
                _ => 0,
            };

        private static Color FromUIColor(UIColor color)
        {
            color.GetRGBA(out var red, out var green, out var blue, out var alpha);
            return Color.FromRgba((float)red, (float)green, (float)blue, (float)alpha);
        }

        private static int GetParagraphEnd(string text, int start)
        {
            var newline = text.IndexOf('\n', start);
            return newline < 0 ? text.Length : newline + 1;
        }

        private void OnNativeDocumentChanged(UITextView textView)
        {
            if (_applyingDocument || VirtualView is null)
            {
                return;
            }

            PlatformView.UpdatePlaceholderVisibility();
            var document = ReadDocumentFromPlatform();
            var start = Math.Clamp((int)textView.SelectedRange.Location, 0, document.Text.Length);
            var length = Math.Clamp(
                (int)textView.SelectedRange.Length,
                0,
                document.Text.Length - start);
            VirtualView.UpdateDocumentFromPlatform(document, start, length);
        }

        private void OnNativeSelectionChanged(UITextView textView)
        {
            if (_applyingDocument || VirtualView is null)
            {
                return;
            }

            var start = Math.Clamp(
                (int)textView.SelectedRange.Location,
                0,
                VirtualView.Document.Text.Length);
            var length = Math.Clamp(
                (int)textView.SelectedRange.Length,
                0,
                VirtualView.Document.Text.Length - start);
            VirtualView.UpdateSelectionFromPlatform(start, length);
        }

        private sealed class RichTextViewDelegate(RichEditorHandler handler) : UITextViewDelegate
        {
            private readonly WeakReference<RichEditorHandler> _handler = new(handler);

            public override void Changed(UITextView textView)
            {
                if (_handler.TryGetTarget(out var target))
                {
                    target.OnNativeDocumentChanged(textView);
                }
            }

            public override void SelectionChanged(UITextView textView)
            {
                if (_handler.TryGetTarget(out var target))
                {
                    target.OnNativeSelectionChanged(textView);
                }
            }
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
}
#endif
