using System.Globalization;
using System.Collections.Immutable;
using System.Text;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private void Control(string word, int? number)
    {
        if (_state.Destination == "skip") return;
        if (_structuralGap && word != "par") throw Error("A structural RTF gap must be followed by its paragraph mark");
        var n = number ?? (word == "charscalex" ? 100 : word is "up" or "dn" ? 6 : 1);
        if (Destination(word, number)) { _state.AtStart = false; return; }
        if (_state.AtStart && _state.Ignorable && word != "remodel" && !(word == "cs" && _state.Destination == "stylesheet"))
        {
            Report(word, $"The unsupported ignorable destination '{word}' was omitted.");
            _state.Destination = "skip";
            return;
        }
        _state.AtStart = false;
        switch (word)
        {
            case "rtf": if (_states.Count != 1 || n != 1) throw Error("Unsupported RTF version or misplaced header"); _rtf = true; return;
            case "remodel": if (n != 1) throw Error("Unsupported authored RTF metadata version"); _semantic = true; return;
            case "redate":
                if (_state.RevisionRecord is not { Date: null } dated) throw Error("Duplicate or misplaced revision date");
                dated.Date = n; return;
            case "recd": case "repd":
                if (_state.RevisionRecord is not { } defaults || n is not (0 or 1)) throw Error("Invalid retained default-format flag");
                if (word == "recd")
                { if (defaults.CharacterDefaults is not null) throw Error("Duplicate retained default-format flag"); defaults.CharacterDefaults = n == 1; }
                else
                { if (defaults.ParagraphDefaults is not null) throw Error("Duplicate retained default-format flag"); defaults.ParagraphDefaults = n == 1; }
                return;
            case "ansi": _state.CodePage = 1252; return;
            case "mac": _state.CodePage = 10000; return;
            case "pc": _state.CodePage = 437; return;
            case "pca": _state.CodePage = 850; return;
            case "ansicpg": _state.CodePage = n; return;
            case "uc": if (n is < 0 or > 255) throw Error("Invalid Unicode fallback count"); _state.UnicodeSkip = n; return;
            case "u": if (n is < short.MinValue or > ushort.MaxValue) throw Error("Invalid Unicode code unit"); Text(((char)(ushort)n).ToString()); _fallback = _state.UnicodeSkip; return;
            case "tab": if (_fallback > 0) _fallback--; else Text("\t"); return;
            case "line": if (_state.Capture is not null) Text("\u2028"); else SoftBreak(); return;
            case "par":
                if (_structuralGap) { _structuralGap = false; return; }
                if (_state.Capture is not null) Text("\n"); else EndParagraph(); return;
            case "emdash": Text("—"); return;
            case "endash": Text("–"); return;
            case "bullet": Text("•"); return;
            case "lquote": Text("‘"); return;
            case "rquote": Text("’"); return;
            case "ldblquote": Text("“"); return;
            case "rdblquote": Text("”"); return;
            case "emspace": Text("\u2003"); return;
            case "enspace": Text("\u2002"); return;
            case "qmspace": Text("\u2005"); return;
            case "red": _state.Red = Component(n); _state.ColorSpecified = true; return;
            case "green": _state.Green = Component(n); _state.ColorSpecified = true; return;
            case "blue": _state.Blue = Component(n); _state.ColorSpecified = true; return;
            case "f":
                _state.Font = n;
                if (_state.Destination == "fonttbl") { _state.Capture ??= new(); return; }
                if (!_fonts.TryGetValue(n, out var font)) { Report("font-reference", $"Font {n} is missing; the view font will be used."); return; }
                _state.Character = _state.Character with { FontFamily = font.Name }; return;
            case "fcharset":
                _state.FontCodePage = n switch { 0 or 1 => 1252, 2 => 42, 77 => 10000, 128 => 932, 129 => 949, 130 => 1361, 134 => 936,
                    136 => 950, 161 => 1253, 162 => 1254, 163 => 1258, 177 => 1255, 178 => 1256, 186 => 1257, 204 => 1251, 222 => 874, 238 => 1250, 255 => 437, _ => _state.CodePage }; return;
            case "cpg": _state.FontCodePage = n; return;
            case "plain":
                _state.Character = RtfStyleState.Restore(_semantic ? new ResolvedCharacterStyle() : ExternalCharacterDefaults().Apply(new()), 0, 0, 0);
                _state.CharacterStyle = null; _state.Font = _defaultFont; return;
            case "deff":
                if (n < 0) throw Error("Invalid default font identifier");
                _defaultFont = n; _state.Font = n; return;
            case "pard":
                _state.Paragraph = RtfStyleState.Restore(new ResolvedParagraphStyle(), 0, 0, 0);
                _state.ParagraphCharacter = new(); _state.ParagraphStyle = null;
                _state.LineSpacing = 0; _state.LineMultiple = false;
                _state.TabAlignment = RichTabAlignment.Left; _state.TabLeader = RichTabLeader.None;
                _state.BorderSide = RichBorderSides.None; _state.Borders = _state.Borders.Clear();
                _state.InTable = false; _state.ListOverride = _state.ListLevel = 0; return;
            case "s": case "cs":
                if (_state.Destination == "stylesheet") { _state.StyleId = n; _state.CharacterDefinition = word == "cs"; _state.Capture ??= new(); return; }
                if (n == 0 && !_styles.ContainsKey(0)) return;
                if (!_styles.TryGetValue(n, out var style)) { Report("style-reference", $"Style {n} is missing."); return; }
                if (word == "s") _state.ParagraphStyle = style.Id; else _state.CharacterStyle = style.Id;
                if (!_semantic)
                {
                    var sheet = new RichStyleSheet(_styles.Values);
                    _state.Character = RtfStyleState.Restore(sheet.ResolveCharacter(_state.ParagraphStyle, _state.CharacterStyle, defaults: ExternalCharacterDefaults()), 0, 0, 0);
                    if (word == "s")
                    {
                        var paragraph = sheet.ResolveParagraph(style.Id);
                        _state.Paragraph = RtfStyleState.Restore(paragraph, 0, 0, 0);
                        _state.LineSpacing = paragraph.LineSpacing.RtfValue;
                        _state.LineMultiple = paragraph.LineSpacing.Kind == RichLineSpacingKind.Multiple;
                        _state.Borders = paragraph.Border is { } border
                            ? BorderEdges.Where(side => (border.Sides & side) != 0).ToImmutableDictionary(side => side, side => border with { Sides = side })
                            : _state.Borders.Clear();
                        _state.BorderSide = RichBorderSides.None;
                    }
                }
                return;
            case "sbasedon": _state.BasedOn = n; return;
            case "snext": _state.NextStyle = n; return;
            case "reinherit": _state.Inherit = n; return;
            case "reprops": if (n is < 1 or > 32) throw Error("Invalid cascade property count"); _state.PropertyCount = n; return;
            case "rebgcolor": _state.Paragraph = _state.Paragraph with { Background = Color(n) }; return;
            case "reinitial": _state.Initial = n; return;
            case "renull": _state.Null = n; return;
            case "rereverse": _state.Reverse = n != 0; return;
            case "reanchor": _state.AnchorAffinity = (TextEdit.Core.CaretAffinity)n; return;
            case "reactive": _state.ActiveAffinity = (TextEdit.Core.CaretAffinity)n; return;
            case "intbl": _state.InTable = n != 0; return;
            case "itap": if (n is < 0 or > 64) throw Error("Unsupported table depth"); _state.TableLevel = n; return;
            case "trowd": BeginRow(); return;
            case "cellx": CellBoundary(n); return;
            case "clmgf": case "clvmgf": return;
            case "clmrg": if (_tables.Count > 0) _tables[^1].HorizontalContinue = true; return;
            case "clvmrg": if (_tables.Count > 0) _tables[^1].VerticalContinue = true; return;
            case "cell": EndCell(1); return;
            case "nestcell": EndCell(Math.Max(2, _state.TableLevel)); return;
            case "row": EndRow(1); return;
            case "nestrow": EndRow(Math.Max(2, _state.TableLevel)); return;
            case "listid":
                _state.ListId = n;
                if (_state.Destination == "listtable" && _state.List is { } list) { ChargeResource(); _listDefinitions[n] = list; }
                return;
            case "ls": _state.ListOverride = n; return;
            case "ilvl": if (n is < 0 or > 8) throw Error("RTF list levels must be from zero through eight"); _state.ListLevel = n; return;
            case "levelnfc": case "levelnfcn":
                if ((_state.ListLevelDefinition ?? _state.List?.Default) is { } definition)
                {
                    definition.Numbered = n != 23;
                    definition.NumberStyle = n switch { 1 => RichListNumberingStyle.UpperRoman, 2 => RichListNumberingStyle.LowerRoman,
                        3 => RichListNumberingStyle.UpperLetter, 4 => RichListNumberingStyle.LowerLetter, _ => RichListNumberingStyle.Decimal };
                }
                if (n is not (>= 0 and <= 4 or 23)) Report("number-format", "A list numbering format was approximated as decimal numbering."); return;
            case "levelstartat": if (n < 0) throw Error("Invalid list start"); if ((_state.ListLevelDefinition ?? _state.List?.Default) is { } start) start.Start = n; return;
            case "pngblip": SetPictureKind(RichImageKind.Png); return;
            case "jpegblip": SetPictureKind(RichImageKind.Jpeg); return;
            case "dibitmap": SetPictureKind(RichImageKind.Dib); return;
            case "emfblip": SetPictureKind(RichImageKind.Emf); return;
            case "wmetafile": SetPictureKind(RichImageKind.Wmf); return;
            case "picwgoal": if (_state.Picture is { } w) w.Width = n; return;
            case "pichgoal": if (_state.Picture is { } h) h.Height = n; return;
            case "picw": if (_state.Picture is { } pw) pw.PixelWidth = n; return;
            case "pich": if (_state.Picture is { } ph) ph.PixelHeight = n; return;
            case "picscalex": case "picscaley": if (n != 100) Report("picture-scale", "Picture scaling was not applied to the authored dimensions."); return;
            case "atndate": if (_state.Annotation is { } annotation) annotation.Date = n; return;
            case "adeff": case "deflang": case "deflangfe": case "viewkind": case "viewscale": case "fet":
            case "fnil": case "froman": case "fswiss": case "fmodern": case "fscript": case "fdecor": case "ftech": case "fbidi": case "fprq":
            case "additive": case "sautoupd": case "slocked": case "shidden": case "slink": case "styrsid":
            case "trleft": case "trautofit": case "trftsWidth": case "trwWidth": case "clftsWidth": case "clwWidth":
            case "listtemplateid": case "listsimple": case "listoverridecount": case "leveljc": case "leveljcn": case "levelfollow":
            case "loch": case "hich": case "dbch": case "ltrdoc": case "rtldoc":
                return;
        }
        if (CellFormatControl(word, n) || CharacterControl(word, n) || ParagraphControl(word, n)) return;
        Report(word, $"The unsupported RTF control '{word}' was ignored.");
    }

    private int Component(int value) => value is >= 0 and <= 255 ? value : throw Error("Invalid color component");

    private bool Destination(string word, int? number)
    {
        switch (word)
        {
            case "rerevisions":
                if (_retained || number != 1 || _revisionEnvelope) throw Error("Invalid or nested revision envelope");
                _revisionEnvelope = true; SetDestination(word); _state.Capture = null; return true;
            case "rerevision":
                if (_state.Destination != "rerevisions") throw Error("A revision requires its envelope");
                ChargeResource(); SetDestination(word); _state.Capture = null; _state.RevisionRecord = new(); return true;
            case "revid": case "regroup": case "reauthor": case "redeps": case "renchanged": case "reschanged":
            case "reachanged": case "rebchanged": case "recchanged": case "rebeforeblocks": case "reafterblocks":
                if (_state.RevisionRecord is null) throw Error("Retained revision metadata is outside a revision");
                SetDestination(word); _state.Capture = new(); return true;
            case "reids": case "reanchorid": SetDestination(word); _state.Capture = new(); return true;
            case "retext": SetDestination(word); _state.Capture = null; return true;
            case "remodel": return false;
            case "fonttbl": case "stylesheet":
                SetDestination(word); _state.Capture = new(); _state.Font = _state.StyleId = -1; return true;
            case "colortbl": SetDestination(word); _state.Capture = null; _state.Red = _state.Green = _state.Blue = 0; _state.ColorSpecified = false; return true;
            case "defchp": case "defpap": SetDestination(word); _state.Character = new(); _state.Paragraph = new(); _state.Capture = null; return true;
            case "rec": case "rep": case "repch":
                SetDestination(word); _state.Inherit = _state.Initial = _state.Null = 0; _state.PropertyCount = null; _state.Capture = null; return true;
            case "listtable": case "listoverridetable": SetDestination(word); _state.Capture = null; return true;
            case "list": if (_state.Destination != "listtable") return false; _state.List = new(); _state.ListLevelDefinition = null; return true;
            case "listlevel":
                if (_state.Destination != "listtable" || _state.List is null) return false;
                if (_state.List.Levels.Count == 9) throw Error("RTF lists support at most nine levels");
                ChargeResource();
                _state.ListLevelDefinition = new(); _state.List.Levels.Add(_state.ListLevelDefinition); return true;
            case "listoverride": if (_state.Destination != "listoverridetable") return false; SetDestination(word); return true;
            case "relistbegin": case "relistitem": case "relistend": SetDestination(word); _state.Capture = null; return true;
            case "nesttableprops": SetDestination(word); _state.Capture = null; return true;
            case "reasset": SetDestination(word); _state.ResourceOnly = true; return true;
            case "reorphan": SetDestination(word); _state.Orphan = true; return true;
            case "regap": SetDestination(word); return true;
            case "pict": SetDestination(word); _state.Picture = new(); _state.Capture = null; return true;
            case "blipuid": case "sn": case "sv": SetDestination(word); _state.Capture = new(); return true;
            case "picprop": case "sp": SetDestination(word); return true;
            case "reemptyalt": if (_state.Picture is { } picture) picture.Alt = ""; SetDestination(word); return true;
            case "bkmkstart": case "bkmkend": case "atrfstart": case "atrfend":
                SetDestination(word); _state.Capture = new(); return true;
            case "annotation": SetDestination(word); _state.Annotation = new(); _state.Capture = _state.Annotation.Text; return true;
            case "atnid": case "atnauthor": SetDestination(word); _state.Capture = new(); return true;
            case "reutc": if (_state.Annotation is { } annotation) annotation.Utc = true; SetDestination(word); _state.Capture = null; return true;
            case "field": SetDestination(word); _state.Field = new(); _state.Capture = null; return true;
            case "fldinst": SetDestination(word); _state.Capture = new(); return true;
            case "fldrslt":
                SetDestination(word);
                if (_state.Field is not { } field) throw Error("A field result has no field");
                if (!field.Atom && RtfFieldSyntax.TryHyperlink(field.Instruction, out var link))
                { field.IsLink = true; _state.Link = link; _state.Capture = null; }
                else
                {
                    if (!field.Atom && field.Instruction.TrimStart().StartsWith("HYPERLINK ", StringComparison.OrdinalIgnoreCase))
                        Report("hyperlink-options", "A hyperlink with unsupported options was retained as a field atom.");
                    _state.Capture = field.Result;
                }
                return true;
            case "refieldatom": if (_state.Field is { } atom) atom.Atom = true; SetDestination(word); return true;
            case "leveltext": case "levelnumbers": case "listtext": case "pntext":
            case "generator": case "rsidtbl": case "filetbl": case "latentstyles":
                SetDestination("skip"); return true;
            case "info": case "header": case "footer": case "headerl": case "headerr": case "footerl": case "footerr":
            case "footnote": case "object": case "shp": case "nonshppict": case "themedata": case "colorschememapping":
                Report(word, $"The unsupported destination '{word}' was omitted."); SetDestination("skip"); return true;
            case "shppict": return true;
        }
        return false;
    }

    private void SetDestination(string destination) { _state.Destination = destination; _state.OwnDestination = true; }

    private void FinishEntry()
    {
        switch (_state.Destination)
        {
            case "fonttbl":
                if (_state.Font < 0) return;
                var name = _state.Capture?.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name)) throw Error("Empty font family");
                ChargeResource();
                _fonts[_state.Font] = (name, _state.FontCodePage);
                _state.Capture = new(); _state.Font = -1; return;
            case "colortbl":
                ChargeResource();
                _colors.Add(_state.ColorSpecified ? new((byte)_state.Red, (byte)_state.Green, (byte)_state.Blue) : null);
                _state.Red = _state.Green = _state.Blue = 0; _state.ColorSpecified = false; return;
            case "stylesheet":
                var styleName = _state.Capture?.ToString().Trim();
                if (string.IsNullOrWhiteSpace(styleName))
                {
                    if (_state.StyleId < 0) return;
                    throw Error("Empty style name");
                }
                // RTF permits the default paragraph style to omit \s0. Its
                // formatting and name still define the base for \sbasedon0.
                var styleNumber = Math.Max(0, _state.StyleId);
                ReportBorderFidelity(_state);
                var id = styleNumber == 0 ? short.MaxValue : styleNumber;
                ChargeResource();
                _styles[styleNumber] = new(new(id), styleName, _state.CharacterDefinition ? RichStyleKind.Character : RichStyleKind.Paragraph)
                {
                    BasedOn = _state.BasedOn is < 0 or 222 ? null : new(_state.BasedOn == 0 ? short.MaxValue : _state.BasedOn),
                    // Some producers emit \snext on character styles too;
                    // it only governs the paragraph style after a paragraph break.
                    NextStyle = _state.CharacterDefinition || _state.NextStyle < 0 ? null : new(_state.NextStyle == 0 ? short.MaxValue : _state.NextStyle),
                    Character = _state.Character, Paragraph = _state.CharacterDefinition ? new() : _state.Paragraph,
                };
                _state.Capture = new(); _state.StyleId = -1; return;
        }
    }

    private void EndGroup(State child)
    {
        if (!child.OwnDestination) return;
        switch (child.Destination)
        {
            case "revid": case "regroup": case "reauthor": case "redeps": case "renchanged": case "reschanged":
            case "reachanged": case "rebchanged": case "recchanged": case "rebeforeblocks": case "reafterblocks":
                ReadRevisionMetadata(child.Destination, child.Capture?.ToString() ?? "", child.RevisionRecord!); break;
            case "rerevision": _revisionRecords.Add(child.RevisionRecord!); break;
            case "reids": ReadNodeIdentities(child.Capture?.ToString() ?? ""); break;
            case "reanchorid": _state.WireAnchor = RtfIdentityMap.Number((child.Capture?.ToString() ?? "").AsSpan()); break;
            case "retext": _newTextRun = true; break;
            case "rec": _state.Character = RtfStyleState.Restore(child.Character.Apply(new()), InheritMask(child, false), child.Initial, child.Null); break;
            case "rep":
                _state.Paragraph = RtfStyleState.Restore(child.Paragraph.Apply(new()), InheritMask(child, true), child.Initial, child.Null);
                if (_state.Destination == "body")
                { var paragraph = EnsureParagraph(); paragraph.Format = _state.Paragraph; paragraph.Style = _state.ParagraphStyle; }
                break;
            case "repch":
                _state.ParagraphCharacter = RtfStyleState.Restore(child.Character.Apply(new()), InheritMask(child, false), child.Initial, child.Null);
                if (_state.Destination == "body") EnsureParagraph().Character = _state.ParagraphCharacter;
                break;
            case "defchp": _defaultCharacter = child.Character; break;
            case "defpap": _defaultParagraph = child.Paragraph; break;
            case "regap":
                if (_paragraph is not null) throw Error("A structural gap cannot split an authored paragraph");
                CloseTables(_state.InTable ? Math.Max(1, _state.TableLevel) : 0);
                _structuralGap = true;
                break;
            case "listoverride": ChargeResource(); _listOverrides[child.ListOverride] = child.ListId; break;
            case "relistbegin": BeginList(child.ListOverride); break;
            case "relistitem": ListItem(); break;
            case "relistend": EndList(); break;
            case "pict": FinishPicture(child); break;
            case "blipuid": if (child.Picture is { } picture) picture.Name = child.Capture?.ToString().Trim(); break;
            case "sn": if (child.Picture is { } property) property.Property = child.Capture?.ToString(); break;
            case "sv": if (child.Picture is { Property: "wzDescription" } value) value.Alt = child.Capture?.ToString(); break;
            case "bkmkstart": StartAnchor(child, _bookmarkStarts); break;
            case "atrfstart": StartAnchor(child, _commentStarts); break;
            case "bkmkend": EndAnchor(child, bookmark: true); break;
            case "atrfend": EndAnchor(child, bookmark: false); break;
            case "atnid": if (child.Annotation is { } name) name.Name = child.Capture?.ToString() ?? ""; break;
            case "atnauthor": if (child.Annotation is { } author) author.Author = child.Capture?.ToString() ?? ""; break;
            case "annotation": FinishAnnotation(child); break;
            case "fldinst": if (child.Field is { } field) field.Instruction = child.Capture?.ToString() ?? ""; break;
            case "field": FinishField(child); break;
        }
    }

    private int InheritMask(State state, bool paragraph)
    {
        var count = state.PropertyCount ?? (paragraph ? 12 : 18);
        if (count > (paragraph ? RtfStyleState.ParagraphPropertyCount : RtfStyleState.CharacterPropertyCount))
            Report("cascade-properties", "The document declares additional unsupported cascade properties.");
        return state.Inherit | (count == 32 ? 0 : unchecked((int)(uint.MaxValue << count)));
    }
}
