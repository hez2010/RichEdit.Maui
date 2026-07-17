using System.Globalization;
using System.Text;

namespace RichEdit.Maui;

internal static class RichTextListFormatter
{
    public static string FormatMarker(RichTextListFormat list, int number) =>
        list.Kind == RichListKind.Bulleted
            ? list.BulletText
            : string.Concat(list.Prefix, FormatNumber(number, list.NumberStyle), list.Suffix);

    public static string FormatNumber(int number, RichListNumberStyle style) => style switch
    {
        RichListNumberStyle.UpperRoman => FormatRoman(number, uppercase: true),
        RichListNumberStyle.LowerRoman => FormatRoman(number, uppercase: false),
        RichListNumberStyle.UpperLetter => FormatLetters(number, uppercase: true),
        RichListNumberStyle.LowerLetter => FormatLetters(number, uppercase: false),
        _ => number.ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatRoman(int number, bool uppercase)
    {
        if (number is <= 0 or > 3999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        ReadOnlySpan<(int Value, string Text)> numerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];
        var output = new StringBuilder();
        foreach (var numeral in numerals)
        {
            while (number >= numeral.Value)
            {
                output.Append(numeral.Text);
                number -= numeral.Value;
            }
        }

        var result = output.ToString();
        return uppercase ? result : result.ToLowerInvariant();
    }

    private static string FormatLetters(int number, bool uppercase)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        Span<char> buffer = stackalloc char[16];
        var position = buffer.Length;
        while (number > 0 && position > 0)
        {
            number--;
            buffer[--position] = (char)((uppercase ? 'A' : 'a') + number % 26);
            number /= 26;
        }

        return number == 0
            ? new string(buffer[position..])
            : string.Empty;
    }
}
