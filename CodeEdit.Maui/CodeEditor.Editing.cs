using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>Options for ordinal, literal source searches.</summary>
/// <param name="MatchCase">Whether comparisons distinguish uppercase and lowercase.</param>
/// <param name="WholeWord">Whether matches must have identifier boundaries on both sides.</param>
public readonly record struct CodeSearchOptions(bool MatchCase = false, bool WholeWord = false);

public sealed partial class CodeEditor
{
    /// <summary>Inserts indentation at the caret, or indents every selected logical line in one undo unit.</summary>
    public void Indent()
    {
        if (IsReadOnly) return;
        if (SelectedRange.IsEmpty)
        {
            var line = Lines.GetRange(Lines.GetLineIndex(SelectedRange.Start));
            var column = 0;
            foreach (var character in Document.Text.AsSpan(line.Start, SelectedRange.Start - line.Start))
                column += character == '\t' ? IndentSize - column % IndentSize : 1;
            var indentation = UseTabs ? "\t" : new string(' ', IndentSize - column % IndentSize);
            ApplyEdits([new(SelectedRange, indentation)], "Indent");
            return;
        }
        var (first, last) = Lines.GetSelectedLines(SelectedRange);
        var edits = new List<CodeTextEdit>();
        for (var line = first; line <= last; line++)
            edits.Add(new(new RichTextRange(Lines.GetRange(line).Start, 0), Indentation));
        ApplyEdits(edits, "Indent lines");
    }

    /// <summary>Removes one indentation level from each selected line in one undo unit.</summary>
    public void Outdent()
    {
        if (IsReadOnly) return;
        var (first, last) = Lines.GetSelectedLines(SelectedRange);
        var edits = new List<CodeTextEdit>();
        for (var line = first; line <= last; line++)
        {
            var range = Lines.GetRange(line);
            var length = 0;
            if (!range.IsEmpty && Document.Text[range.Start] == '\t') length = 1;
            else
                while (length < Math.Min(IndentSize, range.Length) && Document.Text[range.Start + length] == ' ') length++;
            if (length > 0) edits.Add(new(new RichTextRange(range.Start, length), string.Empty));
        }
        ApplyEdits(edits, "Outdent lines");
    }

    /// <summary>Replaces the selection with a newline and, when enabled, the current line's leading whitespace.</summary>
    public void InsertNewLine()
    {
        if (IsReadOnly) return;
        ApplyEdits([new(SelectedRange, "\n" + (AutoIndent ? GetIndentation(SelectedRange.Start) : string.Empty))], "New line");
    }

    /// <summary>Adds or removes the line-comment prefix after leading whitespace on selected nonempty lines.</summary>
    public void ToggleLineComment()
    {
        if (IsReadOnly) return;
        var (first, last) = Lines.GetSelectedLines(SelectedRange);
        var targets = new List<RichTextRange>();
        for (var line = first; line <= last; line++)
        {
            var range = Lines.GetRange(line);
            var start = range.Start;
            while (start < range.End && Document.Text[start] is ' ' or '\t') start++;
            if (start != range.End || first == last) targets.Add(new RichTextRange(start, range.End - start));
        }
        var uncomment = targets.Count > 0 && targets.All(range =>
            Document.Text.AsSpan(range.Start, range.Length).StartsWith(LineCommentPrefix, StringComparison.Ordinal));
        var edits = new List<CodeTextEdit>();
        foreach (var range in targets)
        {
            if (uncomment)
            {
                var length = LineCommentPrefix.Length;
                if (length < range.Length && Document.Text[range.Start + length] == ' ') length++;
                edits.Add(new(new RichTextRange(range.Start, length), string.Empty));
            }
            else edits.Add(new(new RichTextRange(range.Start, 0), LineCommentPrefix + " "));
        }
        ApplyEdits(edits, uncomment ? "Uncomment lines" : "Comment lines");
    }

    /// <summary>Gets the source range for a one-based logical line, excluding its line terminator.</summary>
    /// <param name="line">The one-based line number.</param>
    /// <returns>The line's UTF-16 source range.</returns>
    public RichTextRange GetLineRange(int line) => Lines.GetRange(line - 1);

    /// <summary>Moves the caret to a one-based logical line and UTF-16 column, clamped to the document.</summary>
    /// <param name="line">The requested line, starting at one.</param>
    /// <param name="column">The requested column, starting at one.</param>
    public void GoToLine(int line, int column = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        var range = Lines.GetRange(Math.Min(line, LineCount) - 1);
        SelectedRange = new RichTextRange(range.Start + Math.Min(column - 1, range.Length), 0);
        NativeAdapter?.ScrollToSelection();
    }

