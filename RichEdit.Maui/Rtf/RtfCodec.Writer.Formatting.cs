namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed partial class Writer
    {
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

            // Tab stops are always written explicitly. RTF cannot express "no tab
            // stops" against an inherited default, so \pard clears stops and every
            // paragraph that has stops declares them.
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
                if (border.Sides == RichTextBorderSides.None ||
                    border.Style == RichTextBorderStyle.None)
                {
                    WriteClearedBorder();
                }
                else
                {
                    WriteBorder(border);
                }
            }
        }

        private void WriteClearedBorder() =>
            _output.Append(@"\brdrt\brdrnil\brdrl\brdrnil\brdrb\brdrnil\brdrr\brdrnil");

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
    }
}
