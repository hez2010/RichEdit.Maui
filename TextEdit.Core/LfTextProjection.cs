using System.Collections.Immutable;

namespace TextEdit.Core;

// A native LF buffer and a logarithmic map into exact source line endings. This is
// derived view state: the canonical document keeps its original CRLF, LF, and CR.
internal sealed class LfTextProjection
{
    internal LfTextProjection(TextSnapshot source) : this(source, source.NormalizeToLf()) { }
    private LfTextProjection(TextSnapshot source, TextSnapshot display) { Source = source; Display = display; }
    internal TextSnapshot Source { get; }
    internal TextSnapshot Display { get; }
    internal int ToDisplay(int source, bool afterCrLf = true) => Source.ToLfOffset(source, afterCrLf);
    internal int ToSource(int display) => Source.FromLfOffset(display);
    internal TextSpan ToDisplay(TextSpan range)
    {
        range.Validate(Source.Length);
        if (range.IsEmpty) return new(ToDisplay(range.Start), 0);
        var start = ToDisplay(range.Start, afterCrLf: false);
        return new(start, ToDisplay(range.End) - start);
    }
    internal TextSpan ToSource(TextSpan range)
    {
        range.Validate(Display.Length);
        var start = ToSource(range.Start);
        return new(start, ToSource(range.End) - start);
    }

    internal LfTextProjection Apply(TextDocumentChangedEventArgs change, out ImmutableArray<TextReplacement> displayEdits)
    {
        if (!ReferenceEquals(Source, change.Before))
            throw new ArgumentException("The projection does not describe the change's input snapshot.", nameof(change));
        var source = Source;
        var display = Display;
        var edits = ImmutableArray.CreateBuilder<TextReplacement>();
        foreach (var edit in change.Changes)
        {
            // Include neighboring code units: inserting or deleting between CR and
            // LF can create or split a pair beyond the literal replacement range.
            var start = Math.Max(0, edit.OldSpan.Start - 1);
            var end = edit.OldSpan.End < source.Length ? edit.OldSpan.End + 1 : source.Length;
            if (start > 0 && start < source.Length && source[start - 1] == '\r' && source[start] == '\n') start--;
            if (end > 0 && end < source.Length && source[end - 1] == '\r' && source[end] == '\n') end++;
            var displayStart = source.ToLfOffset(start);
            var displayEnd = source.ToLfOffset(end);
            source = source.Replace(edit.OldSpan, edit.Inserted.ToString());
            var newEnd = checked((int)((long)end + edit.Inserted.Length - edit.OldSpan.Length));
            var after = source.GetText(new(start, newEnd - start)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var before = display.GetText(new(displayStart, displayEnd - displayStart));
            var prefix = before.AsSpan().CommonPrefixLength(after);
            var suffix = 0;
            var maximum = Math.Min(before.Length, after.Length) - prefix;
            while (suffix < maximum && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
            var removed = before.Length - prefix - suffix;
            var inserted = after.Substring(prefix, after.Length - prefix - suffix);
            if (removed == 0 && inserted.Length == 0) continue;
            var replacement = new TextReplacement(new(displayStart + prefix, removed), inserted);
            display = display.Replace(replacement.Range, replacement.Text);
            edits.Add(replacement);
        }
        displayEdits = edits.ToImmutable();
        return new(change.After, display);
    }
}
