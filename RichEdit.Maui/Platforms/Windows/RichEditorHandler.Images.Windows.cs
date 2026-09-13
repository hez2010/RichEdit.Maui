using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Windows.Storage.Streams;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyImagesIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextChangeSet changes,
        RichTextRange affectedRange,
        ISet<int>? loadedImages = null)
    {
        var imageChanges = changes.Changes
            .Where(static change => change.Kind == RichTextChangeKind.Image)
            .ToArray();
        var imagePositions = snapshot.Images.Select(image => image.Position).ToHashSet();
        foreach (var image in snapshot.Images.Where(image =>
            image.Position >= affectedRange.Start &&
            image.Position < affectedRange.End && (loadedImages is null || !loadedImages.Contains(image.Position))))
        {
            ApplyImageIncrementally(image);
        }

        for (var position = affectedRange.Start; position < affectedRange.End; position++)
        {
            if (snapshot.Text[position] == '\uFFFC' && !imagePositions.Contains(position))
            {
                ApplyImageIncrementally(new RichTextImage { Position = position });
            }
        }

        if (changes.IsTextChanged)
        {
            return;
        }

        // An image can be removed while its logical U+FFFC position remains. In
        // that case replace the native object itself with the literal placeholder.
        foreach (var change in imageChanges)
        {
            var range = change.OldRange.Clamp(snapshot.Length);
            for (var position = range.Start; position < range.End; position++)
            {
                if (snapshot.Text[position] == RichTextDocument.ObjectReplacementCharacter &&
                    !imagePositions.Contains(position))
                {
                    ReplaceNativeImageWithPlaceholder(position);
                }
            }
        }
    }

    private void ApplyImageIncrementally(RichTextImage image)
    {
        var positions = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        var range = PlatformView.Document.GetRange(
            positions?.ToNativePosition(image.Position) ?? image.Position,
            positions?.ToNativePosition(image.Position + 1) ?? image.Position + 1);
        var insertionPosition = range.StartPosition;
        range.SetText(TextSetOptions.None, string.Empty);
        range.SetRange(insertionPosition, insertionPosition);
        var lengthBeforeInsert = range.StoryLength;
        try
        {
            if (image.Adornment is not null)
            {
                InsertImage(ImmutableCollectionsMarshal.AsArray(RichTextDisplayProjection.TransparentPixel)!, image.Width * 0.75, image.Height * 0.75);
            }
            else if (!image.Data.IsDefaultOrEmpty)
            {
                InsertImage(ImmutableCollectionsMarshal.AsArray(image.Data)!, image.Width, image.Height);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or COMException)
        {
            // Unsupported payloads remain owned by the managed snapshot.
        }

        if (range.StoryLength == lengthBeforeInsert ||
            PlatformView.Document.GetRange(insertionPosition, insertionPosition + 1).Character != '\uFFFC')
        {
            range.SetRange(insertionPosition, insertionPosition + Math.Max(0, range.StoryLength - lengthBeforeInsert));
            range.SetText(TextSetOptions.None, string.Empty);
            range.SetRange(insertionPosition, insertionPosition);
            InsertImage(ImagePlaceholder, 12, 12);
        }

        _nativeTextSnapshot = null;

        void InsertImage(byte[] data, double width, double height)
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(data);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            stream.Seek(0);
            range.InsertImage(ToNativeImageSize(width), ToNativeImageSize(height), image.AdornmentBaseline is { } baseline ? ToNativeImageSize(baseline * 0.75) : 0,
                image.VerticalAlignment switch
                {
                    RichTextImageVerticalAlignment.Top => VerticalCharacterAlignment.Top,
                    RichTextImageVerticalAlignment.Bottom => VerticalCharacterAlignment.Bottom,
                    _ => VerticalCharacterAlignment.Baseline,
                }, GetWindowsImageAlternativeText(image.AlternativeText), stream);
        }
    }

    private void ReplaceNativeImageWithPlaceholder(int position)
    {
        ApplyImageIncrementally(new RichTextImage { Position = position });
    }

    private static int ToNativeImageSize(double points)
    {
        if (points <= 0)
        {
            return 0;
        }

        // WinUI's projected ITextRange API takes DIPs, while the document model
        // uses the RTF unit of points.
        var deviceIndependentPixels = Math.Round(points * 96d / 72d);
        return (int)Math.Clamp(deviceIndependentPixels, 1d, int.MaxValue);
    }

    // WinUI returns E_INVALIDARG for an empty alternate-text HSTRING. Use the
    // same neutral character that represents the inline object in logical text.
    internal static string GetWindowsImageAlternativeText(string? alternativeText) =>
        string.IsNullOrEmpty(alternativeText)
            ? RichTextDocument.ObjectReplacementCharacter.ToString()
            : alternativeText;
}
