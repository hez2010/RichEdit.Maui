namespace RichEdit.Maui;

public partial class NativeEditorView
{
    internal TextEditorSession? SourceSession { get; private set; }
    internal string SourceNewLine { get; set; } = "\n";
    internal int SourceTabSize { get; set; } = 4;
    internal bool SourceWordWrap { get; set; } = true;
    internal bool SourceVirtualSpaceEnabled { get; set; }
    internal event EventHandler<TextPastingEventArgs>? SourcePasting;

    private Task CopySourceAsync() => Selections.All(selection => selection.IsEmpty) ? Task.CompletedTask :
        PlainTextClipboard.SetAsync(Selections.Select(selection => Document.GetText(selection.Range) +
            new string(' ', selection.Range.IsEmpty ? Math.Abs(selection.ActiveVirtualSpaces - selection.AnchorVirtualSpaces) : selection.EndVirtualSpaces)).ToArray());

    private async Task CutSourceAsync()
    {
        var session = SourceSession!;
        var revision = session.Document.Revision;
        var generation = session.SelectionGeneration;
        if (IsReadOnly || Document.HasActiveComposition || Selections.All(selection => selection.IsEmpty)) return;
        await CopySourceAsync();
        if (ReferenceEquals(SourceSession, session) && revision == session.Document.Revision && generation == session.SelectionGeneration)
            ReplaceSourceSelections(Selections.Select(_ => "").ToArray());
    }

    private async Task PasteSourceAsync()
    {
        var session = SourceSession!;
        var revision = session.Document.Revision;
        var generation = session.SelectionGeneration;
        if (IsReadOnly || Document.HasActiveComposition) return;
        var clipboard = await PlainTextClipboard.GetAsync();
        if (clipboard.Text is null) return;
        var args = new TextPastingEventArgs(clipboard.Text);
        SourcePasting?.Invoke(this, args);
        ArgumentNullException.ThrowIfNull(args.Text);
        if (!args.Cancel && ReferenceEquals(SourceSession, session) && revision == session.Document.Revision && generation == session.SelectionGeneration)
            ReplaceSourceSelections(args.Text == clipboard.Text && clipboard.Parts?.Length == Selections.Count ? clipboard.Parts :
                Selections.Select(_ => args.Text).ToArray());
    }

    internal sealed record SourceReadback(RichTextDocumentSnapshot Snapshot, TextSelectionState Selection, LfTextProjection Projection);

    internal SourceReadback RestoreNativeSource(
        RichTextDocumentSnapshot native, TextSelectionState selection, TextSpan? knownSourceReplacement = null)
    {
        var before = InputSnapshot;
        var projection = ReferenceEquals(before.TextBuffer, Document.SourceLineEndings?.Source)
            ? Document.SourceLineEndings : new LfTextProjection(before.TextBuffer);
        if (knownSourceReplacement is { } known)
        {
            var displayRange = projection.ToDisplay(known);
            var insertedLength = native.Length - (projection.Display.Length - displayRange.Length);
            if (insertedLength >= 0 && displayRange.Start <= native.Length - insertedLength)
            {
                var inserted = native.GetText(new(displayRange.Start, insertedLength));
                if (projection.Display.GetText(displayRange) == inserted)
                    return new(before, new(projection.ToSource(selection.Anchor), projection.ToSource(selection.Active)), projection);
                var updated = projection.Source.Replace(known, inserted.Replace("\n", SourceNewLine, StringComparison.Ordinal));
                var mapped = new LfTextProjection(updated, native.TextBuffer);
                return new(RichTextDocumentSnapshot.CreateSourceProjection(updated), new(mapped.ToSource(selection.Anchor), mapped.ToSource(selection.Active)), mapped);
            }
        }
        var text = projection.Display.ToString();
        if (text == native.Text)
            return new(before, new(projection.ToSource(selection.Anchor), projection.ToSource(selection.Active)), projection);
        var hint = projection.ToDisplay(InputSelection.Range);
        TextSpan range;
        string replacement;
        if (RichTextDocumentSnapshot.TryGetReplacement(text, native.Text, hint, out replacement)) range = hint;
        else
        {
            var prefix = text.AsSpan().CommonPrefixLength(native.Text);
            var suffix = 0;
            while (suffix < Math.Min(text.Length, native.Length) - prefix && text[^(suffix + 1)] == native.Text[^(suffix + 1)]) suffix++;
            range = new(prefix, text.Length - prefix - suffix);
            replacement = native.Text.Substring(prefix, native.Length - prefix - suffix);
        }
        var raw = projection.Source.Replace(projection.ToSource(range), replacement.Replace("\n", SourceNewLine, StringComparison.Ordinal));
        var after = new LfTextProjection(raw, native.TextBuffer);
        return new(RichTextDocumentSnapshot.CreateSourceProjection(raw), new(after.ToSource(selection.Anchor), after.ToSource(selection.Active)), after);
    }

