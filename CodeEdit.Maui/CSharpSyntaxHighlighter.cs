using System.Collections.Frozen;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A dependency-free lexical C# highlighter, including comments and verbatim/raw strings.</summary>
/// <remarks>
/// This is lexical coloring, not compiler classification. Contextual keywords are colored
/// wherever they occur. Interpolation expressions are not parsed or classified separately.
/// </remarks>
public sealed class CSharpSyntaxHighlighter : ICodeSyntaxHighlighter
{
    private static readonly FrozenSet<string> Keywords = (
        "abstract as base bool break byte case catch char checked class const continue decimal default delegate " +
        "do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int " +
        "interface internal is lock long namespace new null object operator out override params private protected " +
        "public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw " +
        "true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while " +
        "add alias and ascending async await by descending dynamic equals extension field file from get global group init into " +
        "join let managed nameof nint not notnull nuint on or orderby partial record remove required scoped select " +
        "set unmanaged value var when where with yield").Split(' ').ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<CodeToken> Highlight(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<CodeToken>();
        var position = 0;
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = position;
            var character = text[position];
            CodeTokenKind? kind = null;
            if (character == '/' && position + 1 < text.Length && text[position + 1] is '/' or '*')
            {
                var block = text[position + 1] == '*';
                position += 2;
                while (position < text.Length)
                {
                    Poll(position, cancellationToken);
                    if (block && text[position] == '*' && position + 1 < text.Length && text[position + 1] == '/')
                    {
                        position += 2;
                        break;
                    }
                    if (!block && IsNewLine(text[position])) break;
                    position++;
                }
                kind = CodeTokenKind.Comment;
            }
            else if (character == '#' && IsDirectiveStart(text, start))
            {
                while (position < text.Length && !IsNewLine(text[position]))
                {
                    Poll(position, cancellationToken);
                    position++;
                }
                kind = CodeTokenKind.Preprocessor;
            }
            else if (TryReadString(text, ref position, cancellationToken))
            {
                kind = CodeTokenKind.String;
            }
            else if (char.IsDigit(character))
            {
                var radixLiteral = character == '0' && position + 1 < text.Length && text[position + 1] is 'x' or 'X' or 'b' or 'B';
                position++;
                while (position < text.Length)
                {
                    Poll(position, cancellationToken);
                    var next = text[position];
                    if (char.IsLetterOrDigit(next) || next == '_' ||
                        next == '.' && position + 1 < text.Length && char.IsDigit(text[position + 1]) ||
                        !radixLiteral && next is '+' or '-' && text[position - 1] is 'e' or 'E') position++;
                    else break;
                }
                kind = CodeTokenKind.Number;
            }
            else if (char.IsLetter(character) || character is '_' or '@')
            {
                position++;
                while (position < text.Length && IsIdentifierPart(text[position]))
                {
                    Poll(position, cancellationToken);
                    position++;
                }
                if (Keywords.Contains(text[start..position])) kind = CodeTokenKind.Keyword;
            }
            else
            {
                position++;
                if (character == '$')
                    while (position < text.Length && text[position] == '$')
                    {
                        Poll(position, cancellationToken);
                        position++;
                    }
            }

            if (kind is { } tokenKind) tokens.Add(new CodeToken(new RichTextRange(start, position - start), tokenKind));
        }
        return tokens;
    }

    private static bool TryReadString(string text, ref int position, CancellationToken cancellationToken)
    {
        var cursor = position;
        var verbatim = false;
        if (text[cursor] == '@') { verbatim = true; cursor++; }
        while (cursor < text.Length && text[cursor] == '$')
        {
            Poll(cursor, cancellationToken);
            cursor++;
        }
        if (cursor < text.Length && text[cursor] == '@' && !verbatim) { verbatim = true; cursor++; }
        if (cursor >= text.Length || text[cursor] is not ('"' or '\'') ||
            text[cursor] == '\'' && cursor != position) return false;

        var quote = text[cursor++];
        var count = 1;
        if (quote == '"' && !verbatim)
        {
            while (cursor < text.Length && text[cursor] == '"')
            {
                Poll(cursor, cancellationToken);
                count++;
                cursor++;
            }
            if (count == 2) { position = cursor; return true; }
        }
        if (count >= 3)
        {
            var closing = 0;
            while (cursor < text.Length)
            {
                Poll(cursor, cancellationToken);
                closing = text[cursor++] == '"' ? closing + 1 : 0;
                if (closing == count) break;
            }
        }
        else
        {
            while (cursor < text.Length)
            {
                Poll(cursor, cancellationToken);
                if (!verbatim && IsNewLine(text[cursor])) break;
                var next = text[cursor++];
                if (next == quote)
                {
                    if (verbatim && cursor < text.Length && text[cursor] == quote) cursor++;
                    else break;
                }
                else if (!verbatim && next == '\\' && cursor < text.Length && !IsNewLine(text[cursor])) cursor++;
            }
        }
        position = cursor;
        return true;
    }

    private static bool IsDirectiveStart(string text, int position)
    {
        while (--position >= 0 && !IsNewLine(text[position]))
            if (!char.IsWhiteSpace(text[position])) return false;
        return true;
    }

    internal static bool IsNewLine(char character) => character is '\n' or '\r' or '\u2028';

    internal static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_' ||
        char.GetUnicodeCategory(character) is System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.ConnectorPunctuation;

    private static void Poll(int position, CancellationToken cancellationToken)
    {
        if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
    }
}
