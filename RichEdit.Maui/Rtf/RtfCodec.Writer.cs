using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Writer
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
                .Select(static paragraph => paragraph.Format.List?.ListId)
                .OfType<RichTextListId>()
                .Distinct()
                .SelectMany(id => document.Lists[id].Levels)
                .Select(static level => (level.Marker as RichTextListMarker.Picture)?.PictureId)
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
                ? document.Fields.Where(field => field.Length > 0).ToDictionary(field => field.Start)
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
                .Concat(_semanticBoundaries)
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

        private void WriteBody()
        {
            RichTextField? activeField = null;
            RichTextLink? activeLink = null;
            var position = 0;
            while (true)
            {
                var closedScope = false;
                if (activeLink?.End == position)
                {
                    _output.Append("}}");
                    activeLink = null;
                    closedScope = true;
                }

                if (activeField?.End == position)
                {
                    _output.Append("}}");
                    activeField = null;
                    closedScope = true;
                }

                var paragraphStart = position == 0 || _document.Text[position - 1] == '\n';
                if (paragraphStart || closedScope)
                {
                    // Paragraph controls may live inside a field result. Reapply
                    // them when a semantic group closes and restores older state.
                    WriteParagraphHeader(position, writeListText: paragraphStart);
                }

                WriteEmptyFieldsAt(position);
                if (_fieldsByStart.TryGetValue(position, out var field))
                {
                    activeField = field;
                    WriteFieldHeader(field.Instruction);
                }

                if (_linksByStart.TryGetValue(position, out var link))
                {
                    activeLink = link;
                    WriteHyperlinkHeader(link);
                }

                if (position == _document.Length)
                {
                    break;
                }

                if (_document.Text[position] == '\n')
                {
                    WriteParagraphBreak(position++);
                    continue;
                }

                if (_imagesByPosition.TryGetValue(position, out var image))
                {
                    WriteImage(image);
                    position++;
                    continue;
                }

                var run = GetRunAt(position);
                var end = Math.Min(run.End, GetNextSemanticStart(position, _document.Length));
                var newline = _document.Text.IndexOf('\n', position, end - position);
                if (newline >= 0)
                {
                    end = newline;
                }

                WriteRun(_document.Text.AsSpan(position, end - position), run.Format);
                position = end;
            }
        }

        private void WriteParagraphHeader(int position, bool writeListText)
        {
            _output.Append(@"\pard\plain");
            if (_nativeDefaultCharacterFormat is not null)
            {
                WriteNativeDefaultAppearance(_document.DefaultCharacterFormat);
            }

            var paragraph = _document.Paragraphs[_document.FindParagraphIndex(position)];
            var paragraphFormat = paragraph.Format;
            WriteParagraphFormatControls(paragraphFormat, _document.DefaultParagraphFormat);

            if (_listsByItemStart.TryGetValue(paragraph.Start, out var list))
            {
                _output.Append(@"\ls").Append(list.Override.OverrideId)
                    .Append(@"\ilvl").Append(list.Format.Level)
                    .Append(' ');
                if (!writeListText)
                {
                    return;
                }

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

        }

        private void WriteEmptyFieldsAt(int position)
        {
            if (_emptyFieldsByPosition.TryGetValue(position, out var fields))
            {
                foreach (var field in fields)
                {
                    WriteFieldHeader(field.Instruction);
                    _output.Append("}}");
                }
            }
        }

        private void WriteFieldHeader(string instruction)
        {
            _output.Append(@"{\field{\*\fldinst ");
            WriteText(instruction.AsSpan());
            _output.Append(@"}{\fldrslt ");
        }

        private void WriteHyperlinkHeader(RichTextLink link)
        {
            _output.Append("{\\field{\\*\\fldinst HYPERLINK \"");
            WriteText(EscapeFieldArgument(link.Target).AsSpan());
            _output.Append('"');
            if (!string.IsNullOrWhiteSpace(link.ToolTip))
            {
                _output.Append(" \\\\o \"");
                WriteText(EscapeFieldArgument(link.ToolTip).AsSpan());
                _output.Append('"');
            }

            _output.Append(@"}{\fldrslt ");
        }

        // Word field syntax escapes literal backslashes and quotation marks in a
        // quoted argument, so UNC targets and quoted text survive a round trip.
        private static string EscapeFieldArgument(string value) =>
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

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
                var segmentEnd = end;
                var boundaryIndex = Array.BinarySearch(_semanticBoundaries, start + 1);
                if (boundaryIndex < 0)
                {
                    boundaryIndex = ~boundaryIndex;
                }

                if (boundaryIndex < _semanticBoundaries.Length)
                {
                    segmentEnd = Math.Min(segmentEnd, _semanticBoundaries[boundaryIndex]);
                }

                yield return (start, segmentEnd - start);
                start = segmentEnd;
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
    }
}
