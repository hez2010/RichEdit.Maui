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

    internal static string RemapBookmarkReferences(string instruction, IReadOnlyDictionary<string, string> names)
    {
        var tokens = Tokens(instruction).ToArray();
        if (tokens.Length < 2) return instruction;
        if (tokens[0].Value.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase))
        {
            if (TryHyperlink(instruction, out var link) && link!.Target.StartsWith('#') && names.TryGetValue(link.Target[1..], out var renamed))
                return HyperlinkInstruction(link with { Target = "#" + renamed });
            return instruction;
        }
        if (!(tokens[0].Value.Equals("REF", StringComparison.OrdinalIgnoreCase) || tokens[0].Value.Equals("PAGEREF", StringComparison.OrdinalIgnoreCase) ||
            tokens[0].Value.Equals("NOTEREF", StringComparison.OrdinalIgnoreCase)) || !names.TryGetValue(tokens[1].Value, out var name)) return instruction;
        var token = tokens[1];
        return instruction[..token.Start] + Quote(name) + instruction[(token.Start + token.Length)..];
    }

    private static IEnumerable<(string Value, bool Switch, int Start, int Length)> Tokens(string text)
    {
        for (var position = 0; position < text.Length;)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) yield break;
            var start = position;
            var quoted = text[position] == '"';
            if (quoted) position++;
            var value = new StringBuilder();
            while (position < text.Length)
            {
                var character = text[position++];
                if (quoted && character == '"') break;
                if (!quoted && char.IsWhiteSpace(character)) { position--; break; }
                if (character == '\\' && position < text.Length && text[position] is '\\' or '"') character = text[position++];
                value.Append(character);
            }
            yield return (value.ToString(), !quoted && value.Length > 0 && value[0] == '\\', start, position - start);
        }
    }
}
