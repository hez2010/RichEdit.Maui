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

    public static string Serialize(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Writer(document).Write();
    }

    public static RichTextDocument Parse(string rtf)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        return new Reader(rtf).Read();
    }

    private readonly record struct RtfColor(byte Red, byte Green, byte Blue);

    private sealed class Writer
    {
        private readonly RichTextDocument _document;
        private readonly StringBuilder _output = new();
        private readonly List<string> _fontNames = [];
        private readonly Dictionary<string, int> _fontIndices = new(StringComparer.Ordinal);
        private readonly List<RtfColor> _colors = [];
        private readonly Dictionary<RtfColor, int> _colorIndices = [];
        private readonly List<ListDefinition> _listDefinitions;
        private readonly Dictionary<int, ListDefinition> _listsByItemStart = [];
        private readonly Dictionary<int, int> _nextListNumbers = [];

        public Writer(RichTextDocument document)
        {
            _document = document;
            AddFont(document.DefaultCharacterFormat.FontFamily ?? "Arial");
            BuildFormattingTables();
            _listDefinitions = BuildListDefinitions();
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
            foreach (var definition in _listDefinitions)
            {
                _output.Append(@"{\list\listtemplateid").Append(definition.ListId)
                    .Append(@"\listsimple1{")
                    .Append(@"\listlevel\levelnfc")
                    .Append(definition.Kind == RichListKind.Bulleted ? 23 : (int)definition.NumberFormat)
                    .Append(@"\levelnfcn")
                    .Append(definition.Kind == RichListKind.Bulleted ? 23 : (int)definition.NumberFormat)
                    .Append(@"\leveljc0\leveljcn0\levelfollow1\levelstartat")
                    .Append(definition.StartAt);

                if (definition.Kind == RichListKind.Bulleted)
                {
                    _output.Append(@"{\leveltext\'01\'95;}{\levelnumbers;}");
                }
                else
                {
                    _output.Append(@"{\leveltext\'02\'00")
                        .Append(definition.Delimiter)
                        .Append(@";}{\levelnumbers\'01;}");
                }

                _output.Append(@"\fi-360\li720\lin720}\listrestarthdn0\listid")
                    .Append(definition.ListId)
                    .Append(@"{\listname ;}}")
                    .Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
            _output.Append(@"{\*\listoverridetable").Append("\r\n");
            foreach (var definition in _listDefinitions)
            {
                _output.Append(@"{\listoverride\listid").Append(definition.ListId)
                    .Append(@"\listoverridecount0\ls").Append(definition.OverrideId)
                    .Append("}")
                    .Append("\r\n");
            }

            _output.Append('}').Append("\r\n");
        }

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
            var paragraphFormat = _document.GetParagraphFormat(start);
            WriteParagraphFormatControls(paragraphFormat, _document.DefaultParagraphFormat);

            if (_listsByItemStart.TryGetValue(start, out var list))
            {
                _output.Append(@"\ls").Append(list.OverrideId)
                    .Append(@"\ilvl").Append(paragraphFormat.List?.Level ?? 0)
                    .Append(' ');
                _output.Append(@"{\listtext ");
                if (list.Kind == RichListKind.Bulleted)
                {
                    WriteText((paragraphFormat.List?.BulletText ?? "•").AsSpan());
                }
                else
                {
                    var number = _nextListNumbers.GetValueOrDefault(
                        list.OverrideId,
                        list.StartAt);
                    _nextListNumbers[list.OverrideId] = number == int.MaxValue
                        ? number
                        : number + 1;
                    _output.Append(FormatListNumber(number, list.NumberFormat))
                        .Append(list.Delimiter);
                }

                _output.Append(@"\tab ");
                _output.Append('}');
            }
            else
            {
                _output.Append(' ');
            }

            WriteRange(start, end);
            if (hasParagraphBreak)
            {
                WriteParagraphBreak(end);
            }

            _output.Append('}').Append("\r\n");
        }

        private void WriteRange(int start, int end)
        {
            var position = start;
            while (position < end)
            {
                var format = _document.GetCharacterFormat(position);
                var runEnd = position + 1;
                while (runEnd < end && format == _document.GetCharacterFormat(runEnd))
                {
                    runEnd++;
                }

                WriteRun(_document.Text.AsSpan(position, runEnd - position), format);
                position = runEnd;
            }
        }

        private void WriteParagraphBreak(int index)
        {
            var format = _document.GetCharacterFormat(index);
            if (format == _document.DefaultCharacterFormat)
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
            if (format == _document.DefaultCharacterFormat)
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

            if (!string.Equals(format.FontFamily, baseline.FontFamily, StringComparison.Ordinal) &&
                format.FontFamily is not null)
            {
                _output.Append(@"\f").Append(_fontIndices[format.FontFamily]);
            }

            if (format.FontSize != baseline.FontSize && format.FontSize is { } fontSize)
            {
                _output.Append(@"\fs").Append(GetHalfPointSize(fontSize));
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

            WriteColorControl(@"\cf", format.ForegroundColor, baseline.ForegroundColor);
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

        private void WriteColorControl(string control, Color? value, Color? baseline)
        {
            if (Equals(value, baseline))
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
                if (paragraph.Format.List is not { } list)
                {
                    continue;
                }

                if (!definitionsById.TryGetValue(list.Id, out var definition))
                {
                    var overrideId = definitions.Count + 1;
                    if (overrideId > MaximumListOverrideId)
                    {
                        throw new InvalidOperationException(
                            $"RTF 1.9.1 permits at most {MaximumListOverrideId} list override IDs in one document.");
                    }

                    definition = new ListDefinition(
                        overrideId,
                        overrideId,
                        list.Kind,
                        list.StartAt,
                        string.IsNullOrEmpty(list.Suffix) ? ' ' : list.Suffix[0],
                        list.NumberStyle switch
                        {
                            RichListNumberStyle.UpperRoman => ListNumberFormat.UpperRoman,
                            RichListNumberStyle.LowerRoman => ListNumberFormat.LowerRoman,
                            RichListNumberStyle.UpperLetter => ListNumberFormat.UpperLetter,
                            RichListNumberStyle.LowerLetter => ListNumberFormat.LowerLetter,
                            _ => ListNumberFormat.Arabic,
                        });
                    definitionsById.Add(list.Id, definition);
                    definitions.Add(definition);
                }

                _listsByItemStart.Add(paragraph.Start, definition);
            }

            return definitions;
        }

        private sealed class ListDefinition(
            int listId,
            int overrideId,
            RichListKind kind,
            int startAt,
            char delimiter,
            ListNumberFormat numberFormat)
        {
            public int ListId { get; } = listId;

            public int OverrideId { get; } = overrideId;

            public RichListKind Kind { get; } = kind;

            public int StartAt { get; } = startAt;

            public char Delimiter { get; } = delimiter;

            public ListNumberFormat NumberFormat { get; } = numberFormat;

        }
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
            !token.Equals(FormatRomanNumber(total, uppercase).AsSpan(), StringComparison.Ordinal))
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

    private static string FormatListNumber(int number, ListNumberFormat format) => format switch
    {
        ListNumberFormat.UpperRoman => FormatRomanNumber(number, uppercase: true),
        ListNumberFormat.LowerRoman => FormatRomanNumber(number, uppercase: false),
        ListNumberFormat.UpperLetter => FormatLetterNumber(number, uppercase: true),
        ListNumberFormat.LowerLetter => FormatLetterNumber(number, uppercase: false),
        _ => number.ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatRomanNumber(int number, bool uppercase)
    {
        if (number is <= 0 or > 3999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        ReadOnlySpan<(int Value, string Text)> numerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];
        var output = new StringBuilder();
        foreach (var numeral in numerals)
        {
            while (number >= numeral.Value)
            {
                output.Append(numeral.Text);
                number -= numeral.Value;
            }
        }

        var result = output.ToString();
        return uppercase ? result : result.ToLowerInvariant();
    }

    private static string FormatLetterNumber(int number, bool uppercase)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        Span<char> buffer = stackalloc char[16];
        var position = buffer.Length;
        while (number > 0 && position > 0)
        {
            number--;
            buffer[--position] = (char)((uppercase ? 'A' : 'a') + number % 26);
            number /= 26;
        }

        return number == 0
            ? new string(buffer[position..])
            : string.Empty;
    }

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
            "nonesttables", "nonshppict", "object", "objdata", "pict", "private", "revtbl", "rsidtbl",
            "shp", "shpinst", "shppict", "stylesheet", "themedata", "userprops", "xmlnstbl",
        };

        private readonly string _rtf;
        private readonly TextAccumulator _document = new();
        private readonly Dictionary<int, ParsedFont> _fonts = [];
        private readonly List<Color?> _colors = [];
        private readonly Dictionary<int, ParsedListDefinition> _lists = [];
        private readonly Dictionary<int, int> _listOverrides = [];
        private readonly Dictionary<int, RichTextListFormat> _listItems = [];
        private readonly Dictionary<int, RichTextParagraphFormat> _paragraphs = [];
        private readonly Dictionary<int, int> _nextListNumbers = [];
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
        private int? _pendingOverrideListId;
        private int? _pendingOverrideId;
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

        public Reader(string rtf)
        {
            _rtf = rtf;
        }

        public RichTextDocument Read()
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
            var runs = _document.Runs.Select(run => run with
            {
                Format = run.Format with
                {
                    FontFamily = run.Format.FontFamily ?? defaultCharacterFormat.FontFamily,
                    FontSize = run.Format.FontSize ?? defaultCharacterFormat.FontSize,
                },
            });
            var paragraphs = Enumerable.Range(0, _document.Text.Length + 1)
                .Where(start => start == 0 || _document.Text[start - 1] == '\n')
                .Select(start =>
            {
                var format = _paragraphs.GetValueOrDefault(start, _defaultParagraphFormat);
                _listItems.TryGetValue(start, out var list);

                return new RichTextParagraph(
                    start,
                    format with { List = list });
            });
            return new RichTextDocument(
                _document.Text,
                runs,
                paragraphs,
                defaultCharacterFormat: defaultCharacterFormat,
                defaultParagraphFormat: _defaultParagraphFormat);
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
                var tab = new RichTextTabStop(
                    Math.Max(FromTwips(parameter ?? 0), 0),
                    state.PendingTabAlignment,
                    state.PendingTabLeader);
                SetParagraphFormat(state, state.ParagraphFormat with
                {
                    TabStops = state.ParagraphFormat.TabStops.Add(tab),
                });
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
                _pendingListKind = null;
                _pendingListNumberFormat = ListNumberFormat.Arabic;
                _pendingListStartAt = 1;
                _pendingListLevel = -1;
                _pendingListUsesModernNumberFormat = false;
                return;
            }

            if (word == "listlevel")
            {
                _pendingListLevel++;
                return;
            }

            if (word == "listid" && parameter is not null)
            {
                _pendingListId = parameter;
            }
            else if (word == "levelnfc" && parameter is not null &&
                     _pendingListLevel <= 0 && !_pendingListUsesModernNumberFormat)
            {
                SetPendingListNumberFormat(parameter.Value);
            }
            else if (word == "levelnfcn" && parameter is not null && _pendingListLevel <= 0)
            {
                SetPendingListNumberFormat(parameter.Value);
                _pendingListUsesModernNumberFormat = true;
            }
            else if (word == "levelstartat" && parameter is > 0 && _pendingListLevel <= 0)
            {
                _pendingListStartAt = parameter.Value;
            }

            CommitListDefinition();
        }

        private void CommitListDefinition()
        {
            if (_pendingListId is { } listId && _pendingListKind is { } kind)
            {
                _lists[listId] = new ParsedListDefinition(
                    kind,
                    _pendingListStartAt,
                    _pendingListNumberFormat);
            }
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

            if (_pendingOverrideListId is { } listId && _pendingOverrideId is { } overrideId)
            {
                _listOverrides[overrideId] = listId;
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
                case Destination.FieldInstruction:
                    state.FieldInstructionCapture!.Append(character);
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

            if (state.CompletesCapture && state.Capture is not null)
            {
                var definition = ResolveList(state.ListOverride);
                var kind = definition?.Kind ?? InferListKind(state.Capture.Text);
                if (kind is { } listKind)
                {
                    var parsedNumber = TryParseNumberMarker(
                        state.Capture.Text.AsSpan().TrimStart(),
                        out var numberMarker,
                        out _)
                        ? numberMarker
                        : (NumberMarker?)null;
                    _listItems[_lineStart] = new RichTextListFormat
                    {
                        Id = state.ListOverride > 0
                            ? state.ListOverride
                            : _fallbackListId++,
                        Level = Math.Clamp(state.ListLevel, 0, 8),
                        Kind = listKind,
                        NumberStyle = (definition?.NumberFormat ?? parsedNumber?.Format) switch
                        {
                            ListNumberFormat.UpperRoman => RichListNumberStyle.UpperRoman,
                            ListNumberFormat.LowerRoman => RichListNumberStyle.LowerRoman,
                            ListNumberFormat.UpperLetter => RichListNumberStyle.UpperLetter,
                            ListNumberFormat.LowerLetter => RichListNumberStyle.LowerLetter,
                            _ => RichListNumberStyle.Arabic,
                        },
                        StartAt = definition?.StartAt ?? parsedNumber?.Number ?? 1,
                        Suffix = parsedNumber?.Delimiter.ToString() ??
                            (listKind == RichListKind.Numbered ? "." : string.Empty),
                        BulletText = listKind == RichListKind.Bulleted
                            ? GetBulletText(state.Capture.Text)
                            : "•",
                    };
                }

                _paragraphListHandled = true;
                UpdateNumberCounter(state.ListOverride, state.Capture.Text);
            }
        }

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

            var definition = ResolveList(state.ListOverride);
            if (definition is null)
            {
                return;
            }

            _listItems[_lineStart] = new RichTextListFormat
            {
                Id = state.ListOverride,
                Level = Math.Clamp(state.ListLevel, 0, 8),
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
            };
            if (definition.Value.Kind == RichListKind.Numbered)
            {
                _ = GetNextListNumber(state.ListOverride, definition.Value);
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

        private ParsedListDefinition? ResolveList(int overrideId)
        {
            if (overrideId <= 0 || !_listOverrides.TryGetValue(overrideId, out var listId) ||
                !_lists.TryGetValue(listId, out var definition))
            {
                return null;
            }

            return definition;
        }

        private int GetNextListNumber(int overrideId, ParsedListDefinition definition)
        {
            var number = _nextListNumbers.GetValueOrDefault(overrideId, definition.StartAt);
            _nextListNumbers[overrideId] = number == int.MaxValue ? number : number + 1;
            return number;
        }

        private void UpdateNumberCounter(int overrideId, string marker)
        {
            var definition = ResolveList(overrideId);
            if (definition is not { Kind: RichListKind.Numbered })
            {
                return;
            }

            if (TryParseNumberMarker(marker.AsSpan().TrimStart(), out var parsed, out _))
            {
                _nextListNumbers[overrideId] = parsed.Number == int.MaxValue
                    ? parsed.Number
                    : parsed.Number + 1;
            }
            else
            {
                _ = GetNextListNumber(overrideId, definition.Value);
            }
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

        private static string GetBulletText(string marker)
        {
            var content = marker.AsSpan().Trim();
            return content.Length > 0 && IsBulletMarker(content[0])
                ? content[0].ToString()
                : "•";
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
            ListNumberFormat NumberFormat);

        private enum Destination
        {
            Body,
            FontTable,
            ColorTable,
            DefaultCharacterProperties,
            DefaultParagraphProperties,
            ListTable,
            ListOverrideTable,
            ListText,
            FieldInstruction,
            Skip,
        }

        private sealed class FieldContext
        {
            public string? Instruction { get; set; }
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

            public FieldContext? Field { get; set; }

            public StringBuilder? FieldInstructionCapture { get; set; }

            public bool CompletesFieldInstruction { get; set; }

            public int FieldResultStart { get; set; }

            public bool CompletesFieldResult { get; set; }

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
                Field = Field,
                FieldInstructionCapture = FieldInstructionCapture,
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
