namespace RichEdit.Maui;

/// <summary>A replacement expressed in the original revision's UTF-16 coordinates.</summary>
/// <param name="Range">The source range to replace.</param>
/// <param name="Text">The replacement text. Line endings are normalized to LF.</param>
public sealed record RichTextEdit(RichTextRange Range, string Text);

public partial class RichEditor
{
    /// <summary>Atomically applies original-coordinate edits if this view still accepts the captured revision.</summary>
    /// <param name="revision">The captured document revision.</param>
    /// <param name="edits">Non-overlapping replacements. Equal-offset insertions retain caller order.</param>
    /// <param name="selectionAfter">An optional directional selection in the resulting normalized source.</param>
    /// <param name="options">History behavior and application metadata.</param>
    /// <returns>False for stale revisions, read-only state, composition, or an exceeded input limit.</returns>
    public bool TryApplyEdits(RichTextRevision revision, IReadOnlyList<RichTextEdit> edits,
        RichTextSelectionState? selectionAfter = null, RichTextEditOptions options = default)
    {
        VerifyAccess();
        Document.VerifyAttachmentChange();
        ArgumentNullException.ThrowIfNull(edits);
        if (!Enum.IsDefined(options.UndoBehavior)) throw new ArgumentOutOfRangeException(nameof(options));
        if (!CanApply()) return false;
        var source = Document.CurrentSnapshot;
        var requested = edits.Select(edit =>
        {
            ArgumentNullException.ThrowIfNull(edit);
            ArgumentNullException.ThrowIfNull(edit.Text);
            edit.Range.Validate(source.Length, nameof(edits));
            return edit with { Text = RichTextDocumentSnapshot.NormalizeText(edit.Text) };
        }).OrderBy(static edit => edit.Range.Start).ToArray();
        var normalized = new List<RichTextEdit>();
        var previousEnd = 0;
        foreach (var group in requested.GroupBy(static edit => edit.Range.Start))
        {
            if (group.Key < previousEnd) throw new ArgumentException("Edit ranges overlap.", nameof(edits));
            var replacements = group.Where(static edit => !edit.Range.IsEmpty).ToArray();
            if (replacements.Length > 1) throw new ArgumentException("Edit ranges overlap.", nameof(edits));
            var replacement = replacements.FirstOrDefault();
            var range = replacement?.Range ?? new RichTextRange(group.Key, 0);
            var text = string.Concat(group.Where(static edit => edit.Range.IsEmpty).Select(static edit => edit.Text)) + replacement?.Text;
            normalized.Add(new(range, text));
            previousEnd = range.End;
        }
        var length = source.Length + normalized.Sum(static edit => (long)edit.Text.Length - edit.Range.Length);
        if (length > int.MaxValue || MaxLength >= 0 && length > MaxLength && length > source.Length) return false;
        selectionAfter?.Range.Validate((int)length, nameof(selectionAfter));
        if (!CanApply()) return false;
        normalized.RemoveAll(edit => source.Text.AsSpan(edit.Range.Start, edit.Range.Length).SequenceEqual(edit.Text));
        var selection = SelectionState;
        for (var i = normalized.Count - 1; i >= 0; i--)
        {
            var edit = normalized[i];
            var change = new RichTextTextChange(edit.Range, edit.Text);
            selection = new(RichTextSelectionState.MapOffset(selection.Anchor, change), RichTextSelectionState.MapOffset(selection.Active, change));
        }
        EditDocument(transaction =>
        {
            for (var i = normalized.Count - 1; i >= 0; i--)
            {
                var edit = normalized[i];
                if (!source.Text.AsSpan(edit.Range.Start, edit.Range.Length).SequenceEqual(edit.Text))
                    transaction.ReplaceText(edit.Range, edit.Text);
            }
        }, selectionAfter ?? selection, options);
        return true;

        bool CanApply() => revision == Document.Revision && !IsReadOnly && !Composition.IsActive && (Handler as IRichEditorHandler)?.IsComposing != true;
    }
}
