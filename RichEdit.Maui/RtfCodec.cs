using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RichEdit.Maui;

/// <summary>
/// Reads and writes the RTF 1.9.1 controls represented by <see cref="RichTextDocument"/>.
/// Unknown controls and ignorable destinations follow the RTF reader rules.
/// </summary>
internal static class RtfCodec
{
    private const int MaximumListOverrideId = 2000;
    private const double DefaultRtfFontSize = 12d;
    private const int DefaultCodePage = 65001;
    private const double TwipsPerPoint = 20d;
    private const int SingleLineSpacingTwips = 240;

    private enum ListNumberFormat
    {
        Arabic = 0,
        UpperRoman = 1,
        LowerRoman = 2,
        UpperLetter = 3,
        LowerLetter = 4,
    }

    public static string Serialize(RichTextDocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Writer(document).Write();
    }

    public static string SerializeForNativeProjection(RichTextDocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Writer(document, includeSemanticRanges: false).Write();
    }

    public static string SerializeForNativeProjection(
        RichTextDocumentSnapshot document,
        RichTextCharacterFormat nativeDefaultCharacterFormat)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(nativeDefaultCharacterFormat);
        return new Writer(
            document,
            includeSemanticRanges: false,
            nativeDefaultCharacterFormat).Write();
    }

    public static RichTextDocumentSnapshot Parse(string rtf)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        return new Reader(rtf).Read();
    }

    private readonly record struct RtfColor(byte Red, byte Green, byte Blue);

    private sealed class Writer
    {
        private readonly RichTextDocumentSnapshot _document;
        private readonly StringBuilder _output = new();
        private readonly List<string> _fontNames = [];
        private readonly Dictionary<string, int> _fontIndices = new(StringComparer.Ordinal);
        private readonly List<RtfColor> _colors = [];
        private readonly Dictionary<RtfColor, int> _colorIndices = [];
        private readonly List<ListDefinition> _listDefinitions;
        private readonly List<ListOverrideDefinition> _listOverrides = [];
        private readonly RichTextListPicture[] _listPictures;
        private readonly Dictionary<string, int> _listPictureIndices;
        private readonly Dictionary<int, ListItemDefinition> _listsByItemStart = [];
        private readonly Dictionary<(int OverrideId, int Level), int> _nextListNumbers = [];
        private readonly RichTextRun[] _runs;
        private readonly Dictionary<int, RichTextField> _fieldsByStart;
        private readonly Dictionary<int, RichTextField[]> _emptyFieldsByPosition;
        private readonly Dictionary<int, RichTextLink> _linksByStart;
        private readonly Dictionary<int, RichTextImage> _imagesByPosition;
        private readonly int[] _semanticStarts;
        private readonly int[] _semanticBoundaries;
        private readonly RichTextCharacterFormat? _nativeDefaultCharacterFormat;

        public Writer(
            RichTextDocumentSnapshot document,
            bool includeSemanticRanges = true,
            RichTextCharacterFormat? nativeDefaultCharacterFormat = null)
        {
            _document = document;
            _nativeDefaultCharacterFormat = nativeDefaultCharacterFormat;
            AddFont(
                document.DefaultCharacterFormat.FontFamily ??
                nativeDefaultCharacterFormat?.FontFamily ??
                "Arial");
            BuildFormattingTables();
            _listPictures = document.Paragraphs
                .Select(paragraph => paragraph.Format.NativeList?.PictureId)
                .Where(static id => id is not null)
                .Distinct(StringComparer.Ordinal)
                .Select(id => document.ListPictures[id!])
                .Where(static picture => TryGetPictureControl(
                    picture.MediaType,
                    picture.Data.AsSpan(),
                    out _,
                    out _))
                .ToArray();
            _listPictureIndices = _listPictures
                .Select((picture, index) => (picture.Id, Index: index))
                .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
            _listDefinitions = BuildListDefinitions();
            _runs = [.. document.Runs];
            _semanticBoundaries = includeSemanticRanges
                ? document.Fields
                    .SelectMany(field => new[] { field.Start, field.End })
                    .Concat(document.Links.SelectMany(link => new[] { link.Start, link.End }))
                    .Distinct()
                    .Order()
                    .ToArray()
                : [];
            _fieldsByStart = includeSemanticRanges
                ? SplitFields().ToDictionary(field => field.Start)
                : [];
            _emptyFieldsByPosition = includeSemanticRanges
                ? document.Fields
                    .Where(field => field.Length == 0)
                    .GroupBy(field => field.Start)
                    .ToDictionary(group => group.Key, group => group.ToArray())
                : [];
            _linksByStart = includeSemanticRanges
                ? SplitLinks().ToDictionary(link => link.Start)
                : [];
            _imagesByPosition = document.Images.ToDictionary(image => image.Position);
            _semanticStarts = _fieldsByStart.Keys
                .Concat(_emptyFieldsByPosition.Keys)
                .Concat(_linksByStart.Keys)
                .Concat(_imagesByPosition.Keys)
                .Distinct()
                .Order()
                .ToArray();
        }

        public string Write()
        {
            _output.Append(@"{\rtf1\ansi\ansicpg")
                .Append(DefaultCodePage)
                .Append(@"\uc1\deff0")
                .Append("\r\n");
            WriteFontTable();
            WriteColorTable();
            WriteDefaultCharacterProperties();
            WriteDefaultParagraphProperties();
            WriteListTables();
            WriteBody();
            _output.Append('}');
            return _output.ToString();
        }

        private void BuildFormattingTables()
        {
            if (_nativeDefaultCharacterFormat?.FontFamily is { } fontFamily)
            {
                AddFont(fontFamily);
            }

            if (_nativeDefaultCharacterFormat?.FontSize is { } nativeFontSize)
            {
                _ = GetHalfPointSize(nativeFontSize);
            }

            AddColor(_nativeDefaultCharacterFormat?.ForegroundColor);
            AddCharacterFormatColors(_document.DefaultCharacterFormat);
            AddParagraphFormatColors(_document.DefaultParagraphFormat);
            if (_document.DefaultCharacterFormat.FontSize is { } defaultFontSize)
            {
                _ = GetHalfPointSize(defaultFontSize);
            }

            foreach (var run in _document.Runs)
            {
                var format = run.Format;
                if (format.FontFamily is not null)
                {
                    AddFont(format.FontFamily);
                }

                if (format.FontSize is { } fontSize)
                {
                    _ = GetHalfPointSize(fontSize);
                }

                AddCharacterFormatColors(format);
            }

            foreach (var paragraph in _document.Paragraphs)
            {
                AddParagraphFormatColors(paragraph.Format);
            }
        }

        private void AddCharacterFormatColors(RichTextCharacterFormat format)
        {
            AddColor(format.ForegroundColor);
            AddColor(format.BackgroundColor);
            AddColor(format.UnderlineColor);
            AddColor(format.StrikethroughColor);
            AddColor(format.ShadingForegroundColor);
            AddColor(format.ShadingBackgroundColor);
        }

        private void AddParagraphFormatColors(RichTextParagraphFormat format)
        {
            AddColor(format.BackgroundColor);
            AddColor(format.ShadingForegroundColor);
            AddColor(format.ShadingBackgroundColor);
            AddColor(format.Border?.Color);
        }

        private void AddFont(string fontFamily)
        {
            if (_fontIndices.ContainsKey(fontFamily))
            {
                return;
            }

            _fontIndices.Add(fontFamily, _fontNames.Count);
            _fontNames.Add(fontFamily);
        }

        private void AddColor(Color? color)
        {
            if (color is null)
            {
                return;
            }

            var rtfColor = GetRtfColor(color);
            if (_colorIndices.ContainsKey(rtfColor))
            {
                return;
            }

            _colors.Add(rtfColor);
            _colorIndices.Add(rtfColor, _colors.Count);
        }

        private static RtfColor GetRtfColor(Color color)
        {
            if (!float.IsFinite(color.Red) || !float.IsFinite(color.Green) ||
                !float.IsFinite(color.Blue) || !float.IsFinite(color.Alpha))
            {
                throw new InvalidOperationException("A color contains a non-finite channel.");
            }

            return new RtfColor(
                GetColorByte(color.Red),
                GetColorByte(color.Green),
                GetColorByte(color.Blue));
        }

        private static byte GetColorByte(float component)
        {
            return checked((byte)MathF.Round(Math.Clamp(component, 0f, 1f) * byte.MaxValue));
        }

        private static int GetHalfPointSize(double fontSize)
        {
            var halfPoints = fontSize * 2d;
            if (!double.IsFinite(fontSize) || fontSize <= 0 || halfPoints > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "The font size is outside the range representable by RTF.");
            }

            return checked((int)Math.Round(halfPoints));
        }

        private static int ToTwips(double points)
        {
            var twips = points * TwipsPerPoint;
            if (!double.IsFinite(points) || twips < int.MinValue || twips > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "A point measurement is outside the range representable by RTF.");
            }

            return checked((int)Math.Round(twips));
        }

        private static int GetLanguageId(string? languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return 0;
            }

            try
            {
                return CultureInfo.GetCultureInfo(languageTag).LCID;
            }
            catch (CultureNotFoundException)
            {
                return 0;
            }
        }

        private void WriteFontTable()
        {
            _output.Append(@"{\fonttbl");
            for (var index = 0; index < _fontNames.Count; index++)
            {
                _output.Append(@"{\f").Append(index).Append(@"\fnil ");
                WriteText(_fontNames[index].AsSpan(), escapeFontTerminator: true);
                _output.Append(";}");
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteColorTable()
        {
            if (_colors.Count == 0)
            {
                return;
            }

            _output.Append(@"{\colortbl;");
            foreach (var color in _colors)
            {
                _output.Append(@"\red").Append(color.Red)
                    .Append(@"\green").Append(color.Green)
                    .Append(@"\blue").Append(color.Blue)
                    .Append(';');
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteDefaultCharacterProperties()
        {
            if (_document.DefaultCharacterFormat == RichTextCharacterFormat.Default)
            {
                return;
            }

            _output.Append(@"{\*\defchp");
            WriteFormatControls(
                _document.DefaultCharacterFormat,
                RichTextCharacterFormat.Default);
            _output.Append('}').Append("\r\n");
        }

        private void WriteDefaultParagraphProperties()
        {
            if (_document.DefaultParagraphFormat == RichTextParagraphFormat.Default)
            {
                return;
            }

            _output.Append(@"{\*\defpap");
            WriteParagraphFormatControls(
                _document.DefaultParagraphFormat,
                RichTextParagraphFormat.Default);
            _output.Append('}').Append("\r\n");
        }

        private void WriteListTables()
        {
            if (_listDefinitions.Count == 0)
            {
                return;
            }

            _output.Append(@"{\*\listtable").Append("\r\n");
            WriteListPictures();
            foreach (var definition in _listDefinitions)
            {
                _output.Append(@"{\list\listtemplateid").Append(definition.ListId)
                    .Append(definition.IsMultilevel ? @"\listhybrid" : @"\listsimple1");

                var levelCount = definition.IsMultilevel ? 9 : 1;
                for (var level = 0; level < levelCount; level++)
                {
                    WriteListLevel(definition.Levels[level], level);
                }

                _output.Append(@"\listrestarthdn0\listid")
                    .Append(definition.ListId)
                    .Append(@"{\listname ;}}")
                    .Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
            _output.Append(@"{\*\listoverridetable").Append("\r\n");
            foreach (var listOverride in _listOverrides)
            {
                var levelCount = listOverride.HasStartOverrides
                    ? (listOverride.Definition.IsMultilevel ? 9 : 1)
                    : 0;
                _output.Append(@"{\listoverride\listid")
                    .Append(listOverride.Definition.ListId)
                    .Append(@"\listoverridecount").Append(levelCount)
                    .Append(@"\ls").Append(listOverride.OverrideId);
                for (var level = 0; level < levelCount; level++)
                {
                    _output.Append(@"{\lfolevel");
                    if (listOverride.StartAtByLevel[level] is { } startAt)
                    {
                        _output.Append(@"\listoverridestartat\levelstartat").Append(startAt);
                    }

                    _output.Append('}');
                }

                _output.Append('}').Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteListPictures()
        {
            if (_listPictures.Length == 0)
            {
                return;
            }

            _output.Append(@"{\*\listpicture").Append("\r\n");
            foreach (var picture in _listPictures)
            {
                _ = TryGetPictureControl(
                    picture.MediaType,
                    picture.Data.AsSpan(),
                    out var control,
                    out var isRaster);
                _output.Append(@"{\*\shppict");
                WritePictureData(
                    control,
                    isRaster,
                    picture.Data.AsSpan(),
                    picture.Width,
                    picture.Height,
                    default,
                    picture.AlternativeText,
                    rotation: 0);
                _output.Append('}').Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteListLevel(ListLevelDefinition? definition, int level)
        {
            var numberFormat = definition switch
            {
                { Kind: RichListKind.Bulleted } => 23,
                { Kind: RichListKind.Numbered } => (int)definition.NumberFormat,
                _ => 255,
            };
            _output.Append(@"{\listlevel\levelnfc").Append(numberFormat)
                .Append(@"\levelnfcn").Append(numberFormat)
                .Append(@"\leveljc0\leveljcn0\levelfollow0\levelstartat")
                .Append(definition?.StartAt ?? 1);
            if (level > 0)
            {
                // RichTextListFormat counters are independent per level. Prevent an
                // increment at a superior level from implicitly resetting this one.
                _output.Append(@"\levelnorestart1");
            }

            WriteListLevelText(definition, level);
            if (definition?.PictureId is { } pictureId &&
                _listPictureIndices.TryGetValue(pictureId, out var pictureIndex))
            {
                _output.Append(@"\levelpicture").Append(pictureIndex);
            }

            var indent = checked((level + 1) * 720);
            _output.Append(@"\fi-360\li").Append(indent)
                .Append(@"\lin").Append(indent)
                .Append('}');
        }

        private void WriteListLevelText(ListLevelDefinition? definition, int level)
        {
            _output.Append(@"{\leveltext");
            if (definition is null)
            {
                WriteHexByte(0);
                _output.Append(@";}{\levelnumbers;}");
                return;
            }

            if (definition.Kind == RichListKind.Bulleted)
            {
                var bulletLength = Math.Min(definition.BulletText.Length, byte.MaxValue);
                WriteHexByte(bulletLength);
                WriteText(definition.BulletText.AsSpan(0, bulletLength));
                _output.Append(@";}{\levelnumbers;}");
                return;
            }

            var prefixLength = Math.Min(definition.Prefix.Length, byte.MaxValue - 1);
            var suffixLength = Math.Min(
                definition.Suffix.Length,
                byte.MaxValue - prefixLength - 1);
            WriteHexByte(prefixLength + suffixLength + 1);
            WriteText(definition.Prefix.AsSpan(0, prefixLength));
            WriteHexByte(level);
            WriteText(definition.Suffix.AsSpan(0, suffixLength));
            _output.Append(@";}{\levelnumbers");
            WriteHexByte(prefixLength + 1);
            _output.Append(";}");
        }

        private void WriteHexByte(int value) =>
            _output.Append(@"\'").Append(value.ToString("x2", CultureInfo.InvariantCulture));

        private void WriteBody()
        {
            var position = 0;
            while (true)
            {
                var lineEnd = _document.Text.IndexOf('\n', position);
                var hasParagraphBreak = lineEnd >= 0;
                if (!hasParagraphBreak)
                {
                    lineEnd = _document.Text.Length;
                }

                WriteParagraph(position, lineEnd, hasParagraphBreak);
                if (!hasParagraphBreak)
                {
                    break;
                }

                position = lineEnd + 1;
            }
        }

        private void WriteParagraph(int start, int end, bool hasParagraphBreak)
        {
            _output.Append(@"{\pard\plain");
            if (_nativeDefaultCharacterFormat is not null)
            {
                WriteNativeDefaultAppearance(_document.DefaultCharacterFormat);
            }

            var paragraphFormat = _document.GetParagraphFormat(start);
            WriteParagraphFormatControls(paragraphFormat, _document.DefaultParagraphFormat);

            if (_listsByItemStart.TryGetValue(start, out var list))
            {
                _output.Append(@"\ls").Append(list.Override.OverrideId)
                    .Append(@"\ilvl").Append(list.Format.Level)
                    .Append(' ');
                _output.Append(@"{\listtext ");
                if (list.Format.Kind == RichListKind.Bulleted)
                {
                    WriteText(list.Format.BulletText.AsSpan());
                }
                else
                {
                    var key = (list.Override.OverrideId, list.Format.Level);
                    var number = _nextListNumbers.GetValueOrDefault(
                        key,
                        list.Override.StartAtByLevel[list.Format.Level] ?? list.Format.StartAt);
                    _nextListNumbers[key] = number == int.MaxValue
                        ? number
                        : number + 1;
                    WriteText(list.Format.Prefix.AsSpan());
                    _output.Append(RichTextListFormatter.FormatNumber(number, list.Format.NumberStyle));
                    WriteText(list.Format.Suffix.AsSpan());
                }

                _output.Append(@"\tab ");
                _output.Append('}');
            }
            else
            {
                _output.Append(' ');
            }

            WriteRange(start, end);
            WriteEmptyFieldsAt(end);
            if (hasParagraphBreak)
            {
                WriteParagraphBreak(end);
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteRange(
            int start,
            int end,
            RichTextField? excludedField = null,
            RichTextLink? excludedLink = null,
            bool skipEmptyFieldsAtStart = false)
        {
            var position = start;
            while (position < end)
            {
                if (!skipEmptyFieldsAtStart || position != start)
                {
                    WriteEmptyFieldsAt(position, excludedField);
                }

                if (_fieldsByStart.TryGetValue(position, out var field) &&
                    field != excludedField && field.End <= end)
                {
                    WriteField(field, excludedLink);
                    position = field.End;
                    continue;
                }

                if (_linksByStart.TryGetValue(position, out var link) &&
                    link != excludedLink && link.End <= end)
                {
                    WriteHyperlink(link, excludedField);
                    position = link.End;
                    continue;
                }

                if (_imagesByPosition.TryGetValue(position, out var image))
                {
                    WriteImage(image);
                    position++;
                    continue;
                }

                var run = GetRunAt(position);
                var format = run.Format;
                var runEnd = Math.Min(run.End, GetNextSemanticStart(position, end));

                WriteRun(_document.Text.AsSpan(position, runEnd - position), format);
                position = runEnd;
            }
        }

        private void WriteEmptyFieldsAt(int position, RichTextField? excludedField = null)
        {
            if (!_emptyFieldsByPosition.TryGetValue(position, out var fields))
            {
                return;
            }

            foreach (var field in fields)
            {
                if (field != excludedField)
                {
                    WriteField(field);
                }
            }
        }

        private void WriteField(
            RichTextField field,
            RichTextLink? excludedLink = null)
        {
            _output.Append(@"{\field{\*\fldinst ");
            WriteText(field.Instruction.AsSpan());
            _output.Append(@"}{\fldrslt ");
            WriteRange(
                field.Start,
                field.End,
                excludedField: field,
                excludedLink: excludedLink,
                skipEmptyFieldsAtStart: true);
            _output.Append("}}");
        }

        private void WriteHyperlink(
            RichTextLink link,
            RichTextField? excludedField = null)
        {
            _output.Append("{\\field{\\*\\fldinst HYPERLINK \"");
            WriteText(link.Target.Replace("\"", "%22", StringComparison.Ordinal).AsSpan());
            _output.Append('"');
            if (!string.IsNullOrWhiteSpace(link.ToolTip))
            {
                _output.Append(" \\\\o \"");
                WriteText(link.ToolTip.Replace("\"", "'", StringComparison.Ordinal).AsSpan());
                _output.Append('"');
            }

            _output.Append(@"}{\fldrslt ");
            WriteRange(
                link.Start,
                link.End,
                excludedField: excludedField,
                excludedLink: link,
                skipEmptyFieldsAtStart: true);
            _output.Append("}}");
        }

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

        private int GetNextSemanticStart(int position, int end)
        {
            var index = Array.BinarySearch(_semanticStarts, position + 1);
            if (index < 0)
            {
                index = ~index;
            }

            return index < _semanticStarts.Length
                ? Math.Min(_semanticStarts[index], end)
                : end;
        }

        private RichTextRun GetRunAt(int position)
        {
            var low = 0;
            var high = _runs.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var run = _runs[middle];
                if (position < run.Start)
                {
                    high = middle - 1;
                }
                else if (position >= run.End)
                {
                    low = middle + 1;
                }
                else
                {
                    return run;
                }
            }

            throw new InvalidOperationException("The normalized document has no run at the requested position.");
        }

        private IEnumerable<RichTextField> SplitFields()
        {
            foreach (var field in _document.Fields)
            {
                if (field.Length == 0)
                {
                    continue;
                }

                foreach (var (start, length) in SplitSemanticRange(field.Start, field.End))
                {
                    yield return field with { Start = start, Length = length };
                }
            }
        }

        private IEnumerable<RichTextLink> SplitLinks()
        {
            foreach (var link in _document.Links)
            {
                foreach (var (start, length) in SplitSemanticRange(link.Start, link.End))
                {
                    yield return link with { Start = start, Length = length };
                }
            }
        }

        private IEnumerable<(int Start, int Length)> SplitSemanticRange(int start, int end)
        {
            while (start < end)
            {
                var newline = _document.Text.IndexOf('\n', start, end - start);
                var segmentEnd = newline < 0 ? end : newline;
                var boundaryIndex = Array.BinarySearch(_semanticBoundaries, start + 1);
                if (boundaryIndex < 0)
                {
                    boundaryIndex = ~boundaryIndex;
                }

                if (boundaryIndex < _semanticBoundaries.Length)
                {
                    segmentEnd = Math.Min(segmentEnd, _semanticBoundaries[boundaryIndex]);
                }

                if (segmentEnd > start)
                {
                    yield return (start, segmentEnd - start);
                    start = segmentEnd;
                    continue;
                }

                if (start == newline)
                {
                    start++;
                    continue;
                }

                throw new InvalidOperationException("A semantic range could not be partitioned.");
            }
        }

        private void WriteParagraphBreak(int index)
        {
            var format = GetRunAt(index).Format;
            if (!HasDirectCharacterFormatting(
                    format,
                    _document.DefaultCharacterFormat))
            {
                _output.Append(@"\par");
                return;
            }

            _output.Append('{');
            WriteFormatControls(format, _document.DefaultCharacterFormat);
            _output.Append(@"\par}");
        }

        private void WriteRun(ReadOnlySpan<char> text, RichTextCharacterFormat format)
        {
            if (!HasDirectCharacterFormatting(
                    format,
                    _document.DefaultCharacterFormat))
            {
                WriteText(text);
                return;
            }

            _output.Append('{');
            WriteFormatControls(format, _document.DefaultCharacterFormat);
            _output.Append(' ');
            WriteText(text);
            _output.Append('}');
        }

        private void WriteNativeDefaultAppearance(RichTextCharacterFormat format)
        {
            var nativeDefaultCharacterFormat = _nativeDefaultCharacterFormat ??
                throw new InvalidOperationException("A native default format is required.");
            var fontFamily = format.FontFamily ?? nativeDefaultCharacterFormat.FontFamily;
            if (fontFamily is not null)
            {
                _output.Append(@"\f").Append(_fontIndices[fontFamily]);
            }

            var fontSize = format.FontSize ?? nativeDefaultCharacterFormat.FontSize;
            if (fontSize is not null)
            {
                _output.Append(@"\fs").Append(GetHalfPointSize(fontSize.Value));
            }

            WriteColorControl(
                @"\cf",
                format.ForegroundColor ?? nativeDefaultCharacterFormat.ForegroundColor,
                null);
        }

        private void WriteFormatControls(
            RichTextCharacterFormat format,
            RichTextCharacterFormat baseline)
        {
            if (format.Bold != baseline.Bold)
            {
                _output.Append(format.Bold ? @"\b" : @"\b0");
            }

            if (format.Italic != baseline.Italic)
            {
                _output.Append(format.Italic ? @"\i" : @"\i0");
            }

            if (format.Underline != baseline.Underline)
            {
                WriteUnderlineControl(format.Underline);
            }

            var fontFamily = format.FontFamily ?? baseline.FontFamily ??
                _nativeDefaultCharacterFormat?.FontFamily;
            var baselineFontFamily = baseline.FontFamily ??
                _nativeDefaultCharacterFormat?.FontFamily;
            if ((format.FontFamily is not null ||
                 !string.Equals(fontFamily, baselineFontFamily, StringComparison.Ordinal)) &&
                fontFamily is not null)
            {
                _output.Append(@"\f").Append(_fontIndices[fontFamily]);
            }

            var fontSize = format.FontSize ?? baseline.FontSize ??
                _nativeDefaultCharacterFormat?.FontSize;
            var baselineFontSize = baseline.FontSize ?? _nativeDefaultCharacterFormat?.FontSize;
            if ((format.FontSize is not null || fontSize != baselineFontSize) &&
                fontSize is not null)
            {
                _output.Append(@"\fs").Append(GetHalfPointSize(fontSize.Value));
            }

            if (format.Script != baseline.Script)
            {
                _output.Append(format.Script switch
                {
                    RichTextScript.Superscript => @"\super",
                    RichTextScript.Subscript => @"\sub",
                    _ => @"\nosupersub",
                });
            }

            WriteColorControl(
                @"\cf",
                format.ForegroundColor ?? baseline.ForegroundColor ??
                    _nativeDefaultCharacterFormat?.ForegroundColor,
                baseline.ForegroundColor ?? _nativeDefaultCharacterFormat?.ForegroundColor,
                force: format.ForegroundColor is not null);
            WriteColorControl(@"\highlight", format.BackgroundColor, baseline.BackgroundColor);
            WriteColorControl(@"\ulc", format.UnderlineColor, baseline.UnderlineColor);

            if (format.Strikethrough != baseline.Strikethrough)
            {
                _output.Append(format.Strikethrough switch
                {
                    RichTextStrikethroughStyle.Single => @"\strike",
                    RichTextStrikethroughStyle.Double => @"\striked1",
                    _ => baseline.Strikethrough == RichTextStrikethroughStyle.Double
                        ? @"\striked0"
                        : @"\strike0",
                });
            }

            if (!format.BaselineOffset.Equals(baseline.BaselineOffset))
            {
                var halfPoints = checked((int)Math.Round(Math.Abs(format.BaselineOffset) * 2d));
                _output.Append(format.BaselineOffset < 0 ? @"\dn" : @"\up")
                    .Append(halfPoints);
            }

            if (!format.CharacterSpacing.Equals(baseline.CharacterSpacing))
            {
                var quarterPoints = checked((int)Math.Round(format.CharacterSpacing * 4d));
                _output.Append(@"\expnd").Append(quarterPoints)
                    .Append(@"\expndtw").Append(ToTwips(format.CharacterSpacing));
            }

            if (!format.HorizontalScale.Equals(baseline.HorizontalScale))
            {
                _output.Append(@"\charscalex")
                    .Append(checked((int)Math.Round(format.HorizontalScale * 100d)));
            }

            WriteToggle(@"\scaps", format.SmallCaps, baseline.SmallCaps);
            WriteToggle(@"\caps", format.AllCaps, baseline.AllCaps);
            WriteToggle(@"\outl", format.Outline, baseline.Outline);
            WriteToggle(@"\shad", format.Shadow, baseline.Shadow);
            WriteToggle(@"\v", format.Hidden, baseline.Hidden);

            if (!string.Equals(format.LanguageTag, baseline.LanguageTag, StringComparison.OrdinalIgnoreCase))
            {
                _output.Append(@"\lang").Append(GetLanguageId(format.LanguageTag));
            }

            if (format.Direction != baseline.Direction)
            {
                _output.Append(format.Direction == RichTextDirection.RightToLeft
                    ? @"\rtlch"
                    : @"\ltrch");
            }

            if (format.Kerning != baseline.Kerning)
            {
                _output.Append(@"\kerning")
                    .Append(format.Kerning == RichTextFeatureMode.Enabled ? 1 : 0);
            }

            if (format.Shading != baseline.Shading)
            {
                _output.Append(@"\chshdng").Append(format.Shading);
            }

            WriteColorControl(
                @"\chcfpat",
                format.ShadingForegroundColor,
                baseline.ShadingForegroundColor);
            WriteColorControl(
                @"\chcbpat",
                format.ShadingBackgroundColor,
                baseline.ShadingBackgroundColor);
        }

        private void WriteParagraphFormatControls(
            RichTextParagraphFormat format,
            RichTextParagraphFormat baseline)
        {
            if (format.Alignment != baseline.Alignment)
            {
                _output.Append(format.Alignment switch
                {
                    RichTextAlignment.Center => @"\qc",
                    RichTextAlignment.Right => @"\qr",
                    RichTextAlignment.Justified => @"\qj",
                    RichTextAlignment.Distributed => @"\qd",
                    _ => @"\ql",
                });
            }

            if (format.Direction != baseline.Direction)
            {
                _output.Append(format.Direction == RichTextDirection.RightToLeft
                    ? @"\rtlpar"
                    : @"\ltrpar");
            }

            WriteTwipsControl(@"\li", format.LeadingIndent, baseline.LeadingIndent);
            WriteTwipsControl(@"\ri", format.TrailingIndent, baseline.TrailingIndent);
            WriteTwipsControl(@"\fi", format.FirstLineIndent, baseline.FirstLineIndent);
            WriteTwipsControl(@"\sb", format.SpaceBefore, baseline.SpaceBefore);
            WriteTwipsControl(@"\sa", format.SpaceAfter, baseline.SpaceAfter);

            if (format.LineSpacingRule != baseline.LineSpacingRule ||
                !format.LineSpacing.Equals(baseline.LineSpacing))
            {
                var (spacing, multiple) = format.LineSpacingRule switch
                {
                    RichTextLineSpacingRule.Single => (SingleLineSpacingTwips, 1),
                    RichTextLineSpacingRule.OneAndHalf => (SingleLineSpacingTwips * 3 / 2, 1),
                    RichTextLineSpacingRule.Double => (SingleLineSpacingTwips * 2, 1),
                    RichTextLineSpacingRule.Multiple =>
                        (checked((int)Math.Round(format.LineSpacing * SingleLineSpacingTwips)), 1),
                    RichTextLineSpacingRule.AtLeast => (ToTwips(format.LineSpacing), 0),
                    RichTextLineSpacingRule.Exactly => (-ToTwips(format.LineSpacing), 0),
                    _ => (0, 0),
                };
                _output.Append(@"\sl").Append(spacing)
                    .Append(@"\slmult").Append(multiple);
            }

            if (!format.TabStops.AsSpan().SequenceEqual(baseline.TabStops.AsSpan()))
            {
                foreach (var tab in format.TabStops)
                {
                    _output.Append(tab.Alignment switch
                    {
                        RichTextTabAlignment.Center => @"\tqc",
                        RichTextTabAlignment.Right => @"\tqr",
                        RichTextTabAlignment.Decimal => @"\tqdec",
                        _ => @"\tql",
                    });
                    _output.Append(tab.Leader switch
                    {
                        RichTextTabLeader.Dots => @"\tldot",
                        RichTextTabLeader.Hyphens => @"\tlhyph",
                        RichTextTabLeader.Underline => @"\tlul",
                        RichTextTabLeader.ThickLine => @"\tlth",
                        RichTextTabLeader.Equals => @"\tleq",
                        _ => string.Empty,
                    });
                    _output.Append(@"\tx").Append(ToTwips(tab.Position));
                }
            }

            WriteToggle(@"\hyphpar", format.Hyphenation, baseline.Hyphenation);
            var shading = format.Shading;
            var shadingForeground = format.ShadingForegroundColor;
            if (format.BackgroundColor is not null && shading == 0)
            {
                shading = 10000;
                shadingForeground = format.BackgroundColor;
            }

            var baselineShading = baseline.Shading;
            var baselineShadingForeground = baseline.ShadingForegroundColor;
            if (baseline.BackgroundColor is not null && baselineShading == 0)
            {
                baselineShading = 10000;
                baselineShadingForeground = baseline.BackgroundColor;
            }

            if (shading != baselineShading)
            {
                _output.Append(@"\shading").Append(shading);
            }

            WriteColorControl(@"\cfpat", shadingForeground, baselineShadingForeground);
            WriteColorControl(
                @"\cbpat",
                format.ShadingBackgroundColor,
                baseline.ShadingBackgroundColor);

            if (format.Border != baseline.Border && format.Border is { } border)
            {
                WriteBorder(border);
            }
        }

        private void WriteBorder(RichTextBorder border)
        {
            foreach (var (side, control) in new[]
                     {
                         (RichTextBorderSides.Left, @"\brdrl"),
                         (RichTextBorderSides.Top, @"\brdrt"),
                         (RichTextBorderSides.Right, @"\brdrr"),
                         (RichTextBorderSides.Bottom, @"\brdrb"),
                     })
            {
                if (!border.Sides.HasFlag(side))
                {
                    continue;
                }

                _output.Append(control).Append(border.Style switch
                {
                    RichTextBorderStyle.Double => @"\brdrdb",
                    RichTextBorderStyle.Dotted => @"\brdrdot",
                    RichTextBorderStyle.Dashed => @"\brdrdash",
                    RichTextBorderStyle.None => @"\brdrnil",
                    _ => @"\brdrs",
                });
                _output.Append(@"\brdrw").Append(Math.Clamp(ToTwips(border.Width), 0, 255));
                if (border.Color is not null)
                {
                    _output.Append(@"\brdrcf")
                        .Append(_colorIndices[GetRtfColor(border.Color)]);
                }
            }
        }

        private void WriteUnderlineControl(RichTextUnderlineStyle underline)
        {
            _output.Append(underline switch
            {
                RichTextUnderlineStyle.None => @"\ulnone",
                RichTextUnderlineStyle.Words => @"\ulw",
                RichTextUnderlineStyle.Double => @"\uldb",
                RichTextUnderlineStyle.Dotted => @"\uld",
                RichTextUnderlineStyle.Dash => @"\uldash",
                RichTextUnderlineStyle.DashDot => @"\uldashd",
                RichTextUnderlineStyle.DashDotDot => @"\uldashdd",
                RichTextUnderlineStyle.Wave => @"\ulwave",
                RichTextUnderlineStyle.Thick => @"\ulth",
                RichTextUnderlineStyle.DoubleWave => @"\ululdbwave",
                RichTextUnderlineStyle.HeavyWave => @"\ulhwave",
                RichTextUnderlineStyle.LongDash => @"\ulldash",
                _ => @"\ul",
            });
        }

        private static bool HasDirectCharacterFormatting(
            RichTextCharacterFormat format,
            RichTextCharacterFormat baseline) =>
            format.FontFamily is not null ||
            format.FontSize is not null ||
            format.ForegroundColor is not null ||
            format with
            {
                FontFamily = baseline.FontFamily,
                FontSize = baseline.FontSize,
                ForegroundColor = baseline.ForegroundColor,
            } != baseline;

        private void WriteColorControl(
            string control,
            Color? value,
            Color? baseline,
            bool force = false)
        {
            if (!force && Equals(value, baseline))
            {
                return;
            }

            _output.Append(control).Append(value is null
                ? 0
                : _colorIndices[GetRtfColor(value)]);
        }

        private void WriteTwipsControl(
            string control,
            double value,
            double baseline)
        {
            if (!value.Equals(baseline))
            {
                _output.Append(control).Append(ToTwips(value));
            }
        }

        private void WriteToggle(string control, bool value, bool baseline)
        {
            if (value != baseline)
            {
                _output.Append(control);
                if (!value)
                {
                    _output.Append('0');
                }
            }
        }

        private void WriteText(ReadOnlySpan<char> text, bool escapeFontTerminator = false)
        {
            foreach (var character in text)
            {
                switch (character)
                {
                    case '\\':
                    case '{':
                    case '}':
                        _output.Append('\\').Append(character);
                        break;
                    case '\t':
                        _output.Append(@"\tab ");
                        break;
                    case RichTextDocument.SoftLineBreakCharacter:
                        _output.Append(@"\line ");
                        break;
                    case ';' when escapeFontTerminator:
                        WriteUnicodeCharacter(character);
                        break;
                    case >= ' ' and <= '~':
                        _output.Append(character);
                        break;
                    default:
                        WriteUnicodeCharacter(character);
                        break;
                }
            }
        }

        private void WriteUnicodeCharacter(char character) =>
            _output.Append(@"\u")
                .Append(unchecked((short)character).ToString(CultureInfo.InvariantCulture))
                .Append('?');

        private List<ListDefinition> BuildListDefinitions()
        {
            var definitions = new List<ListDefinition>();
            var definitionsById = new Dictionary<int, ListDefinition>();
            foreach (var paragraph in _document.Paragraphs)
            {
                if (paragraph.Format.NativeList is not { } list)
                {
                    continue;
                }

                if (!definitionsById.TryGetValue(list.Id, out var definition))
                {
                    definition = new ListDefinition(definitions.Count + 1, list.Id);
                    definitionsById.Add(list.Id, definition);
                    definitions.Add(definition);
                }

                definition.AddLevel(list);
            }

            var nextOverrideId = 0;
            ListOverrideDefinition AddOverride(
                ListDefinition definition,
                int?[] startAtByLevel)
            {
                nextOverrideId++;
                if (nextOverrideId > MaximumListOverrideId)
                {
                    throw new InvalidOperationException(
                        $"RTF 1.9.1 permits at most {MaximumListOverrideId} list override IDs in one document.");
                }

                var result = new ListOverrideDefinition(
                    definition,
                    nextOverrideId,
                    startAtByLevel);
                _listOverrides.Add(result);
                return result;
            }

            var activeOverrides = new Dictionary<int, ListOverrideDefinition>();
            foreach (var definition in definitions)
            {
                activeOverrides.Add(
                    definition.SourceId,
                    AddOverride(definition, new int?[9]));
            }

            var nextNumbers = new Dictionary<(int ListId, int Level), int>();
            foreach (var paragraph in _document.Paragraphs)
            {
                if (paragraph.Format.NativeList is not { } list)
                {
                    continue;
                }

                var definition = definitionsById[list.Id];
                var listOverride = activeOverrides[list.Id];
                if (list.Kind == RichListKind.Numbered && list.Restart)
                {
                    var starts = new int?[9];
                    for (var level = 0; level < starts.Length; level++)
                    {
                        if (definition.Levels[level] is { Kind: RichListKind.Numbered } levelDefinition)
                        {
                            starts[level] = nextNumbers.GetValueOrDefault(
                                (list.Id, level),
                                levelDefinition.StartAt);
                        }
                    }

                    starts[list.Level] = list.StartAt;
                    listOverride = AddOverride(definition, starts);
                    activeOverrides[list.Id] = listOverride;
                }

                _listsByItemStart.Add(
                    paragraph.Start,
                    new ListItemDefinition(listOverride, list));
                if (list.Kind == RichListKind.Numbered)
                {
                    var key = (list.Id, list.Level);
                    var number = list.Restart
                        ? list.StartAt
                        : nextNumbers.GetValueOrDefault(key, list.StartAt);
                    nextNumbers[key] = number == int.MaxValue ? number : number + 1;
                }
            }

            return definitions;
        }

        private sealed class ListDefinition(int listId, int sourceId)
        {
            public int ListId { get; } = listId;

            public int SourceId { get; } = sourceId;

            public ListLevelDefinition?[] Levels { get; } = new ListLevelDefinition?[9];

            public bool IsMultilevel => Levels[0] is null ||
                Array.FindIndex(Levels, 1, static level => level is not null) >= 1;

            public void AddLevel(RichTextListFormat format)
            {
                Levels[format.Level] ??= new ListLevelDefinition(
                    format.Kind,
                    format.StartAt,
                    format.Prefix,
                    format.Suffix,
                    format.BulletText,
                    format.PictureId,
                    format.NumberStyle switch
                    {
                        RichListNumberStyle.UpperRoman => ListNumberFormat.UpperRoman,
                        RichListNumberStyle.LowerRoman => ListNumberFormat.LowerRoman,
                        RichListNumberStyle.UpperLetter => ListNumberFormat.UpperLetter,
                        RichListNumberStyle.LowerLetter => ListNumberFormat.LowerLetter,
                        _ => ListNumberFormat.Arabic,
                    });
            }
        }

        private sealed record ListLevelDefinition(
            RichListKind Kind,
            int StartAt,
            string Prefix,
            string Suffix,
            string BulletText,
            string? PictureId,
            ListNumberFormat NumberFormat);

        private sealed class ListOverrideDefinition(
            ListDefinition definition,
            int overrideId,
            int?[] startAtByLevel)
        {
            public ListDefinition Definition { get; } = definition;

            public int OverrideId { get; } = overrideId;

            public int?[] StartAtByLevel { get; } = startAtByLevel;

            public bool HasStartOverrides =>
                Array.Exists(StartAtByLevel, static startAt => startAt is not null);
        }

        private readonly record struct ListItemDefinition(
            ListOverrideDefinition Override,
            RichTextListFormat Format);
    }

    private static bool IsBulletMarker(char character) =>
        character is '•' or '·' or '◦' or '▪' or '‣' or '⁃' or '-' or '*';

    private static bool TryParseNumberMarker(
        ReadOnlySpan<char> content,
        out NumberMarker marker,
        out int markerLength)
    {
        marker = default;
        markerLength = 0;

        var delimiterIndex = 0;
        while (delimiterIndex < content.Length && content[delimiterIndex] is not ('.' or ')'))
        {
            delimiterIndex++;
        }

        if (delimiterIndex == 0 || delimiterIndex + 1 >= content.Length ||
            !char.IsWhiteSpace(content[delimiterIndex + 1]))
        {
            return false;
        }

        var token = content[..delimiterIndex];
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var arabic) && arabic > 0)
        {
            marker = new NumberMarker(arabic, content[delimiterIndex], ListNumberFormat.Arabic);
            markerLength = delimiterIndex + 2;
            return true;
        }

        if (TryParseRoman(token, out var roman, out var romanFormat))
        {
            marker = new NumberMarker(roman, content[delimiterIndex], romanFormat);
            markerLength = delimiterIndex + 2;
            return true;
        }

        if (TryParseLetters(token, out var letters, out var letterFormat))
        {
            marker = new NumberMarker(letters, content[delimiterIndex], letterFormat);
            markerLength = delimiterIndex + 2;
            return true;
        }

        return false;
    }

    private static bool TryParseRoman(
        ReadOnlySpan<char> token,
        out int value,
        out ListNumberFormat format)
    {
        value = 0;
        format = ListNumberFormat.Arabic;
        if (token.IsEmpty)
        {
            return false;
        }

        var uppercase = true;
        var lowercase = true;
        foreach (var character in token)
        {
            uppercase &= character is 'I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M';
            lowercase &= character is 'i' or 'v' or 'x' or 'l' or 'c' or 'd' or 'm';
        }

        if (!uppercase && !lowercase)
        {
            return false;
        }

        var total = 0;
        var prior = 0;
        for (var index = token.Length - 1; index >= 0; index--)
        {
            var current = char.ToUpperInvariant(token[index]) switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => 0,
            };
            total += current < prior ? -current : current;
            prior = Math.Max(prior, current);
        }

        if (total is <= 0 or > 3999 ||
            !token.Equals(
                RichTextListFormatter.FormatNumber(
                    total,
                    uppercase
                        ? RichListNumberStyle.UpperRoman
                        : RichListNumberStyle.LowerRoman).AsSpan(),
                StringComparison.Ordinal))
        {
            return false;
        }

        value = total;
        format = uppercase ? ListNumberFormat.UpperRoman : ListNumberFormat.LowerRoman;
        return true;
    }

    private static bool TryParseLetters(
        ReadOnlySpan<char> token,
        out int value,
        out ListNumberFormat format)
    {
        value = 0;
        format = ListNumberFormat.Arabic;
        if (token.IsEmpty)
        {
            return false;
        }

        var uppercase = true;
        var lowercase = true;
        long total = 0;
        foreach (var character in token)
        {
            uppercase &= character is >= 'A' and <= 'Z';
            lowercase &= character is >= 'a' and <= 'z';
            if (!uppercase && !lowercase)
            {
                return false;
            }

            var letter = char.ToUpperInvariant(character) - 'A' + 1;
            total = total * 26 + letter;
            if (total > int.MaxValue)
            {
                return false;
            }
        }

        value = (int)total;
        format = uppercase ? ListNumberFormat.UpperLetter : ListNumberFormat.LowerLetter;
        return true;
    }

    private static string FormatListNumber(int number, ListNumberFormat format) =>
        RichTextListFormatter.FormatNumber(number, format switch
        {
            ListNumberFormat.UpperRoman => RichListNumberStyle.UpperRoman,
            ListNumberFormat.LowerRoman => RichListNumberStyle.LowerRoman,
            ListNumberFormat.UpperLetter => RichListNumberStyle.UpperLetter,
            ListNumberFormat.LowerLetter => RichListNumberStyle.LowerLetter,
            _ => RichListNumberStyle.Arabic,
        });

    private readonly record struct NumberMarker(
        int Number,
        char Delimiter,
        ListNumberFormat Format);

    private sealed class Reader
    {
        private static readonly HashSet<string> UnderlineControls = new(StringComparer.Ordinal)
        {
            "ul", "uld", "uldash", "uldashd", "uldashdd", "uldb", "ulhair", "ulhwave",
            "ulldash", "ulth", "ulthd", "ulthdash", "ulthdashd", "ulthdashdd", "ulthldash",
            "ululdbwave", "ulw", "ulwave",
        };

        private static readonly HashSet<string> SkippedDestinations = new(StringComparer.Ordinal)
        {
            "annotation", "atnauthor", "atndate", "atnicn", "atnid", "atnparent", "atnref",
            "colorschememapping", "datafield", "datastore", "docvar", "filetbl",
            "fontemb", "fontfile", "footer", "footerf", "footerl", "footerr", "footnote",
            "generator", "header", "headerf", "headerl", "headerr", "info", "latentstyles",
            "nonesttables", "nonshppict", "objalias", "objclass", "objdata", "objname",
            "objsect", "oleclsid", "private", "revtbl", "rsidtbl", "shprslt", "stylesheet",
            "themedata", "userprops", "xmlnstbl",
        };

        private readonly string _rtf;
        private readonly TextAccumulator _document = new();
        private readonly Dictionary<int, ParsedFont> _fonts = [];
        private readonly List<Color?> _colors = [];
        private readonly Dictionary<(int ListId, int Level), ParsedListDefinition> _lists = [];
        private readonly Dictionary<int, int> _listOverrides = [];
        private readonly Dictionary<(int OverrideId, int Level), int> _listOverrideStartAt = [];
        private readonly Dictionary<(bool IsTableList, int Id), int> _modelListIds = [];
        private readonly Dictionary<int, RichTextListFormat> _listItems = [];
        private readonly Dictionary<int, RichTextParagraphFormat> _paragraphs = [];
        private readonly List<RichTextLink> _links = [];
        private readonly List<RichTextField> _fields = [];
        private readonly List<RichTextImage> _images = [];
        private readonly List<RichTextListPicture> _listPictures = [];
        private readonly Dictionary<(bool IsTableList, int Id, int Level), int> _nextListNumbers = [];
        private readonly StringBuilder _fontName = new();
        private readonly List<byte> _encodedBytes = [];
        private int? _fontIndex;
        private int? _fontCharacterSet;
        private int? _fontCodePage;
        private int? _colorRed;
        private int? _colorGreen;
        private int? _colorBlue;
        private int? _pendingListId;
        private RichListKind? _pendingListKind;
        private ListNumberFormat _pendingListNumberFormat;
        private int _pendingListStartAt = 1;
        private int _pendingListLevel = -1;
        private bool _pendingListUsesModernNumberFormat;
        private string _pendingListPrefix = string.Empty;
        private string _pendingListSuffix = ".";
        private string _pendingListBulletText = "•";
        private int? _pendingListPictureIndex;
        private readonly ParsedListDefinition?[] _pendingListLevels = new ParsedListDefinition?[9];
        private int? _pendingOverrideListId;
        private int? _pendingOverrideId;
        private int _pendingOverrideLevel = -1;
        private bool _pendingOverrideStartAt;
        private int _unicodeFallbackRemaining;
        private ReaderState? _encodedState;
        private int _encodedCodePage;
        private int _documentCodePage = DefaultCodePage;
        private int _defaultFontIndex;
        private int? _defaultCharacterFontIndex;
        private RichTextCharacterFormat _defaultCharacterFormat = RichTextCharacterFormat.Default;
        private RichTextParagraphFormat _defaultParagraphFormat = RichTextParagraphFormat.Default;
        private int _lineStart;
        private bool _paragraphListHandled;
        private bool _pendingCellSeparator;
        private bool _sawRoot;
        private bool _sawRtfHeader;
        private bool _expectRtfHeader;
        private int _fallbackListId = MaximumListOverrideId + 1;
        private int _nextModelListId = 1;

        public Reader(string rtf)
        {
            _rtf = rtf;
        }

        public RichTextDocumentSnapshot Read()
        {
            var state = new ReaderState();
            var stack = new Stack<ReaderState>();
            var depth = 0;
            var position = 0;

            if (position < _rtf.Length && _rtf[position] == '\uFEFF')
            {
                position++;
            }

            while (position < _rtf.Length)
            {
                var character = _rtf[position];
                if (depth == 0)
                {
                    if (char.IsWhiteSpace(character))
                    {
                        position++;
                        continue;
                    }

                    if (_sawRoot || character != '{')
                    {
                        throw Error(position, "Expected one RTF root group.");
                    }
                }

                if (_expectRtfHeader && character is not ('\\' or '\r' or '\n'))
                {
                    throw Error(position, "The rtf1 control word must immediately follow the root opening brace.");
                }

                switch (character)
                {
                    case '{':
                        FlushEncodedBytes();
                        EndUnicodeFallbackAtGroupDelimiter();
                        var isRoot = depth == 0;
                        stack.Push(state);
                        state = state.CloneForGroup();
                        depth++;
                        if (isRoot)
                        {
                            _expectRtfHeader = true;
                        }

                        _sawRoot = true;
                        position++;
                        break;
                    case '}':
                        FlushEncodedBytes();
                        EndUnicodeFallbackAtGroupDelimiter();
                        if (depth == 0)
                        {
                            throw Error(position, "Found an unmatched closing brace.");
                        }

                        var completesDefaultCharacterProperties =
                            state.CompletesDefaultCharacterProperties;
                        var completesDefaultParagraphProperties =
                            state.CompletesDefaultParagraphProperties;
                        CompleteGroup(state);
                        state = stack.Pop();
                        if (completesDefaultCharacterProperties)
                        {
                            ResetToDefaultCharacterProperties(state);
                        }

                        if (completesDefaultParagraphProperties)
                        {
                            ResetToDefaultParagraphProperties(state);
                            TrackParagraphFormat(state);
                        }

                        depth--;
                        position++;
                        break;
                    case '\\':
                        ParseControl(ref position, state);
                        break;
                    case '\r':
                    case '\n':
                        position++;
                        break;
                    default:
                        if (character is >= (char)0x80 and <= (char)0xFF &&
                            state.CodePage != DefaultCodePage)
                        {
                            ProcessEncodedByte((byte)character, state);
                        }
                        else
                        {
                            FlushEncodedBytes();
                            ProcessLiteral(character, state);
                        }

                        state.AtGroupStart = false;
                        position++;
                        break;
                }
            }

            if (depth != 0)
            {
                throw Error(_rtf.Length, "The RTF document has an unclosed group.");
            }

            if (!_sawRoot || !_sawRtfHeader)
            {
                throw Error(0, "The document does not begin with an RTF 1 header.");
            }

            var defaultCharacterFormat = _defaultCharacterFormat with
            {
                FontFamily = GetDefaultFontFamily(),
                FontSize = GetDefaultFontSize(),
            };
            var text = _document.Text;
            return new RichTextDocumentSnapshot(
                text,
                _document.Runs,
                EnumerateParagraphs(text),
                links: CoalesceLinks(_links),
                fields: CoalesceFields(_fields),
                images: _images,
                defaultCharacterFormat: defaultCharacterFormat,
                defaultParagraphFormat: _defaultParagraphFormat,
                listPictures: _listPictures);
        }

        private IEnumerable<RichTextParagraph> EnumerateParagraphs(string text)
        {
            for (var start = 0; ;)
            {
                var format = _paragraphs.GetValueOrDefault(start, _defaultParagraphFormat);
                _listItems.TryGetValue(start, out var list);
                yield return new RichTextParagraph(start, format with { NativeList = list });

                var newline = text.IndexOf('\n', start);
                if (newline < 0)
                {
                    yield break;
                }

                start = newline + 1;
            }
        }

        private static IReadOnlyList<RichTextLink> CoalesceLinks(
            IEnumerable<RichTextLink> source)
        {
            var result = new List<RichTextLink>();
            foreach (var link in source.OrderBy(link => link.Start).ThenBy(link => link.End))
            {
                if (result.Count > 0 && result[^1] is { } previous &&
                    previous.End == link.Start &&
                    string.Equals(previous.Target, link.Target, StringComparison.Ordinal) &&
                    string.Equals(previous.ToolTip, link.ToolTip, StringComparison.Ordinal))
                {
                    result[^1] = previous with { Length = link.End - previous.Start };
                }
                else
                {
                    result.Add(link);
                }
            }

            return result;
        }

        private static IReadOnlyList<RichTextField> CoalesceFields(
            IEnumerable<RichTextField> source)
        {
            var result = new List<RichTextField>();
            foreach (var field in source.OrderBy(field => field.Start).ThenBy(field => field.End))
            {
                if (result.Count > 0 && result[^1] is { } previous &&
                    previous.End == field.Start &&
                    string.Equals(previous.Instruction, field.Instruction, StringComparison.Ordinal))
                {
                    result[^1] = previous with { Length = field.End - previous.Start };
                }
                else
                {
                    result.Add(field);
                }
            }

            return result;
        }

        private void ParseControl(ref int position, ReaderState state)
        {
            var controlStart = position;
            position++;
            if (position >= _rtf.Length)
            {
                throw Error(controlStart, "A control sequence is incomplete.");
            }

            var symbol = _rtf[position];
            if (!char.IsAsciiLetter(symbol))
            {
                position++;
                if (symbol == '\'')
                {
                    if (position + 1 >= _rtf.Length ||
                        !byte.TryParse(
                            _rtf.AsSpan(position, 2),
                            NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture,
                            out var value))
                    {
                        throw Error(controlStart, "An RTF hexadecimal escape must contain two hexadecimal digits.");
                    }

                    position += 2;
                    if (!ConsumeUnicodeFallback())
                    {
                        ProcessEncodedByte(value, state);
                    }

                    state.AtGroupStart = false;
                    return;
                }

                FlushEncodedBytes();
                if (_expectRtfHeader)
                {
                    throw Error(controlStart, "The rtf1 control word must immediately follow the root opening brace.");
                }

                if (ConsumeUnicodeFallback())
                {
                    state.AtGroupStart = false;
                    return;
                }

                ProcessControlSymbol(symbol, state);
                return;
            }

            var wordStart = position;
            while (position < _rtf.Length && char.IsAsciiLetter(_rtf[position]))
            {
                position++;
            }

            var word = _rtf[wordStart..position];
            if (word.Length > 32)
            {
                throw Error(controlStart, "An RTF control word cannot be longer than 32 letters.");
            }

            int? parameter = null;
            var parameterStart = position;
            if (position < _rtf.Length && _rtf[position] == '-' &&
                position + 1 < _rtf.Length && char.IsAsciiDigit(_rtf[position + 1]))
            {
                position++;
            }

            var digitStart = position;
            while (position < _rtf.Length && char.IsAsciiDigit(_rtf[position]))
            {
                if (position - digitStart == 10)
                {
                    throw Error(controlStart, "An RTF numeric parameter cannot contain more than 10 digits.");
                }

                position++;
            }

            if (position > digitStart)
            {
                if (!int.TryParse(
                        _rtf.AsSpan(parameterStart, position - parameterStart),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    throw Error(controlStart, "An RTF numeric parameter is outside the supported range.");
                }

                parameter = value;
            }

            if (position < _rtf.Length && _rtf[position] == ' ')
            {
                position++;
            }

            FlushEncodedBytes();
            if (_expectRtfHeader)
            {
                if (word != "rtf" || parameter != 1)
                {
                    throw Error(controlStart, "The rtf1 control word must immediately follow the root opening brace.");
                }

                _expectRtfHeader = false;
            }

            if (word == "bin")
            {
                var byteCount = parameter ?? 0;
                if (byteCount < 0)
                {
                    throw Error(controlStart, "The bin control requires a non-negative byte count.");
                }

                if (byteCount > _rtf.Length - position)
                {
                    throw Error(controlStart, "The bin control contains fewer bytes than its declared byte count.");
                }

                for (var index = 0; index < byteCount; index++)
                {
                    if (_rtf[position + index] > byte.MaxValue)
                    {
                        throw Error(controlStart, "Binary RTF data must consist of 8-bit values.");
                    }
                }

                _ = ConsumeUnicodeFallback();
                if (state.Destination == Destination.Picture && state.Picture is { } picture)
                {
                    for (var index = 0; index < byteCount; index++)
                    {
                        picture.AppendByte((byte)_rtf[position + index]);
                    }
                }

                position += byteCount;
                state.AtGroupStart = false;
                return;
            }

            if (ConsumeUnicodeFallback())
            {
                state.AtGroupStart = false;
                return;
            }

            ApplyControl(word, parameter, state, controlStart);
        }

        private void ProcessControlSymbol(char symbol, ReaderState state)
        {
            if (symbol == '*' && state.AtGroupStart)
            {
                state.IgnorableDestination = true;
                return;
            }

            state.AtGroupStart = false;
            if (state.SkipDestination)
            {
                return;
            }

            switch (symbol)
            {
                case '\\':
                case '{':
                case '}':
                    ProcessDecodedCharacter(symbol, state);
                    break;
                case '~':
                    ProcessDecodedCharacter('\u00A0', state);
                    break;
                case '-':
                    ProcessDecodedCharacter('\u00AD', state);
                    break;
                case '_':
                    ProcessDecodedCharacter('\u2011', state);
                    break;
                case ' ':
                    ProcessDecodedCharacter(' ', state);
                    break;
                case '\r':
                case '\n':
                    AppendParagraphBreak(state);
                    break;
            }
        }

        private void ApplyControl(string word, int? parameter, ReaderState state, int controlStart)
        {
            if (state.AtGroupStart && !state.SkipDestination && TrySetDestination(word, state))
            {
                state.AtGroupStart = false;
                return;
            }

            if (state.AtGroupStart && state.IgnorableDestination)
            {
                state.SkipDestination = true;
            }

            state.AtGroupStart = false;
            if (state.SkipDestination)
            {
                return;
            }

            if (word == "uc")
            {
                var skipCount = parameter ?? 0;
                if (skipCount < 0)
                {
                    throw Error(controlStart, "The uc control requires a non-negative character count.");
                }

                state.UnicodeSkipCount = skipCount;
                return;
            }

            if (word == "u")
            {
                if (parameter is null or < -32768 or > 65535)
                {
                    throw Error(controlStart, "The u control requires one UTF-16 code unit.");
                }

                ProcessDecodedCharacter(unchecked((char)(ushort)parameter.Value), state);
                _unicodeFallbackRemaining = state.UnicodeSkipCount;
                return;
            }

            switch (state.Destination)
            {
                case Destination.FontTable:
                    ApplyFontTableControl(word, parameter, state);
                    return;
                case Destination.ColorTable:
                    ApplyColorTableControl(word, parameter, controlStart);
                    return;
                case Destination.ListTable:
                    ApplyListTableControl(word, parameter);
                    return;
                case Destination.ListOverrideTable:
                    ApplyListOverrideControl(word, parameter);
                    return;
                case Destination.Picture:
                    ApplyPictureControl(word, parameter, state.Picture!);
                    return;
            }

            if (word == "rtf")
            {
                if (parameter != 1)
                {
                    throw Error(controlStart, "Only the RTF 1 syntax used by RTF 1.9.1 is supported.");
                }

                _sawRtfHeader = true;
                return;
            }

            if (word == "ansi")
            {
                SetDocumentCodePage(state, DefaultCodePage);
                return;
            }

            if (word == "mac")
            {
                SetDocumentCodePage(state, 10000);
                return;
            }

            if (word == "pc")
            {
                SetDocumentCodePage(state, 437);
                return;
            }

            if (word == "pca")
            {
                SetDocumentCodePage(state, 850);
                return;
            }

            if (word == "ansicpg" && parameter is > 0)
            {
                SetDocumentCodePage(state, parameter.Value);
                return;
            }

            if (word == "deff")
            {
                _defaultFontIndex = parameter ?? 0;
                return;
            }

            if (word == "plain")
            {
                ResetToDefaultCharacterProperties(state);
                return;
            }

            if (TryApplyParagraphControl(word, parameter, state))
            {
                return;
            }

            if (word == "ls")
            {
                var overrideId = parameter ?? 0;
                state.ListOverride = overrideId is >= 1 and <= MaximumListOverrideId ? overrideId : 0;
                return;
            }

            if (word == "ilvl")
            {
                state.ListLevel = parameter ?? 0;
                return;
            }

            if (word == "b")
            {
                state.Format = state.Format with { FontWeight = parameter == 0 ? 400 : 700 };
                return;
            }

            if (word == "i")
            {
                state.Format = state.Format with { Italic = parameter != 0 };
                return;
            }

            if (word == "ulnone" || UnderlineControls.Contains(word))
            {
                state.Format = state.Format with
                {
                    Underline = word == "ulnone" || parameter == 0
                        ? RichTextUnderlineStyle.None
                        : GetUnderlineStyle(word),
                };
                return;
            }

            if (word == "ulc")
            {
                state.Format = state.Format with { UnderlineColor = ResolveColor(parameter ?? 0) };
                return;
            }

            if (word == "strike")
            {
                state.Format = state.Format with
                {
                    Strikethrough = parameter == 0
                        ? RichTextStrikethroughStyle.None
                        : RichTextStrikethroughStyle.Single,
                };
                return;
            }

            if (word == "striked")
            {
                state.Format = state.Format with
                {
                    Strikethrough = parameter == 0
                        ? RichTextStrikethroughStyle.None
                        : RichTextStrikethroughStyle.Double,
                };
                return;
            }

            if (word == "f")
            {
                var fontIndex = parameter ?? 0;
                _fonts.TryGetValue(fontIndex, out var font);
                state.FontIndex = fontIndex;
                state.Format = state.Format with
                {
                    FontFamily = font.Name,
                };
                state.CodePage = font.CodePage > 0 ? font.CodePage : _documentCodePage;
                return;
            }

            if (word == "fs" && parameter is > 0)
            {
                state.Format = state.Format with { FontSize = parameter.Value / 2d };
                return;
            }

            if (word == "super")
            {
                state.Format = state.Format with
                {
                    Script = parameter == 0
                        ? RichTextScript.Normal
                        : RichTextScript.Superscript,
                };
                return;
            }

            if (word == "sub")
            {
                state.Format = state.Format with
                {
                    Script = parameter == 0
                        ? RichTextScript.Normal
                        : RichTextScript.Subscript,
                };
                return;
            }

            if (word == "nosupersub")
            {
                state.Format = state.Format with { Script = RichTextScript.Normal };
                return;
            }

            if (word is "up" or "dn")
            {
                var halfPoints = parameter ?? 6;
                state.Format = state.Format with
                {
                    BaselineOffset = (word == "dn" ? -halfPoints : halfPoints) / 2d,
                };
                return;
            }

            if (word is "expnd" or "expndtw")
            {
                state.Format = state.Format with
                {
                    CharacterSpacing = word == "expndtw"
                        ? (parameter ?? 0) / TwipsPerPoint
                        : (parameter ?? 0) / 4d,
                };
                return;
            }

            if (word == "charscalex" && parameter is > 0)
            {
                state.Format = state.Format with { HorizontalScale = parameter.Value / 100d };
                return;
            }

            if (word is "scaps" or "caps" or "outl" or "shad" or "v")
            {
                var enabled = parameter != 0;
                state.Format = word switch
                {
                    "scaps" => state.Format with { SmallCaps = enabled },
                    "caps" => state.Format with { AllCaps = enabled },
                    "outl" => state.Format with { Outline = enabled },
                    "shad" => state.Format with { Shadow = enabled },
                    _ => state.Format with { Hidden = enabled },
                };
                return;
            }

            if (word is "lang" or "langnp" or "langfe" or "langfenp")
            {
                state.Format = state.Format with { LanguageTag = GetLanguageTag(parameter ?? 0) };
                return;
            }

            if (word is "ltrch" or "rtlch")
            {
                state.Format = state.Format with
                {
                    Direction = word == "rtlch"
                        ? RichTextDirection.RightToLeft
                        : RichTextDirection.LeftToRight,
                };
                return;
            }

            if (word == "kerning")
            {
                state.Format = state.Format with
                {
                    Kerning = parameter == 0
                        ? RichTextFeatureMode.Disabled
                        : RichTextFeatureMode.Enabled,
                };
                return;
            }

            if (word == "cf")
            {
                state.Format = state.Format with { ForegroundColor = ResolveColor(parameter ?? 0) };
                return;
            }

            if (word is "highlight" or "cb")
            {
                state.Format = state.Format with { BackgroundColor = ResolveColor(parameter ?? 0) };
                return;
            }

            if (word == "chshdng")
            {
                var shading = Math.Clamp(parameter ?? 0, 0, 10000);
                state.CharacterShading = shading;
                state.Format = state.Format with
                {
                    Shading = shading,
                    BackgroundColor = shading == 10000
                        ? state.Format.ShadingForegroundColor
                        : state.Format.BackgroundColor,
                };
                return;
            }

            if (word == "chcfpat")
            {
                var color = ResolveColor(parameter ?? 0);
                state.Format = state.Format with
                {
                    ShadingForegroundColor = color,
                    BackgroundColor = state.CharacterShading == 10000
                        ? color
                        : state.Format.BackgroundColor,
                };
                return;
            }

            if (word == "chcbpat")
            {
                var color = ResolveColor(parameter ?? 0);
                state.Format = state.Format with
                {
                    ShadingBackgroundColor = color,
                    BackgroundColor = state.CharacterShading == 0
                        ? color
                        : state.Format.BackgroundColor,
                };
                return;
            }

            if (word == "par")
            {
                AppendParagraphBreak(state);
                return;
            }

            if (word is "sect" or "page" or "column" or "softpage" or "softcol")
            {
                AppendParagraphBreak(state);
                return;
            }

            if (word is "line" or "softline")
            {
                ProcessDecodedCharacter(RichTextDocument.SoftLineBreakCharacter, state);
                return;
            }

            if (word == "tab")
            {
                ProcessDecodedCharacter('\t', state);
                return;
            }

            if (word == "objattph")
            {
                AppendFallbackText("[Attachment]", state);
                return;
            }

            if (word is "cell" or "nestcell")
            {
                EndTableCell(state);
                return;
            }

            if (word is "row" or "nestrow")
            {
                EndTableRow(state);
                return;
            }

            var specialCharacter = word switch
            {
                "bullet" => '•',
                "emdash" => '—',
                "endash" => '–',
                "lquote" => '‘',
                "rquote" => '’',
                "ldblquote" => '“',
                "rdblquote" => '”',
                "emspace" => '\u2003',
                "enspace" => '\u2002',
                "qmspace" => '\u2005',
                "ltrmark" => '\u200E',
                "rtlmark" => '\u200F',
                "zwbo" => '\u200B',
                "zwnbo" => '\u2060',
                "zwj" => '\u200D',
                "zwnj" => '\u200C',
                _ => '\0',
            };
            if (specialCharacter != '\0')
            {
                ProcessDecodedCharacter(specialCharacter, state);
            }
        }

        private bool TryApplyParagraphControl(
            string word,
            int? parameter,
            ReaderState state)
        {
            if (word == "pard")
            {
                state.ParagraphFormat = state.Destination == Destination.DefaultParagraphProperties
                    ? RichTextParagraphFormat.Default
                    : _defaultParagraphFormat;
                state.ListOverride = 0;
                state.ListLevel = 0;
                ResetParagraphControlState(state);
                TrackParagraphFormat(state);
                return true;
            }

            if (word is "ql" or "qc" or "qr" or "qj" or "qd")
            {
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Alignment = word switch
                    {
                        "qc" => RichTextAlignment.Center,
                        "qr" => RichTextAlignment.Right,
                        "qj" => RichTextAlignment.Justified,
                        "qd" => RichTextAlignment.Distributed,
                        _ => RichTextAlignment.Left,
                    },
                });
                return true;
            }

            if (word is "ltrpar" or "rtlpar")
            {
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Direction = word == "rtlpar"
                        ? RichTextDirection.RightToLeft
                        : RichTextDirection.LeftToRight,
                });
                return true;
            }

            if (word is "li" or "lin" or "ri" or "rin" or "fi" or "sb" or "sa")
            {
                var points = FromTwips(parameter ?? 0);
                SetParagraphFormat(state, word switch
                {
                    "li" or "lin" => state.ParagraphFormat with { LeadingIndent = points },
                    "ri" or "rin" => state.ParagraphFormat with { TrailingIndent = points },
                    "fi" => state.ParagraphFormat with { FirstLineIndent = points },
                    "sb" => state.ParagraphFormat with { SpaceBefore = Math.Max(points, 0) },
                    _ => state.ParagraphFormat with { SpaceAfter = Math.Max(points, 0) },
                });
                return true;
            }

            if (word == "hyphpar")
            {
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Hyphenation = parameter != 0,
                });
                return true;
            }

            if (word is "sl" or "slmult")
            {
                if (word == "sl")
                {
                    state.ParagraphLineSpacingTwips = parameter ?? 0;
                }
                else
                {
                    state.ParagraphLineSpacingMultiple = parameter != 0;
                }

                ApplyParagraphLineSpacing(state);
                return true;
            }

            if (word is "tql" or "tqc" or "tqr" or "tqdec")
            {
                state.PendingTabAlignment = word switch
                {
                    "tqc" => RichTextTabAlignment.Center,
                    "tqr" => RichTextTabAlignment.Right,
                    "tqdec" => RichTextTabAlignment.Decimal,
                    _ => RichTextTabAlignment.Left,
                };
                return true;
            }

            if (word is "tldot" or "tlhyph" or "tlul" or "tlth" or "tleq")
            {
                state.PendingTabLeader = word switch
                {
                    "tldot" => RichTextTabLeader.Dots,
                    "tlhyph" => RichTextTabLeader.Hyphens,
                    "tlul" => RichTextTabLeader.Underline,
                    "tlth" => RichTextTabLeader.ThickLine,
                    _ => RichTextTabLeader.Equals,
                };
                return true;
            }

            if (word is "tx" or "tb")
            {
                var position = FromTwips(parameter ?? 0);
                if (position > 0)
                {
                    var tab = new RichTextTabStop(
                        position,
                        state.PendingTabAlignment,
                        state.PendingTabLeader);
                    SetParagraphFormat(state, state.ParagraphFormat with
                    {
                        TabStops = state.ParagraphFormat.TabStops.Add(tab),
                    });
                }

                state.PendingTabAlignment = RichTextTabAlignment.Left;
                state.PendingTabLeader = RichTextTabLeader.None;
                return true;
            }

            if (word == "shading")
            {
                var shading = Math.Clamp(parameter ?? 0, 0, 10000);
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Shading = shading,
                    BackgroundColor = shading == 10000
                        ? state.ParagraphFormat.ShadingForegroundColor
                        : state.ParagraphFormat.BackgroundColor,
                });
                return true;
            }

            if (word == "cfpat")
            {
                var color = ResolveColor(parameter ?? 0);
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    ShadingForegroundColor = color,
                    BackgroundColor = state.ParagraphFormat.Shading == 10000
                        ? color
                        : state.ParagraphFormat.BackgroundColor,
                });
                return true;
            }

            if (word == "cbpat")
            {
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    ShadingBackgroundColor = ResolveColor(parameter ?? 0),
                });
                return true;
            }

            var borderSides = word switch
            {
                "brdrl" => RichTextBorderSides.Left,
                "brdrt" => RichTextBorderSides.Top,
                "brdrr" => RichTextBorderSides.Right,
                "brdrb" => RichTextBorderSides.Bottom,
                "box" => RichTextBorderSides.All,
                _ => RichTextBorderSides.None,
            };
            if (borderSides != RichTextBorderSides.None)
            {
                state.CurrentBorderSides = borderSides;
                var current = state.ParagraphFormat.Border;
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Border = new RichTextBorder(
                        (current?.Sides ?? RichTextBorderSides.None) | borderSides,
                        current?.Style ?? RichTextBorderStyle.Single,
                        current?.Width ?? 0,
                        current?.Color),
                });
                return true;
            }

            if (word is "brdrs" or "brdrdb" or "brdrdot" or "brdrdash" or "brdrnil" or
                "brdrnone")
            {
                if (word is "brdrnil" or "brdrnone")
                {
                    SetParagraphFormat(state, state.ParagraphFormat with { Border = null });
                }
                else if (state.ParagraphFormat.Border is { } border)
                {
                    SetParagraphFormat(state, state.ParagraphFormat with
                    {
                        Border = border with
                        {
                            Style = word switch
                            {
                                "brdrdb" => RichTextBorderStyle.Double,
                                "brdrdot" => RichTextBorderStyle.Dotted,
                                "brdrdash" => RichTextBorderStyle.Dashed,
                                _ => RichTextBorderStyle.Single,
                            },
                        },
                    });
                }

                return true;
            }

            if (word == "brdrw" && state.ParagraphFormat.Border is { } widthBorder)
            {
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Border = widthBorder with { Width = Math.Max(FromTwips(parameter ?? 0), 0) },
                });
                return true;
            }

            if (word == "brdrcf" && state.ParagraphFormat.Border is { } colorBorder)
            {
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    Border = colorBorder with { Color = ResolveColor(parameter ?? 0) },
                });
                return true;
            }

            return false;
        }

        private void ApplyParagraphLineSpacing(ReaderState state)
        {
            var twips = state.ParagraphLineSpacingTwips;
            RichTextLineSpacingRule rule;
            double spacing;
            if (twips == 0)
            {
                rule = RichTextLineSpacingRule.Automatic;
                spacing = 0;
            }
            else if (state.ParagraphLineSpacingMultiple)
            {
                rule = twips switch
                {
                    SingleLineSpacingTwips => RichTextLineSpacingRule.Single,
                    SingleLineSpacingTwips * 3 / 2 => RichTextLineSpacingRule.OneAndHalf,
                    SingleLineSpacingTwips * 2 => RichTextLineSpacingRule.Double,
                    _ => RichTextLineSpacingRule.Multiple,
                };
                spacing = rule == RichTextLineSpacingRule.Multiple
                    ? (double)twips / SingleLineSpacingTwips
                    : 0;
            }
            else
            {
                rule = twips < 0
                    ? RichTextLineSpacingRule.Exactly
                    : RichTextLineSpacingRule.AtLeast;
                spacing = Math.Abs(FromTwips(twips));
            }

            SetParagraphFormat(state, state.ParagraphFormat with
            {
                LineSpacingRule = rule,
                LineSpacing = spacing,
            });
        }

        private void SetParagraphFormat(ReaderState state, RichTextParagraphFormat format)
        {
            state.ParagraphFormat = format;
            TrackParagraphFormat(state);
        }

        private static double FromTwips(int twips) => twips / TwipsPerPoint;

        private static RichTextUnderlineStyle GetUnderlineStyle(string word) => word switch
        {
            "ulw" => RichTextUnderlineStyle.Words,
            "uldb" => RichTextUnderlineStyle.Double,
            "uld" or "ulthd" => RichTextUnderlineStyle.Dotted,
            "uldash" or "ulthdash" => RichTextUnderlineStyle.Dash,
            "uldashd" or "ulthdashd" => RichTextUnderlineStyle.DashDot,
            "uldashdd" or "ulthdashdd" => RichTextUnderlineStyle.DashDotDot,
            "ulwave" => RichTextUnderlineStyle.Wave,
            "ulth" => RichTextUnderlineStyle.Thick,
            "ululdbwave" => RichTextUnderlineStyle.DoubleWave,
            "ulhwave" => RichTextUnderlineStyle.HeavyWave,
            "ulldash" or "ulthldash" => RichTextUnderlineStyle.LongDash,
            _ => RichTextUnderlineStyle.Single,
        };

        private static string? GetLanguageTag(int languageId)
        {
            if (languageId is 0 or 1024)
            {
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(languageId).Name;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        private bool TrySetDestination(string word, ReaderState state)
        {
            switch (word)
            {
                case "fonttbl":
                    state.Destination = Destination.FontTable;
                    return true;
                case "colortbl":
                    state.Destination = Destination.ColorTable;
                    return true;
                case "defchp":
                    state.Destination = Destination.DefaultCharacterProperties;
                    state.Format = RichTextCharacterFormat.Default;
                    state.FontIndex = null;
                    state.CharacterShading = null;
                    state.CompletesDefaultCharacterProperties = true;
                    return true;
                case "defpap":
                    state.Destination = Destination.DefaultParagraphProperties;
                    state.ParagraphFormat = RichTextParagraphFormat.Default;
                    ResetParagraphControlState(state);
                    state.CompletesDefaultParagraphProperties = true;
                    return true;
                case "listtable":
                    state.Destination = Destination.ListTable;
                    return true;
                case "listoverridetable":
                    state.Destination = Destination.ListOverrideTable;
                    return true;
                case "listpicture" when state.Destination == Destination.ListTable:
                    state.Destination = Destination.ListPictures;
                    return true;
                case "leveltext" when state.Destination == Destination.ListTable:
                    state.Destination = Destination.ListLevelText;
                    state.ListLevelTextCapture = new StringBuilder();
                    state.CompletesListLevelText = true;
                    return true;
                case "picprop" when state.Destination == Destination.Picture:
                    state.Destination = Destination.PictureProperties;
                    return true;
                case "sn" when state.Destination == Destination.PictureProperties:
                    state.Destination = Destination.PicturePropertyName;
                    state.PicturePropertyCapture = new StringBuilder();
                    state.CompletesPicturePropertyName = true;
                    return true;
                case "sv" when state.Destination == Destination.PictureProperties:
                    state.Destination = Destination.PicturePropertyValue;
                    state.PicturePropertyCapture = new StringBuilder();
                    state.CompletesPicturePropertyValue = true;
                    return true;
                case "listtext":
                case "pntext":
                    state.Destination = Destination.ListText;
                    state.Capture = new TextAccumulator();
                    state.CompletesCapture = true;
                    return true;
                case "field":
                    state.Field = new FieldContext();
                    return true;
                case "fldinst":
                    state.Destination = Destination.FieldInstruction;
                    state.FieldInstructionCapture = new StringBuilder();
                    state.CompletesFieldInstruction = true;
                    return true;
                case "fldrslt":
                    state.Destination = Destination.Body;
                    state.FieldResultStart = _document.Length;
                    state.CompletesFieldResult = true;
                    return true;
                case "object":
                    state.Destination = Destination.Object;
                    state.Object = new ObjectContext(
                        _document.Length,
                        state.Format,
                        state.ParagraphFormat,
                        state.ListOverride,
                        state.ListLevel);
                    state.CompletesObject = true;
                    return true;
                case "result" when state.Object is not null:
                    state.Destination = Destination.Body;
                    return true;
                case "shp":
                case "shpgrp":
                    state.Destination = Destination.Shape;
                    return true;
                case "shpinst" when state.Destination == Destination.Shape:
                    state.Destination = Destination.ShapeInstructions;
                    return true;
                case "sp" when state.Destination == Destination.ShapeInstructions:
                    state.Destination = Destination.ShapeProperty;
                    return true;
                case "sn" when state.Destination == Destination.ShapeProperty:
                    state.Destination = Destination.ShapePropertyName;
                    return true;
                case "sv" when state.Destination == Destination.ShapeProperty:
                    state.Destination = Destination.ShapePropertyValue;
                    return true;
                case "shptxt" when state.Destination is
                    Destination.Shape or Destination.ShapeInstructions:
                    state.Destination = Destination.Body;
                    return true;
                case "pict":
                    var isListPicture = state.Destination == Destination.ListPictures;
                    state.Destination = Destination.Picture;
                    state.Picture = new PictureContext(
                        state.Format,
                        state.ParagraphFormat,
                        state.ListOverride,
                        state.ListLevel,
                        isListPicture);
                    state.CompletesPicture = true;
                    return true;
                case "shppict":
                    // The ignorable wrapper contains the preferred Word 97+ picture.
                    return true;
                default:
                    if (SkippedDestinations.Contains(word))
                    {
                        state.Destination = Destination.Skip;
                        state.SkipDestination = true;
                        return true;
                    }

                    return false;
            }
        }

        private void ApplyFontTableControl(string word, int? parameter, ReaderState state)
        {
            if (word == "f")
            {
                var index = parameter ?? 0;
                if (index < 0)
                {
                    return;
                }

                _fontIndex = index;
                _fontCharacterSet = null;
                _fontCodePage = null;
                _fontName.Clear();
                state.CodePage = _documentCodePage;
                return;
            }

            if (word == "fcharset")
            {
                _fontCharacterSet = parameter ?? 0;
                state.CodePage = GetCodePageForCharacterSet(_fontCharacterSet.Value) ?? _documentCodePage;
                return;
            }

            if (word == "cpg" && parameter is > 0)
            {
                _fontCodePage = parameter.Value;
                state.CodePage = parameter.Value;
            }
        }

        private void ApplyColorTableControl(string word, int? parameter, int controlStart)
        {
            if (word is not ("red" or "green" or "blue"))
            {
                return;
            }

            var component = parameter ?? 0;
            if (component is < 0 or > 255)
            {
                throw Error(controlStart, $"The {word} control requires an 8-bit color component.");
            }

            if (word == "red")
            {
                _colorRed = component;
            }
            else if (word == "green")
            {
                _colorGreen = component;
            }
            else
            {
                _colorBlue = component;
            }
        }

        private void ApplyListTableControl(string word, int? parameter)
        {
            if (word == "list")
            {
                _pendingListId = null;
                _pendingListLevel = -1;
                Array.Clear(_pendingListLevels);
                ResetPendingListLevel();
                return;
            }

            if (word == "listlevel")
            {
                CommitPendingListLevel();
                _pendingListLevel++;
                ResetPendingListLevel();
                return;
            }

            if (word == "listid" && parameter is not null)
            {
                _pendingListId = parameter;
            }
            else if (word == "levelnfc" && parameter is not null &&
                     !_pendingListUsesModernNumberFormat)
            {
                SetPendingListNumberFormat(parameter.Value);
            }
            else if (word == "levelnfcn" && parameter is not null)
            {
                SetPendingListNumberFormat(parameter.Value);
                _pendingListUsesModernNumberFormat = true;
            }
            else if (word == "levelstartat" && parameter is > 0)
            {
                _pendingListStartAt = parameter.Value;
            }
            else if (word == "levelpicture" && parameter is >= 0)
            {
                _pendingListPictureIndex = parameter.Value;
            }

            CommitListDefinition();
        }

        private void CommitListDefinition()
        {
            CommitPendingListLevel();
            if (_pendingListId is not { } listId)
            {
                return;
            }

            for (var level = 0; level < _pendingListLevels.Length; level++)
            {
                if (_pendingListLevels[level] is { } definition)
                {
                    _lists[(listId, level)] = definition;
                }
            }
        }

        private void CommitPendingListLevel()
        {
            if (_pendingListLevel is >= 0 and < 9 && _pendingListKind is { } kind)
            {
                _pendingListLevels[_pendingListLevel] = new ParsedListDefinition(
                    kind,
                    _pendingListStartAt,
                    _pendingListNumberFormat,
                    _pendingListPrefix,
                    _pendingListSuffix,
                    _pendingListBulletText,
                    _pendingListPictureIndex);
            }
        }

        private void ResetPendingListLevel()
        {
            _pendingListKind = null;
            _pendingListNumberFormat = ListNumberFormat.Arabic;
            _pendingListStartAt = 1;
            _pendingListUsesModernNumberFormat = false;
            _pendingListPrefix = string.Empty;
            _pendingListSuffix = ".";
            _pendingListBulletText = "•";
            _pendingListPictureIndex = null;
        }

        private void CompleteListLevelText(StringBuilder capture)
        {
            if (_pendingListLevel is < 0 or >= 9 || capture.Length == 0)
            {
                return;
            }

            var declaredLength = Math.Min(capture[0], capture.Length - 1);
            var text = capture.ToString(1, declaredLength);
            var placeholderIndex = text.IndexOfAny(
                ['\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007', '\b']);
            if (placeholderIndex >= 0)
            {
                _pendingListPrefix = text[..placeholderIndex];
                _pendingListSuffix = text[(placeholderIndex + 1)..];
            }
            else if (_pendingListKind == RichListKind.Bulleted && text.Length > 0)
            {
                _pendingListBulletText = text;
                _pendingListSuffix = string.Empty;
            }

            CommitPendingListLevel();
            CommitListDefinition();
        }

        private void SetPendingListNumberFormat(int numberFormat)
        {
            if (numberFormat == 23)
            {
                _pendingListKind = RichListKind.Bulleted;
                _pendingListNumberFormat = ListNumberFormat.Arabic;
                return;
            }

            if (numberFormat == 255)
            {
                _pendingListKind = null;
                return;
            }

            _pendingListKind = RichListKind.Numbered;
            _pendingListNumberFormat = Enum.IsDefined((ListNumberFormat)numberFormat)
                ? (ListNumberFormat)numberFormat
                : ListNumberFormat.Arabic;
        }

        private void ApplyListOverrideControl(string word, int? parameter)
        {
            if (word == "listoverride")
            {
                _pendingOverrideListId = null;
                _pendingOverrideId = null;
                _pendingOverrideLevel = -1;
                _pendingOverrideStartAt = false;
                return;
            }

            if (word == "lfolevel")
            {
                _pendingOverrideLevel++;
                _pendingOverrideStartAt = false;
                return;
            }

            if (word == "listid" && parameter is not null)
            {
                _pendingOverrideListId = parameter;
            }
            else if (word == "ls" && parameter is >= 1 and <= MaximumListOverrideId)
            {
                _pendingOverrideId = parameter;
            }
            else if (word == "listoverridestartat")
            {
                _pendingOverrideStartAt = true;
            }
            else if (word == "levelstartat" &&
                     parameter is > 0 &&
                     _pendingOverrideStartAt &&
                     _pendingOverrideId is { } startOverrideId &&
                     _pendingOverrideLevel is >= 0 and < 9)
            {
                _listOverrideStartAt[(startOverrideId, _pendingOverrideLevel)] = parameter.Value;
            }

            if (_pendingOverrideListId is { } listId && _pendingOverrideId is { } overrideId)
            {
                _listOverrides[overrideId] = listId;
            }
        }

        private static void ApplyPictureControl(
            string word,
            int? parameter,
            PictureContext picture)
        {
            switch (word)
            {
                case "pngblip":
                    picture.MediaType = "image/png";
                    break;
                case "jpegblip":
                    picture.MediaType = "image/jpeg";
                    break;
                case "emfblip":
                    picture.MediaType = "image/emf";
                    break;
                case "wmetafile":
                    picture.MediaType = "image/wmf";
                    break;
                case "macpict":
                    picture.MediaType = "image/x-pict";
                    break;
                case "pmmetafile":
                    picture.MediaType = "image/x-os2-metafile";
                    break;
                case "dibitmap":
                    picture.MediaType = "image/x-rtf-dib";
                    break;
                case "wbitmap":
                    picture.MediaType = "image/x-rtf-ddb";
                    break;
                case "picwgoal":
                    picture.WidthTwips = parameter ?? 0;
                    break;
                case "pichgoal":
                    picture.HeightTwips = parameter ?? 0;
                    break;
                case "picw":
                    picture.PixelWidth = parameter ?? 0;
                    break;
                case "pich":
                    picture.PixelHeight = parameter ?? 0;
                    break;
                case "picscalex":
                    picture.ScaleX = parameter ?? 100;
                    break;
                case "picscaley":
                    picture.ScaleY = parameter ?? 100;
                    break;
                case "piccropt":
                    picture.CropTopTwips = parameter ?? 0;
                    break;
                case "piccropb":
                    picture.CropBottomTwips = parameter ?? 0;
                    break;
                case "piccropl":
                    picture.CropLeftTwips = parameter ?? 0;
                    break;
                case "piccropr":
                    picture.CropRightTwips = parameter ?? 0;
                    break;
            }
        }

        private void ProcessLiteral(char character, ReaderState state)
        {
            if (ConsumeUnicodeFallback())
            {
                return;
            }

            if (state.SkipDestination)
            {
                return;
            }

            if (state.Destination == Destination.FontTable && character == ';')
            {
                CompleteFontEntry();
                return;
            }

            if (state.Destination == Destination.ColorTable && character == ';')
            {
                CompleteColorEntry();
                return;
            }

            AppendDecodedCharacter(character, state);
        }

        private void ProcessDecodedCharacter(char character, ReaderState state)
        {
            if (ConsumeUnicodeFallback() || state.SkipDestination)
            {
                return;
            }

            AppendDecodedCharacter(character, state);
        }

        private void AppendDecodedCharacter(char character, ReaderState state)
        {
            switch (state.Destination)
            {
                case Destination.FontTable:
                    if (_fontIndex is not null)
                    {
                        _fontName.Append(character);
                    }
                    break;
                case Destination.ListText:
                    state.Capture!.Append(character, state.Format);
                    break;
                case Destination.ListLevelText:
                    state.ListLevelTextCapture!.Append(character);
                    break;
                case Destination.FieldInstruction:
                    state.FieldInstructionCapture!.Append(character);
                    break;
                case Destination.Picture:
                    state.Picture!.AppendHex(character);
                    break;
                case Destination.PicturePropertyName:
                case Destination.PicturePropertyValue:
                    state.PicturePropertyCapture!.Append(character);
                    break;
                case Destination.Body:
                    AppendBodyCharacter(character, state.Format, state);
                    break;
            }
        }

        private void AppendBodyCharacter(
            char character,
            RichTextCharacterFormat format,
            ReaderState state)
        {
            AppendPendingCellSeparator(format);
            TrackParagraphFormat(state);
            EnsureParagraphList(state);
            _document.Append(character, format);
            if (character == '\n')
            {
                _lineStart = _document.Length;
            }
        }

        private void AppendFallbackText(ReadOnlySpan<char> text, ReaderState state)
        {
            foreach (var character in text)
            {
                AppendBodyCharacter(character, state.Format, state);
            }
        }

        private void AppendParagraphBreak(ReaderState state)
        {
            if (state.Destination == Destination.ListText)
            {
                state.Capture!.Append('\n', state.Format);
                return;
            }

            if (state.Destination != Destination.Body)
            {
                return;
            }

            AppendPendingCellSeparator(state.Format);
            TrackParagraphFormat(state);
            EnsureParagraphList(state);
            _document.Append('\n', state.Format);
            _lineStart = _document.Length;
            _paragraphListHandled = false;
        }

        private void EndTableCell(ReaderState state)
        {
            if (state.Destination != Destination.Body)
            {
                return;
            }

            if (_pendingCellSeparator)
            {
                _document.Append('\t', state.Format);
            }

            _pendingCellSeparator = true;
        }

        private void EndTableRow(ReaderState state)
        {
            if (state.Destination != Destination.Body)
            {
                return;
            }

            _pendingCellSeparator = false;
            if (_document.LastCharacter == '\n')
            {
                _lineStart = _document.Length;
                _paragraphListHandled = false;
                return;
            }

            TrackParagraphFormat(state);
            _document.Append('\n', state.Format);
            _lineStart = _document.Length;
            _paragraphListHandled = false;
        }

        private void AppendPendingCellSeparator(RichTextCharacterFormat format)
        {
            if (!_pendingCellSeparator)
            {
                return;
            }

            _document.Append('\t', format);
            _pendingCellSeparator = false;
        }

        private void CompleteFontEntry()
        {
            if (_fontIndex is { } index)
            {
                _fonts[index] = new ParsedFont(
                    _fontName.ToString(),
                    _fontCodePage ??
                    (_fontCharacterSet is { } characterSet
                        ? GetCodePageForCharacterSet(characterSet)
                        : null) ??
                    _documentCodePage);
            }

            _fontIndex = null;
            _fontCharacterSet = null;
            _fontCodePage = null;
            _fontName.Clear();
        }

        private void CompleteColorEntry()
        {
            if (_colorRed is null && _colorGreen is null && _colorBlue is null)
            {
                _colors.Add(null);
            }
            else
            {
                _colors.Add(Color.FromRgb(
                    (byte)(_colorRed ?? 0),
                    (byte)(_colorGreen ?? 0),
                    (byte)(_colorBlue ?? 0)));
            }

            _colorRed = null;
            _colorGreen = null;
            _colorBlue = null;
        }

        private void CompleteGroup(ReaderState state)
        {
            if (state.CompletesDefaultCharacterProperties)
            {
                _defaultCharacterFormat = state.Format;
                _defaultCharacterFontIndex = state.FontIndex;
            }

            if (state.CompletesDefaultParagraphProperties)
            {
                _defaultParagraphFormat = state.ParagraphFormat;
            }

            if (state.CompletesFieldInstruction &&
                state.Field is not null &&
                state.FieldInstructionCapture is not null)
            {
                state.Field.Instruction = state.FieldInstructionCapture.ToString();
            }

            if (state.CompletesFieldResult &&
                state.Field is not null &&
                state.FieldResultStart == _document.Length &&
                TryEvaluateSymbolField(
                    state.Field.Instruction,
                    state.Format,
                    out var result,
                    out var format))
            {
                foreach (var character in result)
                {
                    AppendBodyCharacter(character, format, state);
                }
            }

            if (state.CompletesFieldResult && state.Field is not null)
            {
                CompleteFieldResult(state.Field, state.FieldResultStart);
            }

            if (state.CompletesPicture && state.Picture is not null)
            {
                CompletePicture(state.Picture);
            }

            if (state.CompletesListLevelText && state.ListLevelTextCapture is not null)
            {
                CompleteListLevelText(state.ListLevelTextCapture);
            }

            if (state.CompletesPicturePropertyName &&
                state.Picture is not null &&
                state.PicturePropertyCapture is not null)
            {
                state.Picture.PendingPropertyName = state.PicturePropertyCapture.ToString().Trim();
            }

            if (state.CompletesPicturePropertyValue &&
                state.Picture is not null &&
                state.PicturePropertyCapture is not null)
            {
                state.Picture.ApplyPropertyValue(state.PicturePropertyCapture.ToString());
            }

            if (state.CompletesObject && state.Object is not null)
            {
                CompleteObject(state.Object);
            }

            if (state.CompletesCapture && state.Capture is not null)
            {
                var definition = ResolveList(state.ListOverride, state.ListLevel);
                var kind = definition?.Kind ?? InferListKind(state.Capture.Text);
                if (kind is { } listKind)
                {
                    var level = Math.Clamp(state.ListLevel, 0, 8);
                    var parsedNumber = TryParseListNumberMarker(
                        state.Capture.Text,
                        definition,
                        out var numberMarker)
                        ? numberMarker
                        : (NumberMarker?)null;
                    var startAt = definition?.StartAt ?? parsedNumber?.Number ?? 1;
                    var counterKey = GetListCounterKey(state.ListOverride, level);
                    var hasExpectedNumber = _nextListNumbers.TryGetValue(
                        counterKey,
                        out var expectedNumber);
                    var restart = !hasExpectedNumber &&
                        _listOverrideStartAt.ContainsKey((state.ListOverride, level));
                    if (listKind == RichListKind.Numbered && parsedNumber is { } parsed)
                    {
                        if (hasExpectedNumber)
                        {
                            restart = parsed.Number != expectedNumber;
                            if (restart)
                            {
                                startAt = parsed.Number;
                            }
                        }
                        else if (definition is not null && parsed.Number != definition.Value.StartAt)
                        {
                            startAt = parsed.Number;
                        }
                    }

                    _listItems[_lineStart] = new RichTextListFormat
                    {
                        Id = state.ListOverride > 0
                            ? GetModelListId(state.ListOverride)
                            : _fallbackListId++,
                        Level = level,
                        Kind = listKind,
                        NumberStyle = (definition?.NumberFormat ?? parsedNumber?.Format) switch
                        {
                            ListNumberFormat.UpperRoman => RichListNumberStyle.UpperRoman,
                            ListNumberFormat.LowerRoman => RichListNumberStyle.LowerRoman,
                            ListNumberFormat.UpperLetter => RichListNumberStyle.UpperLetter,
                            ListNumberFormat.LowerLetter => RichListNumberStyle.LowerLetter,
                            _ => RichListNumberStyle.Arabic,
                        },
                        StartAt = startAt,
                        Restart = restart,
                        Prefix = definition?.Prefix ?? string.Empty,
                        Suffix = definition?.Suffix ?? parsedNumber?.Delimiter.ToString() ??
                            (listKind == RichListKind.Numbered ? "." : string.Empty),
                        BulletText = listKind == RichListKind.Bulleted
                            ? GetBulletText(state.Capture.Text, definition?.BulletText)
                            : "•",
                        PictureId = listKind == RichListKind.Bulleted
                            ? GetListPictureId(definition)
                            : null,
                    };
                }

                _paragraphListHandled = true;
                UpdateNumberCounter(state.ListOverride, state.ListLevel, state.Capture.Text);
            }
        }

        private void CompleteFieldResult(FieldContext field, int start)
        {
            if (string.IsNullOrWhiteSpace(field.Instruction) || start > _document.Length)
            {
                return;
            }

            var instruction = field.Instruction.Trim();
            var length = _document.Length - start;
            if (length > 0 && TryParseHyperlinkField(instruction, out var target, out var toolTip))
            {
                var link = new RichTextLink(start, length, target, toolTip);
                if (!_links.Any(existing => RangesOverlap(
                        existing.Start,
                        existing.End,
                        link.Start,
                        link.End)))
                {
                    _links.Add(link);
                }

                return;
            }

            var candidate = new RichTextField(start, length, instruction);
            if (!_fields.Any(existing => RangesOverlap(
                    existing.Start,
                    existing.End,
                    candidate.Start,
                    candidate.End)))
            {
                _fields.Add(candidate);
            }
        }

        private void CompletePicture(PictureContext picture)
        {
            var width = picture.WidthTwips != 0
                ? FromTwips(picture.WidthTwips)
                : picture.PixelWidth * 72d / 96d;
            var height = picture.HeightTwips != 0
                ? FromTwips(picture.HeightTwips)
                : picture.PixelHeight * 72d / 96d;
            width *= Math.Max(picture.ScaleX, 0) / 100d;
            height *= Math.Max(picture.ScaleY, 0) / 100d;
            if (picture.IsListPicture)
            {
                var id = _listPictures.Count.ToString(CultureInfo.InvariantCulture);
                _listPictures.Add(new RichTextListPicture
                {
                    Id = id,
                    MediaType = picture.MediaType,
                    Data = picture.TakeData(),
                    Width = Math.Max(width, 0),
                    Height = Math.Max(height, 0),
                    AlternativeText = picture.AlternativeText,
                });
                return;
            }

            var position = _document.Length;
            var pictureState = new ReaderState
            {
                Destination = Destination.Body,
                Format = picture.Format,
                ParagraphFormat = picture.ParagraphFormat,
                ListOverride = picture.ListOverride,
                ListLevel = picture.ListLevel,
            };
            AppendBodyCharacter(
                RichTextDocument.ObjectReplacementCharacter,
                picture.Format,
                pictureState);
            var image = new RichTextImage
            {
                Position = position,
                MediaType = picture.MediaType,
                Data = picture.TakeData(),
                Width = Math.Max(width, 0),
                Height = Math.Max(height, 0),
                Crop = new RichTextImageCrop(
                    FromTwips(picture.CropLeftTwips),
                    FromTwips(picture.CropTopTwips),
                    FromTwips(picture.CropRightTwips),
                    FromTwips(picture.CropBottomTwips)),
                AlternativeText = picture.AlternativeText,
                Rotation = picture.Rotation,
            };
            _images.Add(image);
        }

        private string? GetListPictureId(ParsedListDefinition? definition) =>
            definition?.PictureIndex is { } index &&
            (uint)index < (uint)_listPictures.Count
                ? _listPictures[index].Id
                : null;

        private void CompleteObject(ObjectContext context)
        {
            if (_document.Length != context.Start)
            {
                return;
            }

            var state = new ReaderState
            {
                Destination = Destination.Body,
                Format = context.Format,
                ParagraphFormat = context.ParagraphFormat,
                ListOverride = context.ListOverride,
                ListLevel = context.ListLevel,
            };
            AppendFallbackText("[Embedded object]", state);
        }

        private static bool TryParseHyperlinkField(
            string instruction,
            out string target,
            out string? toolTip)
        {
            target = string.Empty;
            toolTip = null;
            var position = 0;
            if (!TryReadFieldToken(instruction, ref position, out var fieldName) ||
                !fieldName.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? fragment = null;
            while (TryReadFieldToken(instruction, ref position, out var token))
            {
                if (token.Equals(@"\l", StringComparison.OrdinalIgnoreCase) &&
                    TryReadFieldToken(instruction, ref position, out var bookmark))
                {
                    fragment = bookmark;
                }
                else if (token.Equals(@"\o", StringComparison.OrdinalIgnoreCase) &&
                         TryReadFieldToken(instruction, ref position, out var parsedToolTip))
                {
                    toolTip = parsedToolTip;
                }
                else if (!token.StartsWith('\\') && target.Length == 0)
                {
                    target = token;
                }
            }

            if (fragment is not null)
            {
                target = target.Length == 0 ? $"#{fragment}" : $"{target}#{fragment}";
            }

            return target.Length > 0;
        }

        private static bool RangesOverlap(
            int firstStart,
            int firstEnd,
            int secondStart,
            int secondEnd) =>
            firstStart < secondEnd && secondStart < firstEnd;

        private bool TryEvaluateSymbolField(
            string? instruction,
            RichTextCharacterFormat resultFormat,
            out string result,
            out RichTextCharacterFormat format)
        {
            result = string.Empty;
            format = resultFormat;
            if (!TryParseSymbolField(instruction, out var symbol))
            {
                return false;
            }

            if (symbol.FontSize is { } fontSize)
            {
                format = format with { FontSize = fontSize };
            }

            if (symbol.FontFamily is { Length: > 0 } fontFamily)
            {
                format = format with { FontFamily = fontFamily };
            }

            if (symbol.Encoding == SymbolEncoding.Unicode)
            {
                if (!Rune.IsValid(symbol.CharacterNumber))
                {
                    return false;
                }

                result = char.ConvertFromUtf32(symbol.CharacterNumber);
                return true;
            }

            return TryDecodeSymbolCharacter(symbol, out result);
        }

        private bool TryDecodeSymbolCharacter(SymbolField symbol, out string result)
        {
            result = string.Empty;
            if (symbol.CharacterNumber is < 0 or > 0xFFFF)
            {
                return false;
            }

            Span<byte> bytes = stackalloc byte[2];
            var byteCount = 1;
            if (symbol.CharacterNumber <= byte.MaxValue)
            {
                bytes[0] = (byte)symbol.CharacterNumber;
            }
            else if (symbol.Encoding == SymbolEncoding.ShiftJis)
            {
                bytes[0] = (byte)(symbol.CharacterNumber >> 8);
                bytes[1] = (byte)symbol.CharacterNumber;
                byteCount = 2;
            }
            else
            {
                return false;
            }

            var codePage = symbol.Encoding == SymbolEncoding.ShiftJis ? 932 : _documentCodePage;
            result = GetEncoding(codePage).GetString(bytes[..byteCount]);
            return result.Length > 0;
        }

        private static bool TryParseSymbolField(string? instruction, out SymbolField symbol)
        {
            symbol = default;
            if (string.IsNullOrWhiteSpace(instruction))
            {
                return false;
            }

            var position = 0;
            if (!TryReadFieldToken(instruction, ref position, out var fieldName) ||
                !fieldName.Equals("SYMBOL", StringComparison.OrdinalIgnoreCase) ||
                !TryReadFieldToken(instruction, ref position, out var characterToken) ||
                !TryParseCharacterNumber(characterToken, out var characterNumber))
            {
                return false;
            }

            string? fontFamily = null;
            double? fontSize = null;
            var encoding = SymbolEncoding.Ansi;
            while (TryReadFieldToken(instruction, ref position, out var token))
            {
                if (token.Equals(@"\a", StringComparison.OrdinalIgnoreCase))
                {
                    encoding = SymbolEncoding.Ansi;
                }
                else if (token.Equals(@"\j", StringComparison.OrdinalIgnoreCase))
                {
                    encoding = SymbolEncoding.ShiftJis;
                }
                else if (token.Equals(@"\u", StringComparison.OrdinalIgnoreCase))
                {
                    encoding = SymbolEncoding.Unicode;
                }
                else if (token.Equals(@"\f", StringComparison.OrdinalIgnoreCase) &&
                         TryReadFieldToken(instruction, ref position, out var parsedFontFamily))
                {
                    fontFamily = parsedFontFamily;
                }
                else if (token.Equals(@"\s", StringComparison.OrdinalIgnoreCase) &&
                         TryReadFieldToken(instruction, ref position, out var sizeToken) &&
                         double.TryParse(
                             sizeToken,
                             NumberStyles.Float,
                             CultureInfo.InvariantCulture,
                             out var parsedFontSize) &&
                         double.IsFinite(parsedFontSize) &&
                         parsedFontSize > 0)
                {
                    fontSize = parsedFontSize;
                }
            }

            symbol = new SymbolField(characterNumber, fontFamily, fontSize, encoding);
            return true;
        }

        private static bool TryReadFieldToken(
            string instruction,
            ref int position,
            out string token)
        {
            while (position < instruction.Length && char.IsWhiteSpace(instruction[position]))
            {
                position++;
            }

            if (position >= instruction.Length)
            {
                token = string.Empty;
                return false;
            }

            if (instruction[position] == '"')
            {
                var start = ++position;
                while (position < instruction.Length && instruction[position] != '"')
                {
                    position++;
                }

                token = instruction[start..position];
                if (position < instruction.Length)
                {
                    position++;
                }

                return true;
            }

            var tokenStart = position;
            while (position < instruction.Length && !char.IsWhiteSpace(instruction[position]))
            {
                position++;
            }

            token = instruction[tokenStart..position];
            return true;
        }

        private static bool TryParseCharacterNumber(string token, out int characterNumber)
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(
                    token.AsSpan(2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out characterNumber);
            }

            if (int.TryParse(
                    token,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out characterNumber))
            {
                return true;
            }

            if (token.Length == 1)
            {
                characterNumber = token[0];
                return true;
            }

            characterNumber = 0;
            return false;
        }

        private void EnsureParagraphList(ReaderState state)
        {
            if (_paragraphListHandled || state.ListOverride <= 0)
            {
                return;
            }

            var definition = ResolveList(state.ListOverride, state.ListLevel);
            if (definition is null)
            {
                return;
            }

            var level = Math.Clamp(state.ListLevel, 0, 8);
            _listItems[_lineStart] = new RichTextListFormat
            {
                Id = GetModelListId(state.ListOverride),
                Level = level,
                Kind = definition.Value.Kind,
                NumberStyle = definition.Value.NumberFormat switch
                {
                    ListNumberFormat.UpperRoman => RichListNumberStyle.UpperRoman,
                    ListNumberFormat.LowerRoman => RichListNumberStyle.LowerRoman,
                    ListNumberFormat.UpperLetter => RichListNumberStyle.UpperLetter,
                    ListNumberFormat.LowerLetter => RichListNumberStyle.LowerLetter,
                    _ => RichListNumberStyle.Arabic,
                },
                StartAt = definition.Value.StartAt,
                Restart = _listOverrideStartAt.ContainsKey((state.ListOverride, level)) &&
                    !_nextListNumbers.ContainsKey(GetListCounterKey(state.ListOverride, level)),
                Prefix = definition.Value.Prefix,
                Suffix = definition.Value.Suffix,
                BulletText = definition.Value.BulletText,
                PictureId = definition.Value.Kind == RichListKind.Bulleted
                    ? GetListPictureId(definition)
                    : null,
            };
            if (definition.Value.Kind == RichListKind.Numbered)
            {
                _ = GetNextListNumber(state.ListOverride, state.ListLevel, definition.Value);
            }

            _paragraphListHandled = true;
        }

        private void TrackParagraphFormat(ReaderState state)
        {
            if (state.Destination != Destination.Body)
            {
                return;
            }

            if (state.ParagraphFormat == _defaultParagraphFormat)
            {
                _paragraphs.Remove(_lineStart);
            }
            else
            {
                _paragraphs[_lineStart] = state.ParagraphFormat;
            }
        }

        private ParsedListDefinition? ResolveList(int overrideId, int level)
        {
            if (overrideId <= 0 || !_listOverrides.TryGetValue(overrideId, out var listId))
            {
                return null;
            }

            level = Math.Clamp(level, 0, 8);
            ParsedListDefinition? result = null;
            if (_lists.TryGetValue((listId, level), out var definition))
            {
                result = definition;
            }
            else if (_lists.TryGetValue((listId, 0), out definition))
            {
                result = definition;
            }
            else
            {
                for (var fallbackLevel = 1; fallbackLevel < 9; fallbackLevel++)
                {
                    if (_lists.TryGetValue((listId, fallbackLevel), out definition))
                    {
                        result = definition;
                        break;
                    }
                }
            }

            if (result is { } resolved &&
                _listOverrideStartAt.TryGetValue((overrideId, level), out var startAt))
            {
                result = resolved with { StartAt = startAt };
            }

            return result;
        }

        private int GetModelListId(int overrideId)
        {
            var identity = GetListIdentity(overrideId);
            if (!_modelListIds.TryGetValue(identity, out var modelListId))
            {
                modelListId = _nextModelListId++;
                _modelListIds.Add(identity, modelListId);
            }

            return modelListId;
        }

        private (bool IsTableList, int Id) GetListIdentity(int overrideId) =>
            _listOverrides.TryGetValue(overrideId, out var listId)
                ? (true, listId)
                : (false, overrideId);

        private (bool IsTableList, int Id, int Level) GetListCounterKey(
            int overrideId,
            int level)
        {
            var identity = GetListIdentity(overrideId);
            return (identity.IsTableList, identity.Id, Math.Clamp(level, 0, 8));
        }

        private int GetNextListNumber(
            int overrideId,
            int level,
            ParsedListDefinition definition)
        {
            var key = GetListCounterKey(overrideId, level);
            var number = _nextListNumbers.GetValueOrDefault(key, definition.StartAt);
            _nextListNumbers[key] = number == int.MaxValue ? number : number + 1;
            return number;
        }

        private void UpdateNumberCounter(int overrideId, int level, string marker)
        {
            var definition = ResolveList(overrideId, level);
            if (definition is not { Kind: RichListKind.Numbered })
            {
                return;
            }

            var key = GetListCounterKey(overrideId, level);
            if (TryParseListNumberMarker(marker, definition, out var parsed))
            {
                _nextListNumbers[key] = parsed.Number == int.MaxValue
                    ? parsed.Number
                    : parsed.Number + 1;
            }
            else
            {
                _ = GetNextListNumber(overrideId, level, definition.Value);
            }
        }

        private static bool TryParseListNumberMarker(
            string marker,
            ParsedListDefinition? definition,
            out NumberMarker parsed)
        {
            if (definition is { Kind: RichListKind.Numbered } known)
            {
                var content = marker.AsSpan();
                content = !content.IsEmpty && content[^1] == '\t'
                    ? content[..^1]
                    : content.TrimEnd();
                if (content.StartsWith(known.Prefix, StringComparison.Ordinal) &&
                    content.EndsWith(known.Suffix, StringComparison.Ordinal) &&
                    content.Length >= known.Prefix.Length + known.Suffix.Length)
                {
                    var token = content.Slice(
                        known.Prefix.Length,
                        content.Length - known.Prefix.Length - known.Suffix.Length);
                    if (TryParseNumberToken(token, known.NumberFormat, out var number))
                    {
                        parsed = new NumberMarker(
                            number,
                            known.Suffix.Length > 0 ? known.Suffix[0] : '\0',
                            known.NumberFormat);
                        return true;
                    }
                }
            }

            return TryParseNumberMarker(marker.AsSpan().TrimStart(), out parsed, out _);
        }

        private static bool TryParseNumberToken(
            ReadOnlySpan<char> token,
            ListNumberFormat format,
            out int number)
        {
            if (format == ListNumberFormat.Arabic)
            {
                return int.TryParse(
                    token,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number) && number > 0;
            }

            if (format is ListNumberFormat.UpperRoman or ListNumberFormat.LowerRoman &&
                TryParseRoman(token, out number, out var romanFormat))
            {
                return romanFormat == format;
            }

            if (format is ListNumberFormat.UpperLetter or ListNumberFormat.LowerLetter &&
                TryParseLetters(token, out number, out var letterFormat))
            {
                return letterFormat == format;
            }

            number = 0;
            return false;
        }

        private static RichListKind? InferListKind(string marker)
        {
            var content = marker.AsSpan().TrimStart();
            if (content.Length == 0)
            {
                return null;
            }

            if (IsBulletMarker(content[0]))
            {
                return RichListKind.Bulleted;
            }

            return RichListKind.Numbered;
        }

        private static string GetBulletText(string marker, string? fallback)
        {
            if (!string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }

            var content = marker.AsSpan().Trim();
            return content.Length > 0 && IsBulletMarker(content[0])
                ? content[0].ToString()
                : fallback ?? "•";
        }

        private Color? ResolveColor(int index)
        {
            if (index < 0 || index >= _colors.Count)
            {
                return null;
            }

            return _colors[index];
        }

        private bool ConsumeUnicodeFallback()
        {
            if (_unicodeFallbackRemaining <= 0)
            {
                return false;
            }

            _unicodeFallbackRemaining--;
            return true;
        }

        private void EndUnicodeFallbackAtGroupDelimiter()
        {
            _unicodeFallbackRemaining = 0;
        }

        private void ProcessEncodedByte(byte value, ReaderState state)
        {
            if (ConsumeUnicodeFallback() || state.SkipDestination)
            {
                return;
            }

            if (_encodedBytes.Count > 0 &&
                (!ReferenceEquals(_encodedState, state) || _encodedCodePage != state.CodePage))
            {
                FlushEncodedBytes();
            }

            _encodedState = state;
            _encodedCodePage = state.CodePage;
            _encodedBytes.Add(value);
        }

        private void FlushEncodedBytes()
        {
            if (_encodedBytes.Count == 0)
            {
                return;
            }

            var state = _encodedState!;
            var encoding = GetEncoding(_encodedCodePage);
            var decoded = encoding.GetString(CollectionsMarshal.AsSpan(_encodedBytes));
            _encodedBytes.Clear();
            _encodedState = null;
            foreach (var character in decoded)
            {
                AppendDecodedCharacter(character, state);
            }
        }

        private static Encoding GetEncoding(int codePage)
        {
            if (codePage == 42)
            {
                return Encoding.Latin1;
            }

            return codePage switch
            {
                DefaultCodePage => Encoding.UTF8,
                20127 => Encoding.ASCII,
                28591 => Encoding.Latin1,
                _ => CodePagesEncodingProvider.Instance.GetEncoding(
                    codePage,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback) ?? Encoding.Latin1,
            };
        }

        private void SetDocumentCodePage(ReaderState state, int codePage)
        {
            _documentCodePage = codePage;
            state.CodePage = codePage;
        }

        private int GetFontCodePage(int fontIndex) =>
            _fonts.TryGetValue(fontIndex, out var font) && font.CodePage > 0
                ? font.CodePage
                : _documentCodePage;

        private int GetDefaultCharacterFontIndex() =>
            _defaultCharacterFontIndex ?? _defaultFontIndex;

        private string? GetDefaultFontFamily() =>
            _fonts.TryGetValue(GetDefaultCharacterFontIndex(), out var font)
                ? font.Name
                : null;

        private double GetDefaultFontSize() =>
            _defaultCharacterFormat.FontSize ?? DefaultRtfFontSize;

        private void ResetToDefaultCharacterProperties(ReaderState state)
        {
            state.Format = _defaultCharacterFormat with
            {
                FontFamily = null,
                FontSize = null,
                ForegroundColor = null,
            };
            state.FontIndex = null;
            state.CodePage = GetFontCodePage(GetDefaultCharacterFontIndex());
            state.CharacterShading = null;
        }

        private void ResetToDefaultParagraphProperties(ReaderState state)
        {
            state.ParagraphFormat = _defaultParagraphFormat;
            state.ListOverride = 0;
            state.ListLevel = 0;
            ResetParagraphControlState(state);
        }

        private static void ResetParagraphControlState(ReaderState state)
        {
            state.ParagraphLineSpacingTwips = 0;
            state.ParagraphLineSpacingMultiple = false;
            state.PendingTabAlignment = RichTextTabAlignment.Left;
            state.PendingTabLeader = RichTextTabLeader.None;
            state.CurrentBorderSides = RichTextBorderSides.None;
        }

        private static int? GetCodePageForCharacterSet(int characterSet) => characterSet switch
        {
            0 => 1252,
            1 => null,
            2 => 42,
            77 => 10000,
            78 => 10001,
            79 => 10003,
            80 => 10008,
            81 => 10002,
            83 => 10005,
            84 => 10004,
            85 => 10006,
            86 => 10081,
            87 => 10021,
            88 => 10029,
            89 => 10007,
            128 => 932,
            129 => 949,
            130 => 1361,
            134 => 936,
            136 => 950,
            161 => 1253,
            162 => 1254,
            163 => 1258,
            177 => 1255,
            178 => 1256,
            186 => 1257,
            204 => 1251,
            222 => 874,
            238 => 1250,
            254 => 437,
            255 => 850,
            _ => null,
        };

        private FormatException Error(int position, string message) =>
            new($"Invalid RTF at character {position}: {message}");

        private readonly record struct ParsedFont(string? Name, int CodePage);

        private readonly record struct SymbolField(
            int CharacterNumber,
            string? FontFamily,
            double? FontSize,
            SymbolEncoding Encoding);

        private enum SymbolEncoding
        {
            Ansi,
            ShiftJis,
            Unicode,
        }

        private readonly record struct ParsedListDefinition(
            RichListKind Kind,
            int StartAt,
            ListNumberFormat NumberFormat,
            string Prefix,
            string Suffix,
            string BulletText,
            int? PictureIndex);

        private enum Destination
        {
            Body,
            FontTable,
            ColorTable,
            DefaultCharacterProperties,
            DefaultParagraphProperties,
            ListTable,
            ListPictures,
            ListOverrideTable,
            ListLevelText,
            ListText,
            FieldInstruction,
            Picture,
            PictureProperties,
            PicturePropertyName,
            PicturePropertyValue,
            Object,
            Shape,
            ShapeInstructions,
            ShapeProperty,
            ShapePropertyName,
            ShapePropertyValue,
            Skip,
        }

        private sealed class FieldContext
        {
            public string? Instruction { get; set; }
        }

        private sealed class ObjectContext(
            int start,
            RichTextCharacterFormat format,
            RichTextParagraphFormat paragraphFormat,
            int listOverride,
            int listLevel)
        {
            public int Start { get; } = start;

            public RichTextCharacterFormat Format { get; } = format;

            public RichTextParagraphFormat ParagraphFormat { get; } = paragraphFormat;

            public int ListOverride { get; } = listOverride;

            public int ListLevel { get; } = listLevel;
        }

        private sealed class PictureContext(
            RichTextCharacterFormat format,
            RichTextParagraphFormat paragraphFormat,
            int listOverride,
            int listLevel,
            bool isListPicture)
        {
            private readonly List<byte> _data = [];
            private int? _highNibble;

            public RichTextCharacterFormat Format { get; } = format;

            public RichTextParagraphFormat ParagraphFormat { get; } = paragraphFormat;

            public int ListOverride { get; } = listOverride;

            public int ListLevel { get; } = listLevel;

            public bool IsListPicture { get; } = isListPicture;

            public string MediaType { get; set; } = "application/octet-stream";

            public int WidthTwips { get; set; }

            public int HeightTwips { get; set; }

            public int PixelWidth { get; set; }

            public int PixelHeight { get; set; }

            public int ScaleX { get; set; } = 100;

            public int ScaleY { get; set; } = 100;

            public int CropTopTwips { get; set; }

            public int CropBottomTwips { get; set; }

            public int CropLeftTwips { get; set; }

            public int CropRightTwips { get; set; }

            public string? PendingPropertyName { get; set; }

            public string? AlternativeText { get; private set; }

            public double Rotation { get; private set; }

            public ImmutableArray<byte> TakeData() =>
                ImmutableCollectionsMarshal.AsImmutableArray(_data.ToArray());

            public void ApplyPropertyValue(string value)
            {
                if (string.Equals(PendingPropertyName, "wzDescription", StringComparison.OrdinalIgnoreCase))
                {
                    AlternativeText = value;
                }
                else if (string.Equals(PendingPropertyName, "rotation", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(
                             value,
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out var fixedAngle))
                {
                    Rotation = fixedAngle / 65536d;
                }

                PendingPropertyName = null;
            }

            public void AppendByte(byte value)
            {
                _highNibble = null;
                _data.Add(value);
            }

            public void AppendHex(char character)
            {
                int nibble;
                if (character is >= '0' and <= '9')
                {
                    nibble = character - '0';
                }
                else if (character is >= 'a' and <= 'f')
                {
                    nibble = character - 'a' + 10;
                }
                else if (character is >= 'A' and <= 'F')
                {
                    nibble = character - 'A' + 10;
                }
                else
                {
                    return;
                }

                if (_highNibble is { } high)
                {
                    _data.Add((byte)((high << 4) | nibble));
                    _highNibble = null;
                }
                else
                {
                    _highNibble = nibble;
                }
            }
        }

        private sealed class ReaderState
        {
            public Destination Destination { get; set; }

            public RichTextCharacterFormat Format { get; set; } = RichTextCharacterFormat.Default;

            public int? FontIndex { get; set; }

            public int UnicodeSkipCount { get; set; } = 1;

            public int CodePage { get; set; } = DefaultCodePage;

            public int? CharacterShading { get; set; }

            public RichTextParagraphFormat ParagraphFormat { get; set; } = RichTextParagraphFormat.Default;

            public int ParagraphLineSpacingTwips { get; set; }

            public bool ParagraphLineSpacingMultiple { get; set; }

            public RichTextTabAlignment PendingTabAlignment { get; set; }

            public RichTextTabLeader PendingTabLeader { get; set; }

            public RichTextBorderSides CurrentBorderSides { get; set; }

            public int ListOverride { get; set; }

            public int ListLevel { get; set; }

            public bool AtGroupStart { get; set; } = true;

            public bool IgnorableDestination { get; set; }

            public bool SkipDestination { get; set; }

            public bool CompletesCapture { get; set; }

            public TextAccumulator? Capture { get; set; }

            public StringBuilder? ListLevelTextCapture { get; set; }

            public bool CompletesListLevelText { get; set; }

            public StringBuilder? PicturePropertyCapture { get; set; }

            public bool CompletesPicturePropertyName { get; set; }

            public bool CompletesPicturePropertyValue { get; set; }

            public FieldContext? Field { get; set; }

            public ObjectContext? Object { get; set; }

            public bool CompletesObject { get; set; }

            public StringBuilder? FieldInstructionCapture { get; set; }

            public bool CompletesFieldInstruction { get; set; }

            public int FieldResultStart { get; set; }

            public bool CompletesFieldResult { get; set; }

            public PictureContext? Picture { get; set; }

            public bool CompletesPicture { get; set; }

            public bool CompletesDefaultCharacterProperties { get; set; }

            public bool CompletesDefaultParagraphProperties { get; set; }

            public ReaderState CloneForGroup() => new()
            {
                Destination = Destination,
                Format = Format,
                FontIndex = FontIndex,
                UnicodeSkipCount = UnicodeSkipCount,
                CodePage = CodePage,
                CharacterShading = CharacterShading,
                ParagraphFormat = ParagraphFormat,
                ParagraphLineSpacingTwips = ParagraphLineSpacingTwips,
                ParagraphLineSpacingMultiple = ParagraphLineSpacingMultiple,
                PendingTabAlignment = PendingTabAlignment,
                PendingTabLeader = PendingTabLeader,
                CurrentBorderSides = CurrentBorderSides,
                ListOverride = ListOverride,
                ListLevel = ListLevel,
                AtGroupStart = true,
                SkipDestination = SkipDestination,
                Capture = Capture,
                ListLevelTextCapture = ListLevelTextCapture,
                PicturePropertyCapture = PicturePropertyCapture,
                Field = Field,
                Object = Object,
                FieldInstructionCapture = FieldInstructionCapture,
                Picture = Picture,
            };
        }
    }

    private sealed class TextAccumulator
    {
        private readonly StringBuilder _text = new();
        private readonly List<RichTextRun> _runs = [];

        public string Text => _text.ToString();

        public int Length => _text.Length;

        public char? LastCharacter => _text.Length == 0 ? null : _text[^1];

        public IReadOnlyList<RichTextRun> Runs => _runs;

        public void Append(char character, RichTextCharacterFormat format)
        {
            var start = _text.Length;
            _text.Append(character);
            if (_runs.Count > 0)
            {
                var previous = _runs[^1];
                if (previous.End == start && previous.Format == format)
                {
                    _runs[^1] = previous with { Length = previous.Length + 1 };
                    return;
                }
            }

            _runs.Add(new RichTextRun(start, 1, format));
        }

        public void Append(TextAccumulator fragment)
        {
            var offset = _text.Length;
            _text.Append(fragment._text);
            foreach (var run in fragment._runs)
            {
                var adjusted = run with { Start = run.Start + offset };
                if (_runs.Count > 0 &&
                    _runs[^1].End == adjusted.Start &&
                    _runs[^1].Format == adjusted.Format)
                {
                    _runs[^1] = _runs[^1] with
                    {
                        Length = _runs[^1].Length + adjusted.Length,
                    };
                }
                else
                {
                    _runs.Add(adjusted);
                }
            }
        }
    }
}
