using System.Globalization;
using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
    {
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
            if (!TryReadFieldToken(instruction, ref position, out var fieldName, out _) ||
                !fieldName.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? fragment = null;
            while (TryReadFieldToken(instruction, ref position, out var token, out var isSwitch))
            {
                if (isSwitch &&
                    token.Equals(@"\l", StringComparison.OrdinalIgnoreCase) &&
                    TryReadFieldToken(instruction, ref position, out var bookmark, out _))
                {
                    fragment = bookmark;
                }
                else if (isSwitch &&
                         token.Equals(@"\o", StringComparison.OrdinalIgnoreCase) &&
                         TryReadFieldToken(instruction, ref position, out var parsedToolTip, out _))
                {
                    toolTip = parsedToolTip;
                }
                else if (!isSwitch && target.Length == 0)
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
            if (!TryReadFieldToken(instruction, ref position, out var fieldName, out _) ||
                !fieldName.Equals("SYMBOL", StringComparison.OrdinalIgnoreCase) ||
                !TryReadFieldToken(instruction, ref position, out var characterToken, out _) ||
                !TryParseCharacterNumber(characterToken, out var characterNumber))
            {
                return false;
            }

            string? fontFamily = null;
            double? fontSize = null;
            var encoding = SymbolEncoding.Ansi;
            while (TryReadFieldToken(instruction, ref position, out var token, out _))
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
                         TryReadFieldToken(instruction, ref position, out var parsedFontFamily, out _))
                {
                    fontFamily = parsedFontFamily;
                }
                else if (token.Equals(@"\s", StringComparison.OrdinalIgnoreCase) &&
                         TryReadFieldToken(instruction, ref position, out var sizeToken, out _) &&
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
            out string token,
            out bool isSwitch)
        {
            isSwitch = false;
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
                var builder = new StringBuilder();
                position++;
                while (position < instruction.Length && instruction[position] != '"')
                {
                    var character = instruction[position];
                    if (character == '\\' &&
                        position + 1 < instruction.Length &&
                        instruction[position + 1] is '"' or '\\')
                    {
                        builder.Append(instruction[position + 1]);
                        position += 2;
                        continue;
                    }

                    builder.Append(character);
                    position++;
                }

                token = builder.ToString();
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
            if (token.StartsWith("\\\\", StringComparison.Ordinal))
            {
                // An unquoted escaped path such as \\\\server\\share is a literal
                // argument, not a field switch.
                token = token.Replace("\\\\", "\\", StringComparison.Ordinal);
            }
            else if (token.Length >= 2 && token[0] == '\\' && char.IsAsciiLetter(token[1]))
            {
                isSwitch = true;
            }

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
    }
}
