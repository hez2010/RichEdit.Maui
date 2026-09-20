using System.Runtime.InteropServices;
using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
    {
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
                    if (!state.Picture!.TryAppendHex(character) &&
                        !char.IsWhiteSpace(character))
                    {
                        throw new FormatException(
                            "Picture data contains an invalid hexadecimal digit.");
                    }

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
            var rowStart = _tableRowStarts.Remove(state.TableLevel, out var start) ? start : _tableRowEnds.GetValueOrDefault(state.TableLevel);
            if (_document.Length == rowStart || _document.LastCharacter != '\n')
            {
                TrackParagraphFormat(state);
                _document.Append('\n', state.Format);
            }

            _lineStart = _document.Length;
            _paragraphListHandled = false;
            _tableRowEnds[state.TableLevel] = _document.Length;
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

        private static int? GetCodePageForCharacterSet(int characterSet) => characterSet switch
        {
            // Character set 0 is the ANSI charset. It follows the document code
            // page declared by \ansicpg rather than a hardcoded 1252.
            0 => null,
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
    }
}
