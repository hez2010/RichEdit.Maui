namespace TextEdit.Core;

internal sealed record TextSelectionReplacement(IReadOnlyList<TextReplacement> Replacements, TextSelectionSet Selections, int Length)
{
    internal static TextSelectionReplacement Plan(TextSnapshot snapshot, TextSelectionSet selections, IReadOnlyList<string> text, int? caretOffset = null)
    {
        selections.Validate(snapshot);
        if (text.Count != selections.Count) throw new ArgumentException("Supply one text value per selection.", nameof(text));
        var changes = new List<TextReplacement>();
        var result = new List<TextSelection>();
        var shift = 0;
        for (var index = 0; index < selections.Count; index++)
        {
            var selection = selections[index];
            ArgumentNullException.ThrowIfNull(text[index]);
            var virtualOnly = text[index].Length == 0 && selection.Range.IsEmpty;
            var padding = text[index].Length == 0 ? 0 : selection.StartVirtualSpaces;
            var inserted = padding == 0 ? text[index] : new string(' ', padding) + text[index];
            var position = checked(selection.Range.Start + shift + padding + (caretOffset ?? text[index].Length));
            var spaces = virtualOnly ? selection.StartVirtualSpaces : 0;
            changes.Add(new(selection.Range, inserted));
            result.Add(selection with { Anchor = position, Active = position, AnchorVirtualSpaces = spaces, ActiveVirtualSpaces = spaces, PreferredVisualX = null });
            shift = checked(shift + inserted.Length - selection.Range.Length);
        }
        return new(changes, new(result, selections.PrimaryIndex), checked(snapshot.Length + shift));
    }
}
