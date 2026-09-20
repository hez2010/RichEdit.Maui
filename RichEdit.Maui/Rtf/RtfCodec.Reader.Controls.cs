using System.Globalization;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Reader
    {
        private void ApplyControl(string word, int? parameter, ReaderState state, int controlStart)
        {
            if (word == "ud" &&
                state.AtGroupStart &&
                state.SkipDestination &&
                state.InUnicodePreferred)
            {
                // The \*\ud destination inside \upr carries the Unicode
                // representation, which replaces the skipped ANSI alternative.
                state.SkipDestination = false;
                state.IgnorableDestination = false;
                state.InUnicodePreferred = false;
                state.AtGroupStart = false;
                return;
            }

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
                // The ANSI charset defaults to Windows-1252 until \ansicpg refines it.
                SetDocumentCodePage(state, AnsiCodePage);
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
                // int.MinValue half-points cannot round-trip through the model's
                // point representation; saturate to the representable magnitude.
                var halfPoints = Math.Max(parameter ?? 6, -int.MaxValue);
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

            if (word == "itap")
            {
                state.TableLevel = Math.Max(parameter ?? 1, 1);
                _tableRowStarts.TryAdd(state.TableLevel, _document.Length);
                return;
            }

            if (word is "trowd" or "intbl")
            {
                _tableRowStarts.TryAdd(state.TableLevel, _document.Length);
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
                // Tab stops are declared per paragraph and never inherited
                // through \pard, so a paragraph can clear default stops.
                state.ParagraphFormat = state.Destination == Destination.DefaultParagraphProperties
                    ? RichTextParagraphFormat.Default
                    : _defaultParagraphFormat with { TabStops = [] };
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
                    // An explicit border-none survives a document default border,
                    // unlike a null border, which inherits it.
                    SetParagraphFormat(state, state.ParagraphFormat with
                    {
                        Border = new RichTextBorder(
                            RichTextBorderSides.None,
                            RichTextBorderStyle.None,
                            0),
                    });
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
    }
}
