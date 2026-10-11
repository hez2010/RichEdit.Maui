using System.Globalization;
using TextEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private sealed record NativeSelectionEdit(
        RichTextDocumentSnapshot Snapshot,
        IReadOnlyList<RichTextChange> Changes,
        RichTextDocumentSnapshot NativeBefore,
        IReadOnlyList<RichTextChange> NativeCorrections,
        TextSelectionSet Selections);

    private NativeSelectionEdit? ExpandNativeSelectionEdit(RichTextDocumentSnapshot native, TextSelectionState nativeSelection)
    {
        var hasVirtualSpace = SourceSession is not null && Selections.Any(item => item.AnchorVirtualSpaces != 0 || item.ActiveVirtualSpaces != 0);
        if (Selections.Count < 2 && !hasVirtualSpace || Composition.IsActive || (Handler as IRichEditorHandler)?.IsComposing == true)
            return null;

        var before = Document.CurrentSnapshot;
        var selections = Selections;
        var primary = selections.Primary;
        var primaryRange = new TextSpan(primary.Range.Start, primary.Range.Length);
        RichTextTextChange? primaryChange = null;
        if (TryGetNativeReplacement(before, native, primaryRange, out var inserted) &&
            (!before.TextBuffer.ContentEquals(native.TextBuffer) || !primaryRange.IsEmpty && nativeSelection.IsEmpty &&
                nativeSelection.Active == primaryRange.Start + inserted.Length))
            primaryChange = new(primaryRange, inserted);
        else if (primaryRange.IsEmpty && before.Length > native.Length)
        {
            var backward = new TextSpan(PreviousTextElement(before.TextBuffer, primary.Active),
                primary.Active - PreviousTextElement(before.TextBuffer, primary.Active));
            var forward = new TextSpan(primary.Active, primary.Active == before.Length ? 0 :
                NextTextElementLength(before.TextBuffer, primary.Active));
            if (TryGetNativeReplacement(before, native, backward, out var backwardText) && backwardText.Length == 0)
                primaryChange = new(backward, "");
            else if (TryGetNativeReplacement(before, native, forward, out var forwardText) && forwardText.Length == 0)
                primaryChange = new(forward, "");
        }
        if (primaryChange is null)
            return null;
        var caretOffset = primaryChange.InsertedText.Length;
        if (_committingComposition)
        {
            if (!nativeSelection.IsEmpty || nativeSelection.Active < primaryChange.NewRange.Start || nativeSelection.Active > primaryChange.NewRange.End)
                return null;
            caretOffset = nativeSelection.Active - primaryChange.NewRange.Start;
        }
        var replacesSelection = primaryChange.OldRange == primaryRange;
        var deletesBackward = primaryRange.IsEmpty && primaryChange.InsertedText.Length == 0 &&
            primaryChange.OldRange.End == primary.Active;
        var deletesForward = primaryRange.IsEmpty && primaryChange.InsertedText.Length == 0 &&
            primaryChange.OldRange.Start == primary.Active;
        if (!replacesSelection && !deletesBackward && !deletesForward)
            return null;

        if (hasVirtualSpace)
        {
            var targets = selections.Select(item =>
            {
                if (!item.Range.IsEmpty) return item;
                if (item.ActiveVirtualSpaces != 0 || item.AnchorVirtualSpaces != 0)
                {
                    var spaces = item.IsEmpty && deletesBackward ? Math.Max(0, item.ActiveVirtualSpaces - 1) : item.StartVirtualSpaces;
                    return primaryChange.InsertedText.Length == 0 ? item with { AnchorVirtualSpaces = spaces, ActiveVirtualSpaces = spaces } : item;
                }
                if (deletesBackward) return item with { Anchor = PreviousTextElement(before.TextBuffer, item.Active) };
                if (deletesForward) return item with { Active = item.Active == before.Length ? item.Active : item.Active + NextTextElementLength(before.TextBuffer, item.Active) };
                return item;
            }).ToArray();
            var plan = TextSelectionReplacement.Plan(before.TextBuffer, new(targets, selections.PrimaryIndex),
                selections.Select(_ => primaryChange.InsertedText).ToArray(), caretOffset);
            var undoPrimary = new RichTextTextChange(primaryChange.NewRange, before.GetText(primaryChange.OldRange));
            if (MaxLength >= 0 && plan.Length > MaxLength && plan.Length > before.Length)
                return new(before, [], native, (RichTextChange[])[undoPrimary], selections);
            var candidate = new RichTextDocumentEdit(before);
            foreach (var replacement in plan.Replacements.Reverse()) candidate.ReplaceText(replacement.Range, replacement.Text);
            return new(candidate.Snapshot, candidate.Changes, native, (RichTextChange[])[undoPrimary, .. candidate.Changes], plan.Selections);
        }

        var replacements = new List<(TextSelection Selection, TextSpan Range, string Text)>();
        foreach (var item in selections)
        {
            var range = new TextSpan(item.Range.Start, item.Range.Length);
            if (item.Id == primary.Id)
                range = primaryChange.OldRange;
            else if (range.IsEmpty && deletesBackward)
            {
                var start = PreviousTextElement(before.TextBuffer, item.Active);
                range = new(start, item.Active - start);
            }
            else if (range.IsEmpty && deletesForward)
            {
                var length = item.Active == before.Length ? 0 : NextTextElementLength(before.TextBuffer, item.Active);
                range = new(item.Active, length);
            }
            replacements.Add((item, range, primaryChange.InsertedText));
        }

        var ordered = replacements.OrderBy(item => item.Range.Start).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i].Range.Start < ordered[i - 1].Range.End)
                return null;

        var lengthAfter = before.Length + ordered.Sum(item => (long)item.Text.Length - item.Range.Length);
        if (lengthAfter > int.MaxValue || MaxLength >= 0 && lengthAfter > MaxLength && lengthAfter > before.Length)
            return new(before, [], native, RichTextDocument.CreateDelta(native, before), selections);

        var transaction = new RichTextDocumentEdit(native);
        foreach (var item in ordered.Reverse())
        {
            if (item.Selection.Id == primary.Id)
                continue;
            var mappedStart = TextSelectionStateExtensions.MapOffset(item.Range.Start, primaryChange);
            // Disjoint ranges retain their lengths through the primary replacement.
            transaction.ReplaceText(new(mappedStart, item.Range.Length), item.Text, before.GetCaretFormat(item.Range.Start));
        }
        var final = transaction.Snapshot;
        var changes = ordered.Reverse().Where(item => before.GetText(item.Range) != item.Text)
            .Select(item => (RichTextChange)new RichTextTextChange(item.Range, item.Text))
            .Concat(SourceSession is null ? RichTextDocument.CreateDelta(before, final).Where(change => change.Kind != RichTextChangeKind.Text) : []).ToArray();
        var afterSelections = new List<TextSelection>();
        var shift = 0;
        foreach (var item in ordered)
        {
            var end = item.Range.Start + shift + caretOffset;
            afterSelections.Add(item.Selection with { Anchor = end, Active = end, PreferredVisualX = null });
            shift += item.Text.Length - item.Range.Length;
        }

        return new(final, changes, native, transaction.Changes,
            new(afterSelections, afterSelections.FindIndex(item => item.Id == primary.Id)));
    }

    private static bool TryGetNativeReplacement(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after, TextSpan range, out string inserted)
    {
        inserted = "";
        var length = after.Length - before.Length + range.Length;
        if (length < 0) return false;
        // Slices share persistent subtrees. Repeated characters must still be
        // matched against the actual primary range, rather than a minimal diff
        // that could place the insertion at another identical character.
        if (!before.TextBuffer.Slice(new(0, range.Start)).ContentEquals(after.TextBuffer.Slice(new(0, range.Start))) ||
            !before.TextBuffer.Slice(new(range.End, before.Length - range.End)).ContentEquals(
                after.TextBuffer.Slice(new(range.Start + length, after.Length - range.Start - length)))) return false;
        inserted = after.GetText(new(range.Start, length));
        return true;
    }

    private static int NextTextElementLength(TextSnapshot text, int position)
    {
        var count = Math.Min(32, text.Length - position);
        while (true)
        {
            var part = text.GetText(new(position, count));
            var length = StringInfo.GetNextTextElementLength(part);
            if (length < count || position + count == text.Length) return length;
            count = (int)Math.Min((long)count * 2, text.Length - position);
        }
    }

    private static int PreviousTextElement(TextSnapshot text, int position)
    {
        if (position == 0)
            return 0;
        // A Unicode Prepend character can join a following ASCII letter.
        // The ASCII shortcut is safe only when its preceding context is ASCII.
        if (text[position - 1] <= '\x7f' && (position == 1 || text[position - 2] <= '\x7f'))
            return text[position - 1] == '\n' && position > 1 && text[position - 2] == '\r' ? position - 2 : position - 1;
        var line = text.GetLine(text.GetPosition(position).Line);
        var part = text.GetText(new(line.Span.Start, position - line.Span.Start));
        var start = 0;
        while (start < part.Length)
        {
            var next = start + StringInfo.GetNextTextElementLength(part.AsSpan(start));
            if (next >= part.Length)
                return line.Span.Start + start;
            start = next;
        }
        return line.Span.Start + start;
    }
}
