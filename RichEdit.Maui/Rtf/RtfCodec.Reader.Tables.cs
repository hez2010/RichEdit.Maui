using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
    {
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
                case "leveltext" when state.Destination is
                    Destination.ListTable or Destination.ListOverrideTable:
                    state.ListLevelTextIsOverride =
                        state.Destination == Destination.ListOverrideTable;
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
                    AppendPendingCellSeparator(state.Format);
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
                case "upr":
                    // Skip the ANSI alternative; the paired \*\ud destination
                    // supplies the preferred Unicode representation.
                    state.SkipDestination = true;
                    state.InUnicodePreferred = true;
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

            ApplyListLayoutControl(word, parameter);
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
                    _pendingListPictureIndex,
                    _pendingListLeadingIndent,
                    _pendingListFirstLineIndent,
                    _pendingListMarkerTab);
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
            _pendingListLeadingIndent = null;
            _pendingListFirstLineIndent = null;
            _pendingListMarkerTab = null;
        }

        private void ApplyListLayoutControl(string word, int? parameter)
        {
            if (word is "li" or "lin")
                _pendingListLeadingIndent = FromTwips(parameter ?? 0);
            else if (word == "fi")
                _pendingListFirstLineIndent = FromTwips(parameter ?? 0);
            else if (word == "tx")
                _pendingListMarkerTab ??= Math.Max(0, FromTwips(parameter ?? 0));
        }

        private void CompleteListLevelText(StringBuilder capture, bool isOverride)
        {
            var pendingLevel = isOverride ? _pendingOverrideLevel : _pendingListLevel;
            if (pendingLevel is < 0 or >= 9 || capture.Length == 0)
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

            if (isOverride)
            {
                CommitPendingOverrideLevel();
            }
            else
            {
                CommitPendingListLevel();
                CommitListDefinition();
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
                _pendingOverrideLevel = -1;
                _pendingOverrideStartAt = false;
                // Prevent trailing list-table state from leaking into override
                // levels committed by the shared pending-level machinery.
                _pendingListId = null;
                _pendingListLevel = -1;
                Array.Clear(_pendingListLevels);
                ResetPendingListLevel();
                return;
            }

            if (word == "lfolevel")
            {
                CommitPendingOverrideLevel();
                _pendingOverrideLevel++;
                _pendingOverrideStartAt = false;
                ResetPendingListLevel();
                return;
            }

            if (word == "listlevel")
            {
                // A nested \listlevel redefines the complete lfolevel format.
                ResetPendingListLevel();
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
            else if (word == "levelstartat" && parameter is > 0)
            {
                _pendingListStartAt = parameter.Value;
                if (_pendingOverrideStartAt &&
                    _pendingOverrideId is { } startOverrideId &&
                    _pendingOverrideLevel is >= 0 and < 9)
                {
                    _listOverrideStartAt[(startOverrideId, _pendingOverrideLevel)] = parameter.Value;
                }
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
            else if (word == "levelpicture" && parameter is >= 0)
            {
                _pendingListPictureIndex = parameter.Value;
            }

            if (_pendingOverrideListId is { } listId && _pendingOverrideId is { } overrideId)
            {
                _listOverrides[overrideId] = listId;
            }

            ApplyListLayoutControl(word, parameter);
            CommitPendingOverrideLevel();
        }

        private void CommitPendingOverrideLevel()
        {
            if (_pendingOverrideId is { } overrideId &&
                _pendingOverrideLevel is >= 0 and < 9 &&
                _pendingListKind is { } kind)
            {
                _listOverrideLevels[(overrideId, _pendingOverrideLevel)] = new ParsedListDefinition(
                    kind,
                    _pendingListStartAt,
                    _pendingListNumberFormat,
                    _pendingListPrefix,
                    _pendingListSuffix,
                    _pendingListBulletText,
                    _pendingListPictureIndex,
                    _pendingListLeadingIndent,
                    _pendingListFirstLineIndent,
                    _pendingListMarkerTab);
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
    }
}
