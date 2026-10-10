using System.Globalization;

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
                format = format with { Underline = n == 0 ? RichUnderline.None : word switch { "uldb" => RichUnderline.Double, "uld" => RichUnderline.Dotted,
                    "uldash" => RichUnderline.Dashed, "ulwave" => RichUnderline.Wave, "ulw" => RichUnderline.Words, _ => RichUnderline.Single } }; break;
            case "ulc": format = format with { UnderlineColor = Color(n) }; break;
            case "strike": if (n != 0 || format.Strike.ApplyValue() != RichStrike.Double) format = format with { Strike = n == 0 ? RichStrike.None : RichStrike.Single }; break;
            case "striked": if (n != 0) format = format with { Strike = RichStrike.Double }; else if (format.Strike.ApplyValue() == RichStrike.Double) format = format with { Strike = RichStrike.None }; break;
            case "cf": format = format with { Foreground = Color(n) }; break;
            case "highlight": case "chcbpat": format = format with { Background = Color(n) }; break;
            case "super": format = format with { Script = RichScript.Superscript }; break;
            case "sub": format = format with { Script = RichScript.Subscript }; break;
            case "nosupersub": format = format with { Script = RichScript.Normal }; break;
            case "expndtw": format = format with { CharacterSpacing = new RichLength(n) }; break;
            case "expnd": format = format with { CharacterSpacing = new RichLength(checked(n * 5)) }; break;
            case "scaps": format = format with { SmallCaps = n != 0 }; break;
            case "caps": format = format with { AllCaps = n != 0 }; break;
            case "outl": format = format with { Outline = n != 0 }; break;
            case "shad": format = format with { Shadow = n != 0 }; break;
            case "v": format = format with { Hidden = n != 0 }; break;
            case "lang":
                string? language = null;
                if (n is not (0 or 1024))
                    try { language = CultureInfo.GetCultureInfo(n).Name; }
                    catch (CultureNotFoundException) { Report("language", $"Language identifier {n} is unavailable."); }
                format = format with { Language = language }; break;
            case "rtlch": format = format with { Direction = RichDirection.RightToLeft }; break;
            case "ltrch": format = format with { Direction = RichDirection.LeftToRight }; break;
            default: return false;
        }
        if (_state.Destination == "fldrslt" && _state.Capture is not null && _state.Character != format)
            Report("field-result-format", "Formatting inside a field result was reduced to the field's outer character style.");
        _state.Character = format;
        return true;
    }

    private bool ParagraphControl(string word, int n)
    {
        var format = _state.Paragraph;
        switch (word)
        {
            case "ql": format = format with { Alignment = RichAlignment.Left }; break;
            case "qr": format = format with { Alignment = RichAlignment.Right }; break;
            case "qc": format = format with { Alignment = RichAlignment.Center }; break;
            case "qj": format = format with { Alignment = RichAlignment.Justify }; break;
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
            case "cfpat": case "cbpat": format = format with { Background = Color(n) }; break;
            default: return false;
        }
        if (_state.Destination == "fldrslt" && _state.Capture is not null && _state.Paragraph != format)
            Report("field-result-format", "Paragraph formatting inside a field result was omitted.");
        _state.Paragraph = format;
        return true;
    }

    private RichColor? Color(int index)
    {
        if (index == 0) return null;
        if (index < 0 || index >= _colors.Count) { Report("color-reference", $"Color {index} is missing; the default color was used."); return null; }
        return _colors[index];
    }
}

internal static class RtfStyleValueExtensions
{
    internal static T? ApplyValue<T>(this StyleValue<T> value) => value.Kind == StyleValueKind.Value ? value.Value : default;
}