    internal void BindSourceDocument(TextDocument source)
    {
        VerifyAccess();
        if (ReferenceEquals(SourceSession?.Document, source)) return;
        Document.VerifyAttachmentChange();
        source.VerifyIdle();
        var next = source.CreateSession();
        var projection = RichTextDocument.ProjectSource(source);
        var previous = SourceSession;
        DiscardComposition();
        SourceSession = next;
        try
        {
            Document = projection;
            SynchronizeSourceSelection();
        }
        catch
        {
            if (!ReferenceEquals(Document, projection))
            {
                SourceSession = previous;
                next.Dispose();
            }
            else previous?.Dispose();
            throw;
        }
        previous?.Dispose();
    }

    private void SynchronizeSourceSelection()
    {
        if (SourceSession is { } session && !SameSelections(session.Selections, Selections))
            session.Selections = Selections;
    }

    internal bool TryApplySourceEdits(DocumentRevision revision, IReadOnlyList<TextReplacement> replacements,
        TextSelectionState? selection, TextEditOptions options, long? expectedSelectionGeneration = null)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(replacements);
        foreach (var replacement in replacements)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            ArgumentNullException.ThrowIfNull(replacement.Text);
        }
        if (IsReadOnly || Document.HasActiveComposition || revision != Document.Revision) return false;
        var length = Document.Length + replacements.Sum(edit => (long)edit.Text.Length - edit.Range.Length);
        if (length > int.MaxValue || MaxLength >= 0 && length > MaxLength && length > Document.Length) return false;
        var result = SourceSession!.Edit(edit =>
        {
            edit.ReplaceBatch(replacements);
            if (MaxLength >= 0 && edit.Snapshot.Length > MaxLength && edit.Snapshot.Length > Document.Length)
                throw new ArgumentException("The source edit exceeds MaxLength.", nameof(replacements));
            if (selection is { } requested)
                edit.SetSelections(new((TextSelection[])[Selections.Primary with { Anchor = requested.Anchor, Active = requested.Active,
                    AnchorVirtualSpaces = 0, ActiveVirtualSpaces = 0, PreferredVisualX = null }]));
        }, options, revision, expectedSelectionGeneration);
        if (result.Accepted && result.Change is null) Selections = SourceSession.Selections;
        return result.Accepted;
    }

    internal bool ReplaceSourceSelections(IReadOnlyList<string> text)
    {
        var session = SourceSession!;
        if (IsReadOnly || Document.HasActiveComposition) return false;
        var before = Selections;
        var plan = TextSelectionReplacement.Plan(session.Document.CurrentSnapshot, before, text);
        if (MaxLength >= 0 && plan.Length > MaxLength && plan.Length > Document.Length) return false;
        var result = session.Edit(edit =>
        {
            edit.ReplaceBatch(plan.Replacements);
            edit.SetSelections(plan.Selections);
        });
        if (result.Accepted && result.Change is null) Selections = session.Selections;
        return result.Accepted;
    }

    private bool DeleteSourceVirtualSelections(bool backward)
    {
        if (IsReadOnly || Composition.IsActive) return false;
        var text = Document.Text;
        var targets = Selections.Select(item =>
        {
            if (!item.IsEmpty) return item;
            if (item.ActiveVirtualSpaces != 0)
            {
                var spaces = Math.Max(0, item.ActiveVirtualSpaces - (backward ? 1 : 0));
                return item with { AnchorVirtualSpaces = spaces, ActiveVirtualSpaces = spaces };
            }
            return backward ? item with { Anchor = PreviousTextElement(text, item.Active) } :
                item with { Active = item.Active == text.Length ? item.Active : item.Active + System.Globalization.StringInfo.GetNextTextElementLength(text.AsSpan(item.Active)) };
        });
        var selections = new TextSelectionSet(targets, Selections.PrimaryIndex);
        var plan = TextSelectionReplacement.Plan(Document.CurrentSnapshot.TextBuffer, selections, selections.Select(_ => "").ToArray());
        var result = SourceSession!.Edit(edit => { edit.ReplaceBatch(plan.Replacements); edit.SetSelections(plan.Selections); });
        if (result.Accepted && result.Change is null) Selections = SourceSession.Selections;
        return result.Accepted;
    }
}
