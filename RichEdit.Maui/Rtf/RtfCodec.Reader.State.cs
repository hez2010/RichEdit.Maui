using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
    {
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
            int? PictureIndex,
            double? LeadingIndent,
            double? FirstLineIndent,
            double? MarkerTab);

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

            public bool TryAppendHex(char character)
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
                    return false;
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

                return true;
            }
        }

        private sealed class ReaderState
        {
            public Destination Destination { get; set; }

            public RichTextCharacterFormat Format { get; set; } = RichTextCharacterFormat.Default;

            public int? FontIndex { get; set; }

            public int UnicodeSkipCount { get; set; } = 1;

            public int CodePage { get; set; } = AnsiCodePage;

            public int? CharacterShading { get; set; }

            public RichTextParagraphFormat ParagraphFormat { get; set; } = RichTextParagraphFormat.Default;

            public int ParagraphLineSpacingTwips { get; set; }

            public bool ParagraphLineSpacingMultiple { get; set; }

            public RichTextTabAlignment PendingTabAlignment { get; set; }

            public RichTextTabLeader PendingTabLeader { get; set; }

            public RichTextBorderSides CurrentBorderSides { get; set; }

            public int ListOverride { get; set; }

            public int ListLevel { get; set; }

            public int TableLevel { get; set; } = 1;

            public bool AtGroupStart { get; set; } = true;

            public bool IgnorableDestination { get; set; }

            public bool SkipDestination { get; set; }

            public bool InUnicodePreferred { get; set; }

            public bool CompletesCapture { get; set; }

            public TextAccumulator? Capture { get; set; }

            public StringBuilder? ListLevelTextCapture { get; set; }

            public bool CompletesListLevelText { get; set; }

            public bool ListLevelTextIsOverride { get; set; }

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
                TableLevel = TableLevel,
                AtGroupStart = true,
                SkipDestination = SkipDestination,
                InUnicodePreferred = InUnicodePreferred,
                Capture = Capture,
                ListLevelTextCapture = ListLevelTextCapture,
                ListLevelTextIsOverride = ListLevelTextIsOverride,
                PicturePropertyCapture = PicturePropertyCapture,
                Field = Field,
                Object = Object,
                FieldInstructionCapture = FieldInstructionCapture,
                Picture = Picture,
            };
        }
    }
}
