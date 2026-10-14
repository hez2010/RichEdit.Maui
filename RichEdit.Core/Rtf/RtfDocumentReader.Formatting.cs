using System.Globalization;
using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private bool CharacterControl(string word, int n)
    {
        var format = _state.Character;
        switch (word)
        {
            case "fs": if (n <= 0) throw Error("Invalid font size"); format = format with { FontSize = new RichFontSize(n) }; break;
            case "b": format = format with { Bold = n != 0 }; break;
            case "i": format = format with { Italic = n != 0 }; break;
            case "ulnone": format = format with { Underline = RichUnderline.None }; break;
            case "ul": case "uldb": case "uld": case "uldash": case "ulwave": case "ulw":
            case "uldashd": case "uldashdd": case "ulth": case "ululdbwave": case "ulhwave": case "ulldash":
                format = format with { Underline = n == 0 ? RichUnderline.None : word switch { "uldb" => RichUnderline.Double, "uld" => RichUnderline.Dotted,
                    "uldash" => RichUnderline.Dashed, "ulwave" => RichUnderline.Wave, "ulw" => RichUnderline.Words,
                    "uldashd" => RichUnderline.DashDot, "uldashdd" => RichUnderline.DashDotDot, "ulth" => RichUnderline.Thick,
                    "ululdbwave" => RichUnderline.DoubleWave, "ulhwave" => RichUnderline.HeavyWave, "ulldash" => RichUnderline.LongDash,
                    _ => RichUnderline.Single } }; break;
            case "ulc": format = format with { UnderlineColor = Color(n) }; break;
            case "strike": if (n != 0 || format.Strike.ApplyValue() != RichStrike.Double) format = format with { Strike = n == 0 ? RichStrike.None : RichStrike.Single }; break;
            case "striked": if (n != 0) format = format with { Strike = RichStrike.Double }; else if (format.Strike.ApplyValue() == RichStrike.Double) format = format with { Strike = RichStrike.None }; break;
            case "cf": format = format with { Foreground = Color(n) }; break;
            case "cb":
            case "highlight": format = format with { Background = Color(n) }; break;
            case "chshdng": case "chcfpat": case "chcbpat":
                format = format with { Shading = ReadShading(format.Shading.ApplyValue(), word, n) }; break;
            case "super": format = format with { Script = RichScript.Superscript }; break;
            case "sub": format = format with { Script = RichScript.Subscript }; break;
            case "nosupersub": format = format with { Script = RichScript.Normal }; break;
            case "up": case "dn":
                if (n == int.MinValue) throw Error("Invalid baseline offset");
                format = format with { VerticalPosition = RichVerticalPosition.Offset(word == "dn" ? -n : n) }; break;
            case "expndtw": format = format with { CharacterSpacing = new RichLength(n) }; break;
            case "charscalex":
                if (n < 0) throw Error("Character scaling cannot be negative");
                if (n == 0) Report("character-scale", "Zero character scaling was normalized to 100 percent, as used by the native RichEdit exporter.");
                format = format with { HorizontalScalePercent = n == 0 ? 100 : n }; break;
            case "expnd": format = format with { CharacterSpacing = new RichLength(checked(n * 5)) }; break;
            case "scaps": format = format with { SmallCaps = n != 0 }; break;
            case "caps": format = format with { AllCaps = n != 0 }; break;
            case "outl": format = format with { Outline = n != 0 }; break;
            case "shad": format = format with { Shadow = n != 0 }; break;
            case "v": format = format with { Hidden = n != 0 }; break;
            case "lang": case "langnp": format = format with { Language = Language(n) }; break;
            case "rtlch": format = format with { Direction = RichDirection.RightToLeft }; break;
            case "ltrch": format = format with { Direction = RichDirection.LeftToRight }; break;
            default: return false;
        }
        if (_state.Destination == "fldrslt" && _state.Capture is not null && _state.Character != format)
            Report("field-result-format", "Formatting inside a field result was reduced to the field's outer character style.");
        _state.Character = format;
        return true;
    }

    private string? Language(int id)
    {
        if (id is 0 or 1024) return null;
        try { return CultureInfo.GetCultureInfo(id).Name; }
        catch (CultureNotFoundException) { Report("language", $"Language identifier {id} is unavailable."); return null; }
    }

    private void ResetParagraph(ResolvedParagraphStyle paragraph)
    {
        _state.Paragraph = RtfStyleState.Restore(paragraph, 0, 0, 0);
        _state.LineSpacing = paragraph.LineSpacing.RtfValue;
        _state.LineMultiple = paragraph.LineSpacing.Kind == RichLineSpacingKind.Multiple;
        _state.Borders = paragraph.Border is { } border
            ? BorderEdges.Where(side => (border.Sides & side) != 0).ToImmutableDictionary(side => side, side => border with { Sides = side })
            : _state.Borders.Clear();
        _state.BorderSide = RichBorderSides.None;
    }

    private bool ParagraphControl(string word, int n)
    {
        if (BorderControl(word, n)) return true;
        var format = _state.Paragraph;
        switch (word)
        {
            case "ql": format = format with { Alignment = RichAlignment.Left }; break;
            case "qr": format = format with { Alignment = RichAlignment.Right }; break;
            case "qc": format = format with { Alignment = RichAlignment.Center }; break;
            case "qj": format = format with { Alignment = RichAlignment.Justify }; break;
            case "qd": format = format with { Alignment = RichAlignment.Distributed }; break;
            case "tql": _state.TabAlignment = RichTabAlignment.Left; return true;
            case "tqc": _state.TabAlignment = RichTabAlignment.Center; return true;
            case "tqr": _state.TabAlignment = RichTabAlignment.Right; return true;
            case "tqdec": _state.TabAlignment = RichTabAlignment.Decimal; return true;
            case "tldot": _state.TabLeader = RichTabLeader.Dots; return true;
            case "tlhyph": _state.TabLeader = RichTabLeader.Hyphens; return true;
            case "tlul": _state.TabLeader = RichTabLeader.Underline; return true;
            case "tlth": _state.TabLeader = RichTabLeader.ThickLine; return true;
            case "tleq": _state.TabLeader = RichTabLeader.Equals; return true;
            case "tx":
                ChargeResource();
                if (n <= 0) Report("tab-position", "Tab stops at or before the paragraph origin are outside the supported layout and were omitted.");
                else format = format with { TabStops = (format.TabStops.ApplyValue() ?? RichTabStops.Empty).Set(new(new(n), _state.TabAlignment, _state.TabLeader)) };
                _state.TabAlignment = RichTabAlignment.Left; _state.TabLeader = RichTabLeader.None; break;
            case "hyphpar": format = format with { Hyphenation = n != 0 }; break;
            case "sl":
                if (n == int.MinValue) throw Error("Invalid line spacing");
                _state.LineSpacing = n;
                format = format with { LineSpacing = RichLineSpacing.FromRtf(n, _state.LineMultiple) }; break;
            case "slmult":
                _state.LineMultiple = n != 0;
                if (_state.LineMultiple && _state.LineSpacing < 0) Report("line-spacing", "A negative multiple line height was normalized to a positive factor.");
                format = format with { LineSpacing = RichLineSpacing.FromRtf(_state.LineSpacing, _state.LineMultiple) }; break;
            case "rtlpar": format = format with { Direction = RichDirection.RightToLeft }; break;
            case "ltrpar": format = format with { Direction = RichDirection.LeftToRight }; break;
            case "li": case "lin": format = format with { LeadingIndent = new RichLength(n) }; break;
            case "ri": case "rin": format = format with { TrailingIndent = new RichLength(n) }; break;
            case "fi": format = format with { FirstLineIndent = new RichLength(n) }; break;
            case "sb": format = format with { SpaceBefore = new RichLength(Math.Max(0, n)) }; if (n < 0) Report("space-before", "Negative paragraph spacing was clamped to zero."); break;
            case "sa": format = format with { SpaceAfter = new RichLength(Math.Max(0, n)) }; if (n < 0) Report("space-after", "Negative paragraph spacing was clamped to zero."); break;
            case "keep": format = format with { KeepTogether = n != 0 }; break;
            case "keepn": format = format with { KeepWithNext = n != 0 }; break;
            case "widctlpar": format = format with { WidowControl = n != 0 }; break;
            case "nowidctlpar": format = format with { WidowControl = false }; break;
            case "outlinelevel": if (n is < 0 or > 9) throw Error("Invalid heading level"); format = format with { HeadingLevel = n == 9 ? null : n + 1 }; break;
            case "shading": case "cfpat": case "cbpat":
                var shade = ReadShading(format.Shading.ApplyValue(), word, n);
                format = format with { Shading = shade, Background = shade.SolidColor }; break;
            default: return false;
        }
        if (_state.Destination == "fldrslt" && _state.Capture is not null && _state.Paragraph != format)
            Report("field-result-format", "Paragraph formatting inside a field result was omitted.");
        _state.Paragraph = format;
        return true;
    }

    private RichColor? Color(int index)
    {
        if (index == 0 && _colors.Count == 0) return null;
        if (index < 0 || index >= _colors.Count) { Report("color-reference", $"Color {index} is missing; the default color was used."); return null; }
        return _colors[index];
    }

    private RichShading ReadShading(RichShading? previous, string control, int value)
    {
        var shade = previous ?? default;
        if (control is "shading" or "chshdng")
        {
            if (value is < 0 or > 10000) throw Error("Invalid shading coverage");
            return new(value, shade.Foreground, shade.Background);
        }
        return control is "cfpat" or "chcfpat" ? new(shade.Coverage, Color(value), shade.Background) :
            new(shade.Coverage, shade.Foreground, Color(value));
    }
}

internal static class RtfStyleValueExtensions
{
    internal static T? ApplyValue<T>(this StyleValue<T> value) => value.Kind == StyleValueKind.Value ? value.Value : default;
}
