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
        if (IsReadOnly) return;
        if (SelectedRange.IsEmpty)
        {
            var line = Lines.GetRange(Lines.GetLineIndex(SelectedRange.Start));
            var column = 0;
            foreach (var character in Document.Text.AsSpan(line.Start, SelectedRange.Start - line.Start))
                column += character == '\t' ? IndentSize - column % IndentSize : 1;
            var indentation = UseTabs ? "\t" : new string(' ', IndentSize - column % IndentSize);
            ApplyEdits((CodeTextEdit[])[new(SelectedRange, indentation)], "Indent");
            return;
        }
        var (first, last) = Lines.GetSelectedLines(SelectedRange);
        var edits = new List<CodeTextEdit>();
        for (var line = first; line <= last; line++)
            edits.Add(new(new RichTextRange(Lines.GetRange(line).Start, 0), Indentation));
        ApplyEdits(edits, "Indent lines");
    }

    /// <summary>Removes one indentation level from each selected line in one undo unit.</summary>
    internal void Outdent()
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
    internal void InsertNewLine()
    {
        if (IsReadOnly) return;
        ApplyEdits((CodeTextEdit[])[new(SelectedRange, "\n" + (AutoIndent ? GetIndentation(SelectedRange.Start) : string.Empty))], "New line");
    }

    /// <summary>Adds or removes the line-comment prefix after leading whitespace on selected nonempty lines.</summary>
    internal void ToggleLineComment()
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

    /// <summary>Converts a bounded UTF-16 offset to a one-based line and column.</summary>
    /// <param name="offset">The source offset.</param>
    /// <returns>The logical source position.</returns>
    public CodePosition GetPosition(int offset) => Lines.GetPosition(offset);

    /// <summary>Converts a one-based line and UTF-16 column to a bounded source offset.</summary>
    /// <param name="position">The logical source position.</param>
    /// <returns>The UTF-16 source offset.</returns>
    public int GetOffset(CodePosition position)
    {
        var range = GetLineRange(position.Line);
        ArgumentOutOfRangeException.ThrowIfLessThan(position.Column, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Column - 1, range.Length);
        return range.Start + position.Column - 1;
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
                ApplyEdits((CodeTextEdit[])[new(caret, indentation)], "New line", RichTextUndoBehavior.MergeWithPrevious);
        });
    }

    private bool ApplyEdits(IReadOnlyList<CodeTextEdit> edits, string description,
        RichTextUndoBehavior undoBehavior = RichTextUndoBehavior.CreateUnit)
    {
        if (IsReadOnly || edits.Count == 0) return false;
        var resultingLength = (long)Document.Length + edits.Sum(static edit => (long)edit.Text.Length - edit.Range.Length);
        if (resultingLength > int.MaxValue || MaxLength >= 0 && resultingLength > MaxLength && resultingLength > Document.Length) return false;
        var anchor = SelectionState.Anchor;
        var active = SelectionState.Active;
        for (var i = edits.Count - 1; i >= 0; i--)
        {
            anchor = MapPosition(anchor, edits[i]);
            active = MapPosition(active, edits[i]);
        }
        TextView.Selection.Edit(edit =>
        {
            for (var i = edits.Count - 1; i >= 0; i--)
            {
                var replacement = edits[i];
                edit.ReplaceText(replacement.Range, replacement.Text);
            }
        }, new RichTextSelectionState(anchor, active), new RichTextEditOptions(undoBehavior, description));
        return true;
    }

    private static int MapPosition(int position, CodeTextEdit edit) => position < edit.Range.Start ? position :
        position >= edit.Range.End ? position + edit.Text.Length - edit.Range.Length : edit.Range.Start + edit.Text.Length;

    private readonly record struct CodeTextEdit(RichTextRange Range, string Text);
}