    /// <summary>Finds non-overlapping literal matches in the current source. Empty queries have no matches.</summary>
    /// <param name="query">The literal text to find.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <returns>Ordered UTF-16 source ranges.</returns>
    public IReadOnlyList<RichTextRange> FindAll(string query, CodeSearchOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matches = new List<RichTextRange>();
        if (query.Length == 0) return matches;
        var text = Document.Text;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var position = 0;
        while (position <= text.Length - query.Length)
        {
            position = text.IndexOf(query, position, comparison);
            if (position < 0) break;
            var end = position + query.Length;
            if (!options.WholeWord ||
                (position == 0 || !CSharpSyntaxHighlighter.IsIdentifierPart(text[position - 1])) &&
                (end == text.Length || !CSharpSyntaxHighlighter.IsIdentifierPart(text[end])))
                matches.Add(new RichTextRange(position, query.Length));
            position = end;
        }
        return matches;
    }

    /// <summary>Selects the next literal match, optionally wrapping to the start of the source.</summary>
    /// <param name="query">The text to find.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <param name="wrap">Whether to continue from the beginning.</param>
    /// <returns>The selected range, or null when no match is found.</returns>
    public RichTextRange? FindNext(string query, CodeSearchOptions options = default, bool wrap = true)
    {
        var matches = FindAll(query, options);
        foreach (var match in matches)
            if (match.Start >= SelectedRange.End) return SelectMatch(match);
        return wrap && matches.Count > 0 ? SelectMatch(matches[0]) : null;
    }

    /// <summary>Selects the preceding literal match, optionally wrapping to the end of the source.</summary>
    /// <param name="query">The text to find.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <param name="wrap">Whether to continue from the end.</param>
    /// <returns>The selected range, or null when no match is found.</returns>
    public RichTextRange? FindPrevious(string query, CodeSearchOptions options = default, bool wrap = true)
    {
        var matches = FindAll(query, options);
        for (var i = matches.Count - 1; i >= 0; i--)
            if (matches[i].End <= SelectedRange.Start) return SelectMatch(matches[i]);
        return wrap && matches.Count > 0 ? SelectMatch(matches[^1]) : null;
    }

    /// <summary>Replaces every literal match in one undo unit, respecting read-only state and the length limit.</summary>
    /// <param name="query">The text to find.</param>
    /// <param name="replacement">The replacement source text.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <returns>The number of matches replaced, or zero when the operation cannot be applied.</returns>
    public int ReplaceAll(string query, string replacement, CodeSearchOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var matches = FindAll(query, options);
        var normalized = replacement.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return ApplyEdits(matches.Select(range => new CodeTextEdit(range, normalized)).ToList(), "Replace all") ? matches.Count : 0;
    }

    private RichTextRange SelectMatch(RichTextRange range)
    {
        SelectedRange = range;
        NativeAdapter?.ScrollToSelection();
        return range;
    }

    private string Indentation => UseTabs ? "\t" : new string(' ', IndentSize);

    private string GetIndentation(int position)
    {
        var range = Lines.GetRange(Lines.GetLineIndex(position));
        var end = range.Start;
        while (end < Math.Min(position, range.End) && Document.Text[end] is ' ' or '\t') end++;
        return Document.Text[range.Start..end];
    }

    private void QueueNativeAutoIndent(RichTextChangeSet changes)
    {
        if (!AutoIndent || IsReadOnly || NativeAdapter is not { } handler || handler.IsComposing) return;
        var replacements = changes.Changes.OfType<RichTextTextChange>().ToArray();
        if (replacements is not [{ InsertedText: "\n" } change]) return;
        var indentation = GetIndentation(change.NewRange.Start);
        if (indentation.Length == 0) return;
        var document = Document;
        var version = document.Version;
        var caret = new RichTextRange(change.NewRange.End, 0);
        handler.Post(() =>
        {
            if (ReferenceEquals(NativeAdapter, handler) && !handler.IsComposing && AutoIndent && !IsReadOnly && CanUndo &&
                ReferenceEquals(Document, document) && document.Version == version && SelectedRange == caret)
                ApplyEdits([new(caret, indentation)], "New line", RichTextUndoBehavior.MergeWithPrevious);
        });
    }

    private bool ApplyEdits(IReadOnlyList<CodeTextEdit> edits, string description,
        RichTextUndoBehavior undoBehavior = RichTextUndoBehavior.CreateUnit)
    {
        if (IsReadOnly || edits.Count == 0) return false;
        var resultingLength = (long)Document.Length + edits.Sum(static edit => (long)edit.Text.Length - edit.Range.Length);
        if (resultingLength > int.MaxValue || MaxLength >= 0 && resultingLength > MaxLength && resultingLength > Document.Length) return false;
        var start = SelectedRange.Start;
        var end = SelectedRange.End;
        Document.Edit(edit =>
        {
            for (var i = edits.Count - 1; i >= 0; i--)
            {
                var replacement = edits[i];
                edit.ReplaceText(replacement.Range, replacement.Text);
                start = MapPosition(start, replacement);
                end = MapPosition(end, replacement);
            }
        }, new RichTextEditOptions(undoBehavior, description));
        SelectedRange = new RichTextRange(start, Math.Max(0, end - start));
        return true;
    }

    private static int MapPosition(int position, CodeTextEdit edit) => position < edit.Range.Start ? position :
        position >= edit.Range.End ? position + edit.Text.Length - edit.Range.Length : edit.Range.Start + edit.Text.Length;

    private readonly record struct CodeTextEdit(RichTextRange Range, string Text);
}
