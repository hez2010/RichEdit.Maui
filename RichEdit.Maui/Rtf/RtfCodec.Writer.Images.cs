using System.Globalization;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Writer
    {
        private void WriteImage(RichTextImage image)
        {
            var format = GetRunAt(image.Position).Format;
            if (!TryGetPictureControl(
                image.MediaType,
                image.Data.AsSpan(),
                out var pictureControl,
                out var isRaster))
            {
                WriteRun(
                    _document.Text.AsSpan(image.Position, 1),
                    format);
                return;
            }

            var hasFormatting = HasDirectCharacterFormatting(
                format,
                _document.DefaultCharacterFormat);
            if (hasFormatting)
            {
                _output.Append('{');
                WriteFormatControls(format, _document.DefaultCharacterFormat);
            }

            WritePictureData(
                pictureControl,
                isRaster,
                image.Data.AsSpan(),
                image.Width,
                image.Height,
                image.Crop,
                image.AlternativeText,
                image.Rotation);

            if (hasFormatting)
            {
                _output.Append('}');
            }
        }

        private void WritePictureData(
            string pictureControl,
            bool isRaster,
            ReadOnlySpan<byte> bytes,
            double width,
            double height,
            RichTextImageCrop crop,
            string? alternativeText,
            double rotation)
        {
            _output.Append(@"{\pict").Append(pictureControl);
            if (isRaster && width > 0)
            {
                _output.Append(@"\picw").Append(ToPixelsAt96Dpi(width));
            }

            if (isRaster && height > 0)
            {
                _output.Append(@"\pich").Append(ToPixelsAt96Dpi(height));
            }

            if (width > 0)
            {
                _output.Append(@"\picwgoal").Append(ToTwips(width));
            }

            if (height > 0)
            {
                _output.Append(@"\pichgoal").Append(ToTwips(height));
            }

            if (crop.Top != 0)
            {
                _output.Append(@"\piccropt").Append(ToTwips(crop.Top));
            }

            if (crop.Bottom != 0)
            {
                _output.Append(@"\piccropb").Append(ToTwips(crop.Bottom));
            }

            if (crop.Left != 0)
            {
                _output.Append(@"\piccropl").Append(ToTwips(crop.Left));
            }

            if (crop.Right != 0)
            {
                _output.Append(@"\piccropr").Append(ToTwips(crop.Right));
            }

            WritePictureProperties(alternativeText, rotation);

            _output.Append(' ');
            const string hexDigits = "0123456789abcdef";
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                _output.Append(hexDigits[value >> 4])
                    .Append(hexDigits[value & 0x0f]);
                if ((index + 1) % 32 == 0)
                {
                    _output.Append("\r\n");
                }
            }

            _output.Append('}');
        }

        private void WritePictureProperties(string? alternativeText, double rotation)
        {
            rotation = Math.Abs(rotation) <= 32767d
                ? rotation
                : Math.IEEERemainder(rotation, 360d);
            if (string.IsNullOrEmpty(alternativeText) && rotation == 0)
            {
                return;
            }

            _output.Append(@"{\*\picprop");
            if (!string.IsNullOrEmpty(alternativeText))
            {
                WritePictureProperty("wzDescription", alternativeText);
            }

            if (rotation != 0)
            {
                var fixedAngle = checked((int)Math.Round(rotation * 65536d));
                WritePictureProperty(
                    "rotation",
                    fixedAngle.ToString(CultureInfo.InvariantCulture));
            }

            _output.Append('}');
        }

        private void WritePictureProperty(string name, string value)
        {
            _output.Append(@"{\sp{\sn ");
            WriteText(name.AsSpan());
            _output.Append(@"}{\sv ");
            WriteText(value.AsSpan());
            _output.Append("}}");
        }

        private static bool TryGetPictureControl(
            string mediaType,
            ReadOnlySpan<byte> data,
            out string control,
            out bool isRaster)
        {
            switch (mediaType.Trim().ToLowerInvariant())
            {
                case "image/png":
                    control = @"\pngblip";
                    isRaster = true;
                    return true;
                case "image/jpeg":
                case "image/jpg":
                    control = @"\jpegblip";
                    isRaster = true;
                    return true;
                case "image/emf":
                case "image/x-emf":
                    control = @"\emfblip";
                    isRaster = false;
                    return true;
                case "image/wmf":
                case "image/x-wmf":
                    control = @"\wmetafile8";
                    isRaster = false;
                    return true;
            }

            if (data.Length >= 8 &&
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47 &&
                data[4] == 0x0d && data[5] == 0x0a && data[6] == 0x1a && data[7] == 0x0a)
            {
                control = @"\pngblip";
                isRaster = true;
                return true;
            }

            if (data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff)
            {
                control = @"\jpegblip";
                isRaster = true;
                return true;
            }

            if (data.Length >= 44 &&
                data[0] == 1 && data[1] == 0 && data[2] == 0 && data[3] == 0 &&
                data[40] == 0x20 && data[41] == 0x45 && data[42] == 0x4d && data[43] == 0x46)
            {
                control = @"\emfblip";
                isRaster = false;
                return true;
            }

            if (data.Length >= 4 &&
                data[0] == 0xd7 && data[1] == 0xcd && data[2] == 0xc6 && data[3] == 0x9a ||
                data.Length >= 4 && data[0] is 1 or 2 && data[1] == 0 &&
                data[2] == 9 && data[3] == 0)
            {
                control = @"\wmetafile8";
                isRaster = false;
                return true;
            }

            control = string.Empty;
            isRaster = false;
            return false;
        }

        private static int ToPixelsAt96Dpi(double points) =>
            checked((int)Math.Round(points * 96d / 72d));
    }
}
