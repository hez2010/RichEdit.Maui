#if IOS || MACCATALYST
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using UIKit;


namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyImage(NSMutableAttributedString attributed, RichTextImage image)
    {
        if (image.Position < 0 || image.Position >= attributed.Length)
        {
            return;
        }

        var bytes = image.Data.IsDefaultOrEmpty
            ? null
            : ImmutableCollectionsMarshal.AsArray(image.Data);
        NSTextAttachment attachment;
        if (bytes is null)
        {
            attachment = new NSTextAttachment();
        }
        else
        {
            using var data = NSData.FromArray(bytes);
            using var renderedImage = UIImage.LoadFromData(data);
            if (renderedImage is null)
            {
                attachment = new NSTextAttachment();
            }
            else
            {
                if (!string.IsNullOrEmpty(image.AlternativeText))
                {
                    renderedImage.AccessibilityLabel = image.AlternativeText;
                }

                attachment = NSTextAttachment.Create(renderedImage);
            }
        }

        var characterAttributes = attributed.GetAttributes(image.Position, out _);
        var font = characterAttributes is null
            ? null
            : new UIStringAttributes(characterAttributes).Font;
        attachment.Bounds = new CGRect(
            0,
            image.Adornment is { } adornment ? (adornment.Options.Baseline ?? image.Height) - image.Height : GetImageVerticalOffset(font, image.Height, image.VerticalAlignment),
            image.Width,
            image.Height);
        var attributes = new UIStringAttributes { TextAttachment = attachment };
        var dictionary = new NSMutableDictionary(attributes.Dictionary);
        dictionary[ImageMetadataKey] = new ImageMetadata(image);
        attributed.AddAttributes(dictionary, new NSRange(image.Position, 1));
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
            VerticalAlignment = ReadImageVerticalAlignment(dictionary, attachment.Bounds),
            AlternativeText = attachment.Image?.AccessibilityLabel,
        };
    }

    private static nfloat GetImageVerticalOffset(
        UIFont? font,
        double height,
        RichTextImageVerticalAlignment alignment)
    {
        if (font is null || alignment == RichTextImageVerticalAlignment.Baseline)
        {
            return 0;
        }

        return alignment switch
        {
            RichTextImageVerticalAlignment.Bottom => font.Descender,
            RichTextImageVerticalAlignment.Center =>
                (nfloat)(((double)font.CapHeight - height) / 2d),
            RichTextImageVerticalAlignment.Top => (nfloat)((double)font.Ascender - height),
            _ => 0,
        };
    }

    private static RichTextImageVerticalAlignment ReadImageVerticalAlignment(
        NSDictionary dictionary,
        CGRect bounds)
    {
        var font = new UIStringAttributes(dictionary).Font;
        if (font is null)
        {
            return RichTextImageVerticalAlignment.Baseline;
        }

        var actual = (double)bounds.Y;
        var result = RichTextImageVerticalAlignment.Baseline;
        var distance = Math.Abs(actual);
        var bottomDistance = Math.Abs(
            actual - GetImageVerticalOffset(
                font,
                bounds.Height,
                RichTextImageVerticalAlignment.Bottom));
        if (bottomDistance < distance)
        {
            distance = bottomDistance;
            result = RichTextImageVerticalAlignment.Bottom;
        }

        var centerDistance = Math.Abs(
            actual - GetImageVerticalOffset(
                font,
                bounds.Height,
                RichTextImageVerticalAlignment.Center));
        if (centerDistance < distance)
        {
            distance = centerDistance;
            result = RichTextImageVerticalAlignment.Center;
        }

        var topDistance = Math.Abs(
            actual - GetImageVerticalOffset(
                font,
                bounds.Height,
                RichTextImageVerticalAlignment.Top));
        if (topDistance < distance)
        {
            result = RichTextImageVerticalAlignment.Top;
        }

        return result;
    }
}
#endif
