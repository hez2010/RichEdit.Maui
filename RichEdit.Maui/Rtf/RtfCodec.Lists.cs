using System.Globalization;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private static bool IsBulletMarker(char character) =>
        character is '•' or '·' or '◦' or '▪' or '‣' or '⁃' or '-' or '*';

    private static bool TryParseNumberMarker(
        ReadOnlySpan<char> content,
        out NumberMarker marker,
        out int markerLength)
    {
        marker = default;
        markerLength = 0;

        var delimiterIndex = 0;
        while (delimiterIndex < content.Length && content[delimiterIndex] is not ('.' or ')'))
        {
            delimiterIndex++;
        }

        if (delimiterIndex == 0 || delimiterIndex + 1 >= content.Length ||
            !char.IsWhiteSpace(content[delimiterIndex + 1]))
        {
            return false;
        }

        var token = content[..delimiterIndex];
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var arabic) && arabic > 0)
        {
            marker = new NumberMarker(arabic, content[delimiterIndex], ListNumberFormat.Arabic);
            markerLength = delimiterIndex + 2;
            return true;
        }

        if (TryParseRoman(token, out var roman, out var romanFormat))
        {
            marker = new NumberMarker(roman, content[delimiterIndex], romanFormat);
            markerLength = delimiterIndex + 2;
            return true;
        }

        if (TryParseLetters(token, out var letters, out var letterFormat))
        {
            marker = new NumberMarker(letters, content[delimiterIndex], letterFormat);
            markerLength = delimiterIndex + 2;
            return true;
        }

        return false;
    }

    private static bool TryParseRoman(
        ReadOnlySpan<char> token,
        out int value,
        out ListNumberFormat format)
    {
        value = 0;
        format = ListNumberFormat.Arabic;
        if (token.IsEmpty)
        {
            return false;
        }

        var uppercase = true;
        var lowercase = true;
        foreach (var character in token)
        {
            uppercase &= character is 'I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M';
            lowercase &= character is 'i' or 'v' or 'x' or 'l' or 'c' or 'd' or 'm';
        }

        if (!uppercase && !lowercase)
        {
            return false;
        }

        var total = 0;
        var prior = 0;
        for (var index = token.Length - 1; index >= 0; index--)
        {
            var current = char.ToUpperInvariant(token[index]) switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => 0,
            };
            total += current < prior ? -current : current;
            prior = Math.Max(prior, current);
        }

        if (total is <= 0 or > 3999 ||
            !token.Equals(
                RichTextListFormatter.FormatNumber(
                    total,
                    uppercase
                        ? RichListNumberStyle.UpperRoman
                        : RichListNumberStyle.LowerRoman).AsSpan(),
                StringComparison.Ordinal))
        {
            return false;
        }

        value = total;
        format = uppercase ? ListNumberFormat.UpperRoman : ListNumberFormat.LowerRoman;
        return true;
    }

    private static bool TryParseLetters(
        ReadOnlySpan<char> token,
        out int value,
        out ListNumberFormat format)
    {
        value = 0;
        format = ListNumberFormat.Arabic;
        if (token.IsEmpty)
        {
            return false;
        }

        var uppercase = true;
        var lowercase = true;
        long total = 0;
        foreach (var character in token)
        {
            uppercase &= character is >= 'A' and <= 'Z';
            lowercase &= character is >= 'a' and <= 'z';
            if (!uppercase && !lowercase)
            {
                return false;
            }

            var letter = char.ToUpperInvariant(character) - 'A' + 1;
            total = total * 26 + letter;
            if (total > int.MaxValue)
            {
                return false;
            }
        }

        value = (int)total;
        format = uppercase ? ListNumberFormat.UpperLetter : ListNumberFormat.LowerLetter;
        return true;
    }

    private static string FormatListNumber(int number, ListNumberFormat format) =>
        RichTextListFormatter.FormatNumber(number, format switch
        {
            ListNumberFormat.UpperRoman => RichListNumberStyle.UpperRoman,
            ListNumberFormat.LowerRoman => RichListNumberStyle.LowerRoman,
            ListNumberFormat.UpperLetter => RichListNumberStyle.UpperLetter,
            ListNumberFormat.LowerLetter => RichListNumberStyle.LowerLetter,
            _ => RichListNumberStyle.Arabic,
        });

    private readonly record struct NumberMarker(
        int Number,
        char Delimiter,
        ListNumberFormat Format);
}
