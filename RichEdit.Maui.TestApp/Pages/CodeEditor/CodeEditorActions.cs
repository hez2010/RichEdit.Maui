using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

/// <summary>Options for ordinal, literal source searches.</summary>
/// <param name="MatchCase">Whether comparisons distinguish uppercase and lowercase.</param>
/// <param name="WholeWord">Whether matches must have identifier boundaries on both sides.</param>
internal readonly record struct CodeSearchOptions(bool MatchCase = false, bool WholeWord = false);

// Toolbar actions are application policy. Native shortcuts keep their internal implementation.
internal sealed class CodeEditorActions(CodeEditor editor)
{
    public void CollapseSelection()
    {
        if (GetCollapseRange() is not { } range)
            return;

        editor.Folding.Collapse(range);
        editor.SelectedRange = new(range.Start, 0);
    }

    public TextSpan? GetCollapseRange()
    {
        if (editor.SelectedRange.IsEmpty)
            return null;

        var (first, last) = GetSelectedLines();
        if (first == last)
            return null;
        // Keep the first line as the fold header and the final newline as its line break.
        var start = editor.GetLineRange(first).End;
        return new TextSpan(start, editor.GetLineRange(last).End - start);
    }

    public TextSpan? GetFoldAtCaret() => editor.Folding.CollapsedRanges
        .Where(range => editor.GetLineRange(editor.GetPosition(range.Start).Line).Start <= editor.SelectionState.Active && editor.SelectionState.Active <= range.End)
        .OrderByDescending(static range => range.Length)
        .Select(static range => (TextSpan?)range)
        .FirstOrDefault();

    public void ExpandAtCaret()
    {
        if (GetFoldAtCaret() is { } range)
            editor.Folding.Expand(range);
    }

    public void Indent() => editor.Indent();
    public void Outdent() => editor.Outdent();
    public void InsertNewLine() => editor.InsertNewLine();
    public void ToggleLineComment() => editor.ToggleLineComment();

    /// <summary>Moves the caret to a one-based logical line and UTF-16 column, clamped to the document.</summary>
    /// <param name="line">The requested line, starting at one.</param>
    /// <param name="column">The requested column, starting at one.</param>
    public void GoToLine(int line, int column = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        var range = editor.GetLineRange(Math.Min(line, editor.LineCount) - 1);
        editor.SelectedRange = new TextSpan(range.Start + Math.Min(column - 1, range.Length), 0);
        editor.ScrollIntoView(editor.SelectedRange);
    }

    /// <summary>Finds non-overlapping literal matches in the current source. Empty queries have no matches.</summary>
    /// <param name="query">The literal text to find.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <returns>Ordered UTF-16 source ranges.</returns>
    public IReadOnlyList<TextSpan> FindAll(string query, CodeSearchOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matches = new List<TextSpan>();
        if (query.Length == 0)
            return matches;

        var text = editor.Document.Text;
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var position = 0;
        while (position <= text.Length - query.Length)
        {
            position = text.IndexOf(query, position, comparison);
            if (position < 0)
                break;

            var end = position + query.Length;
            if (!options.WholeWord ||
                (position == 0 || !IsIdentifierPart(text[position - 1])) &&
                (end == text.Length || !IsIdentifierPart(text[end])))
                matches.Add(new TextSpan(position, query.Length));

            position = end;
        }

        return matches;
    }

    /// <summary>Selects the next literal match, optionally wrapping to the start of the source.</summary>
    /// <param name="query">The text to find.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <param name="wrap">Whether to continue from the beginning.</param>
    /// <returns>The selected range, or null when no match is found.</returns>
    public TextSpan? FindNext(string query, CodeSearchOptions options = default, bool wrap = true)
    {
        var matches = FindAll(query, options);
        foreach (var match in matches)
            if (match.Start >= editor.SelectedRange.End)
                return SelectMatch(match);

        return wrap && matches.Count > 0 ? SelectMatch(matches[0]) : null;
    }

    /// <summary>Selects the preceding literal match, optionally wrapping to the end of the source.</summary>
    /// <param name="query">The text to find.</param>
    /// <param name="options">Case and identifier-boundary options.</param>
    /// <param name="wrap">Whether to continue from the end.</param>
    /// <returns>The selected range, or null when no match is found.</returns>
    public TextSpan? FindPrevious(string query, CodeSearchOptions options = default, bool wrap = true)
    {
        var matches = FindAll(query, options);
        for (var i = matches.Count - 1; i >= 0; i--)
            if (matches[i].End <= editor.SelectedRange.Start)
                return SelectMatch(matches[i]);

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
        return ApplyEdits(matches.Select(range => new CodeTextEdit(range, replacement)).ToList(), "Replace all") ? matches.Count : 0;
    }

    private TextSpan SelectMatch(TextSpan range)
    {
        editor.SelectedRange = range;
        editor.ScrollIntoView(editor.SelectedRange);
        return range;
    }

    private (int First, int Last) GetSelectedLines()
    {
        var selection = editor.SelectedRange;
        var first = editor.GetPosition(selection.Start).Line;
        var last = editor.GetPosition(selection.End).Line;
        if (!selection.IsEmpty && last > first && selection.End == editor.GetLineRange(last).Start)
            last--;

        return (first, last);
    }

    private bool ApplyEdits(IReadOnlyList<CodeTextEdit> edits, string description)
    {
        if (editor.IsReadOnly || edits.Count == 0)
            return false;

        var resultingLength = (long)editor.Document.Length + edits.Sum(static edit => (long)edit.Text.Length - edit.Range.Length);
        if (resultingLength > int.MaxValue || editor.MaxLength >= 0 && resultingLength > editor.MaxLength && resultingLength > editor.Document.Length)
            return false;

        editor.Document.Edit(edit =>
        {
            for (var i = edits.Count - 1; i >= 0; i--)
            {
                var replacement = edits[i];
                edit.ReplaceText(replacement.Range, replacement.Text);
            }
        }, new TextEditOptions(Description: description));
        return true;
    }

    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_' ||
        char.GetUnicodeCategory(character) is System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.ConnectorPunctuation;

    private readonly record struct CodeTextEdit(TextSpan Range, string Text);
}
