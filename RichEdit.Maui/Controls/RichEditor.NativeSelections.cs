using System.Globalization;
using TextEdit.Core;

namespace RichEdit.Maui;

public sealed partial class RichEditor
{
    private sealed record NativeSelectionEdit(
        RichTextDocumentSnapshot Snapshot,
        IReadOnlyList<RichTextChange> Changes,
        RichTextDocumentSnapshot NativeBefore,
        IReadOnlyList<RichTextChange> NativeCorrections,
        TextSelectionSet Selections);

    private NativeSelectionEdit? ExpandNativeSelectionEdit(RichTextDocumentSnapshot native, RichTextSelectionState nativeSelection)
    {
        if (Selections.Count < 2 || Composition.IsActive || (Handler as IRichEditorHandler)?.IsComposing == true)
            return null;

        var before = Document.CurrentSnapshot;
        var selections = Selections;
        var primary = selections.Primary;
        var primaryRange = new RichTextRange(primary.Range.Start, primary.Range.Length);
        RichTextTextChange? primaryChange = null;
        if (RichTextDocumentSnapshot.TryGetReplacement(before.Text, native.Text, primaryRange, out var inserted) &&
            (before.Text != native.Text || !primaryRange.IsEmpty && nativeSelection.IsEmpty &&
                nativeSelection.Active == primaryRange.Start + inserted.Length))
            primaryChange = new(primaryRange, inserted);
        else if (primaryRange.IsEmpty && before.Length > native.Length)
        {
            var backward = new RichTextRange(PreviousTextElement(before.Text, primary.Active),
                primary.Active - PreviousTextElement(before.Text, primary.Active));
            var forward = new RichTextRange(primary.Active, primary.Active == before.Length ? 0 :
                StringInfo.GetNextTextElementLength(before.Text.AsSpan(primary.Active)));
            if (RichTextDocumentSnapshot.TryGetReplacement(before.Text, native.Text, backward, out var backwardText) && backwardText.Length == 0)
                primaryChange = new(backward, "");
            else if (RichTextDocumentSnapshot.TryGetReplacement(before.Text, native.Text, forward, out var forwardText) && forwardText.Length == 0)
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

        var replacements = new List<(TextSelection Selection, RichTextRange Range, string Text)>();
        foreach (var item in selections)
        {
            var range = new RichTextRange(item.Range.Start, item.Range.Length);
            if (item.Id == primary.Id)
                range = primaryChange.OldRange;
            else if (range.IsEmpty && deletesBackward)
            {
                var start = PreviousTextElement(before.Text, item.Active);
                range = new(start, item.Active - start);
            }
            else if (range.IsEmpty && deletesForward)
            {
                var length = item.Active == before.Length ? 0 : StringInfo.GetNextTextElementLength(before.Text.AsSpan(item.Active));
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
            var mappedStart = RichTextSelectionState.MapOffset(item.Range.Start, primaryChange);
            // Disjoint ranges retain their lengths through the primary replacement.
            transaction.ReplaceText(new(mappedStart, item.Range.Length), item.Text, before.GetCaretFormat(item.Range.Start));
        }
        var final = transaction.Snapshot;
        var changes = ordered.Reverse().Where(item => !before.Text.AsSpan(item.Range.Start, item.Range.Length).SequenceEqual(item.Text))
            .Select(item => (RichTextChange)new RichTextTextChange(item.Range, item.Text))
            .Concat(RichTextDocument.CreateDelta(before, final).Where(change => change.Kind != RichTextChangeKind.Text)).ToArray();
        var afterSelections = new List<TextSelection>();
        var shift = 0;
        foreach (var item in ordered)
        {
            var end = item.Range.Start + shift + caretOffset;
            afterSelections.Add(item.Selection with { Anchor = end, Active = end });
            shift += item.Text.Length - item.Range.Length;
        }

        return new(final, changes, native, transaction.Changes,
            new(afterSelections, afterSelections.FindIndex(item => item.Id == primary.Id)));
    }

    private static int PreviousTextElement(string text, int position)
    {
        if (position == 0)
            return 0;
        var start = text.LastIndexOf('\n', position - 1) + 1;
        if (start == position)
            return position - 1;
        while (start < position)
        {
            var next = start + StringInfo.GetNextTextElementLength(text.AsSpan(start));
            if (next >= position)
                return start;
            start = next;
        }
        return start;
    }
}
