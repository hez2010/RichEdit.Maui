using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Style;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyImage(ISpannable text, RichTextImage image)
    {
        if (image.Adornment is { } adornment)
        {
            text.SetSpan(new RichAdornmentSpan(ToPixels(image.Width), ToPixels(image.Height), ToPixels(adornment.Options.Baseline ?? image.Height)),
                image.Position, image.Position + 1, SpanTypes.ExclusiveExclusive);
            return;
        }
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
}
