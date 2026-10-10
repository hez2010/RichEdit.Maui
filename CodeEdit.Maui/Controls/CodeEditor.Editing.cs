using RichEdit.Maui;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    private void InitializeKeyBindings()
    {
        var primary = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? EditorKeyModifiers.Meta : EditorKeyModifiers.Control;
        KeyBindings.Add(new(EditorKey.Tab, EditorKeyModifiers.None, new Command(Indent, () => !IsReadOnly)));
        KeyBindings.Add(new(EditorKey.Tab, EditorKeyModifiers.Shift, new Command(Outdent, () => !IsReadOnly)));
        KeyBindings.Add(new(EditorKey.Enter, EditorKeyModifiers.None, new Command(InsertNewLine, () => !IsReadOnly)));
        var comment = new Command(ToggleLineComment, () => !IsReadOnly);
        KeyBindings.Add(new(EditorKey.Slash, primary, comment));
        KeyBindings.Add(new(EditorKey.Divide, primary, comment));
        var suppressRichFormatting = new Command(static () => { });
        foreach (var key in new[] { EditorKey.B, EditorKey.I, EditorKey.U })
            KeyBindings.Add(new(key, primary, suppressRichFormatting));
    }

    /// <summary>Inserts indentation at the caret, or indents every selected logical line in one undo unit.</summary>
    internal void Indent()
    {
        if (IsReadOnly)
            return;
        var edits = new List<TextReplacement>();
        foreach (var selection in Selections)
        {
            var range = new TextSpan(selection.Range.Start, selection.Range.Length);
            if (range.IsEmpty)
            {
                var line = Lines.GetRange(Lines.GetLineIndex(range.Start));
                var column = 0;
                foreach (var chunk in Document.CurrentSnapshot.TextBuffer.GetChunks(new(line.Start, range.Start - line.Start)))
                    foreach (var character in chunk.Span)
                        column += character == '\t' ? TabSize - column % TabSize : 1;

                edits.Add(new(range, UseTabs ? "\t" : new string(' ', IndentSize - column % IndentSize)));
            }
            else
            {
                var (first, last) = Lines.GetSelectedLines(range);
                for (var line = first; line <= last; line++)
                    edits.Add(new(new TextSpan(Lines.GetRange(line).Start, 0), Indentation));
            }
        }
        ApplyEdits(edits.DistinctBy(static edit => edit.Range).ToArray(), "Indent");
    }

    /// <summary>Removes one indentation level from each selected line in one undo unit.</summary>
    internal void Outdent()
    {
        if (IsReadOnly)
            return;

        var edits = new List<TextReplacement>();
        foreach (var line in SelectedSourceLines())
        {
            var range = Lines.GetRange(line);
            var prefix = Document.CurrentSnapshot.GetText(new(range.Start, Math.Min(IndentSize, range.Length)));
            var length = 0;
            if (prefix.Length != 0 && prefix[0] == '\t')
                length = 1;
            else
                while (length < prefix.Length && prefix[length] == ' ')
                    length++;
            if (length > 0)
                edits.Add(new(new TextSpan(range.Start, length), string.Empty));
        }

        ApplyEdits(edits, "Outdent lines");
    }

    /// <summary>Replaces the selection with a newline and, when enabled, the current line's leading whitespace.</summary>
    internal void InsertNewLine()
    {
        if (IsReadOnly)
            return;

        ApplyEdits(Selections.Select(selection => new TextReplacement(
            new(selection.Range.Start, selection.Range.Length), NewLine + (AutoIndent ? GetIndentation(selection.Range.Start) : string.Empty))).ToArray(), "New line");
    }

    /// <summary>Adds or removes the line-comment prefix after leading whitespace on selected nonempty lines.</summary>
    internal void ToggleLineComment()
    {
        if (IsReadOnly)
            return;

        var selectedLines = SelectedSourceLines();
        var targets = new List<TextSpan>();
        foreach (var line in selectedLines)
        {
            var range = Lines.GetRange(line);
            var start = range.Start + LeadingWhitespace(Document.CurrentSnapshot.TextBuffer, range);
            if (start != range.End || selectedLines.Count == 1 || Selections.Any(selection =>
                selection.Range.IsEmpty && Lines.GetLineIndex(selection.Active) == line))
                targets.Add(new TextSpan(start, range.End - start));
        }

        var uncomment = targets.Count > 0 && targets.All(range =>
            Document.CurrentSnapshot.GetText(new(range.Start, Math.Min(range.Length, LineCommentPrefix.Length))) == LineCommentPrefix);
        var edits = new List<TextReplacement>();
        foreach (var range in targets)
        {
            if (uncomment)
            {
                var length = LineCommentPrefix.Length;
                if (length < range.Length && Document.CurrentSnapshot.TextBuffer[range.Start + length] == ' ')
                    length++;

                edits.Add(new(new TextSpan(range.Start, length), string.Empty));
            }
            else
                edits.Add(new(new TextSpan(range.Start, 0), LineCommentPrefix + " "));
        }

        ApplyEdits(edits, uncomment ? "Uncomment lines" : "Comment lines");
    }

    /// <summary>Gets the source range for a zero-based logical line, excluding its line terminator.</summary>
    /// <param name="line">The zero-based line number.</param>
    /// <returns>The line's UTF-16 source range.</returns>
    public TextSpan GetLineRange(int line) => Lines.GetRange(line);

    /// <summary>Converts a bounded UTF-16 offset to a zero-based line and column.</summary>
    /// <param name="offset">The source offset.</param>
    /// <returns>The logical source position.</returns>
    public TextPosition GetPosition(int offset) => Lines.GetPosition(offset);

    /// <summary>Converts a zero-based line and UTF-16 column to a bounded source offset.</summary>
    /// <param name="position">The logical source position.</param>
    /// <returns>The UTF-16 source offset.</returns>
    public int GetOffset(TextPosition position) => Lines.Buffer.GetOffset(position);

    private string Indentation => UseTabs ? "\t" : new string(' ', IndentSize);

    private SortedSet<int> SelectedSourceLines()
    {
        var lines = new SortedSet<int>();
        foreach (var selection in Selections)
        {
            var (first, last) = Lines.GetSelectedLines(new(selection.Range.Start, selection.Range.Length));
            for (var line = first; line <= last; line++)
                lines.Add(line);
        }
        return lines;
    }

    private string GetIndentation(int position)
    {
        return GetIndentation(Document.CurrentSnapshot.TextBuffer, position);
    }

    private static string GetIndentation(TextSnapshot snapshot, int position)
    {
        var line = snapshot.GetLine(snapshot.GetPosition(position).Line).Span;
        var prefix = new TextSpan(line.Start, Math.Min(position, line.End) - line.Start);
        return snapshot.GetText(new(prefix.Start, LeadingWhitespace(snapshot, prefix)));
    }

    private static int LeadingWhitespace(TextSnapshot snapshot, TextSpan range)
    {
        var length = 0;
        foreach (var chunk in snapshot.GetChunks(range))
            foreach (var character in chunk.Span)
            {
                if (character is not (' ' or '\t'))
                    return length;
                length++;
            }
        return length;
    }

    private void PlanNativeAutoIndent(TextInputContext input)
    {
        if (!AutoIndent || IsReadOnly || input.InputChanges.Length != input.BeforeSelections.Count ||
            input.InputChanges.Any(change => change.Text is not ("\n" or "\r\n" or "\r")))
            return;
        var ranges = input.InputChanges.Select(change => change.Range).ToHashSet();
        if (input.BeforeSelections.Any(selection => !ranges.Contains(selection.Range)))
            return;
        var map = new PositionMap(input.Before.Length, input.InputChanges.Select(change => new PositionChange(change.Range, change.Text.Length)));
        foreach (var selection in input.BeforeSelections)
        {
            var indentation = GetIndentation(input.Before, selection.Range.Start);
            if (indentation.Length != 0)
                input.Replace(new(map.Map(selection.Range.Start)!.Value, 0), indentation);
        }
    }

    private bool ApplyEdits(IReadOnlyList<TextReplacement> edits, string description) =>
        TryApplyEdits(Document.Revision, edits, options: new TextEditOptions(description));

    /// <summary>Applies a checked source edit through its source session.</summary>
    /// <param name="revision">The captured source revision.</param>
    /// <param name="edits">Original-coordinate source replacements.</param>
    /// <param name="selectionAfter">The resulting directional selection, or null to map the current selection.</param>
    /// <param name="options">Undo and application metadata.</param>
    /// <param name="expectedSelectionGeneration">An optional captured selection generation, to reject results after the user moves a caret.</param>
    /// <returns>Whether the current view accepted the revision and edits.</returns>
    public bool TryApplyEdits(DocumentRevision revision, IReadOnlyList<TextReplacement> edits,
        TextSelectionState? selectionAfter = null, TextEditOptions options = default, long? expectedSelectionGeneration = null) =>
            TextView.TryApplySourceEdits(revision, edits, selectionAfter, options, expectedSelectionGeneration);

    /// <summary>Gets a generation that changes when this view's selection changes or maps through a source edit.</summary>
    public long SelectionGeneration => TextView.SourceSession!.SelectionGeneration;
}
