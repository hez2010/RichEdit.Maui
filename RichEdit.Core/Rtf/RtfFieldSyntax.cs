using System.Text;

namespace RichEdit.Core.Rtf;

internal static class RtfFieldSyntax
{
    internal static string HyperlinkInstruction(RichHyperlink link) => "HYPERLINK " + Quote(link.Target) +
        (link.ToolTip is { } tip ? " \\o " + Quote(tip) : "");

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    internal static bool TryHyperlink(string instruction, out RichHyperlink? link)
    {
        link = null;
        var tokens = Tokens(instruction).ToArray();
        if (tokens.Length < 2 || !tokens[0].Value.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase)) return false;
        string? target = null, tip = null, fragment = null;
        for (var index = 1; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (!token.Switch) { target ??= token.Value; continue; }
            if (token.Value.Equals("\\l", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length) fragment = tokens[++index].Value;
            else if (token.Value.Equals("\\o", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length) tip = tokens[++index].Value;
            else if (token.Value.Equals("\\t", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length) index++;
            else if (!token.Value.Equals("\\m", StringComparison.OrdinalIgnoreCase) && !token.Value.Equals("\\n", StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (fragment is not null) target = (target ?? "") + "#" + fragment;
        if (string.IsNullOrWhiteSpace(target)) return false;
        link = new(target, tip);
        return true;
    }

    private static IEnumerable<(string Value, bool Switch)> Tokens(string text)
    {
        for (var position = 0; position < text.Length;)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) yield break;
            var quoted = text[position] == '"';
            if (quoted) position++;
            var value = new StringBuilder();
            while (position < text.Length)
            {
                var character = text[position++];
                if (quoted && character == '"' || !quoted && char.IsWhiteSpace(character)) break;
                if (character == '\\' && position < text.Length && text[position] is '\\' or '"') character = text[position++];
                value.Append(character);
            }
            yield return (value.ToString(), !quoted && value.Length > 0 && value[0] == '\\');
        }
    }
}
