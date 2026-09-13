using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
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
        private readonly Dictionary<(int OverrideId, int Level), ParsedListDefinition> _listOverrideLevels = [];
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
        private int _documentCodePage = AnsiCodePage;
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
                        var completesFontTable =
                            state.Destination == Destination.FontTable &&
                            stack.Peek().Destination != Destination.FontTable;
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

                        if (completesFontTable && state.FontIndex is null)
                        {
                            // Text that follows the font table without an explicit
                            // \f control uses the \deff default font's code page.
                            state.CodePage = GetFontCodePage(GetDefaultCharacterFontIndex());
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
                fields: _fields,
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
                    // An out-of-range numeric parameter saturates instead of failing
                    // the document. Control words that require a bounded value
                    // validate the saturated parameter themselves.
                    value = _rtf[parameterStart] == '-' ? int.MinValue : int.MaxValue;
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
                CompleteListLevelText(state.ListLevelTextCapture, state.ListLevelTextIsOverride);
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
                        _listOverrideStartAt.ContainsKey((state.ListOverride, level)) &&
                        !IsContinuationStartOverride(state.ListOverride, level);
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

        private Color? ResolveColor(int index)
        {
            if (index < 0 || index >= _colors.Count)
            {
                return null;
            }

            return _colors[index];
        }

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
            // Tab stops are never inherited through \pard; a paragraph declares
            // its stops explicitly. This lets a paragraph clear default stops.
            state.ParagraphFormat = _defaultParagraphFormat with { TabStops = [] };
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

        private FormatException Error(int position, string message) =>
            new($"Invalid RTF at character {position}: {message}");
    }
}
