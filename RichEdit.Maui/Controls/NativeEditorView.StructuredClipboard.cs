using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private sealed class PasteLengthExceededException : Exception;
    internal event EventHandler<RichPastingEventArgs>? StructuredPasting;

    internal bool HasClipboardSelection => StructuredSession is null ? Selections.Any(item => !item.Range.IsEmpty) : CanCopyStructured;
    private bool CanCopyStructured => StructuredSession is { } session && session.Selection switch
    {
        RichBoundarySelection => false,
        RichEdit.Core.RichTextSelection text => text.Anchor != text.Active,
        RichTextSelectionSet set => set.Any(item => item.Selection.Anchor != item.Selection.Active),
        _ => true,
    };

    private (RichDocumentFragment Payload, RichDocumentFragment[]? Parts) CreateStructuredClipboard()
    {
        var session = StructuredSession!;
        var snapshot = session.Document.CurrentSnapshot;
        if (session.Selection is not RichTextSelectionSet set)
            return (RichDocumentFragment.FromSelection(snapshot, session.Selection), null);
        var parts = set.Where(item => item.Selection.Anchor != item.Selection.Active)
            .Select(item => RichDocumentFragment.FromSelection(snapshot, item.Selection)).ToArray();
        if (parts.Length == 1) return (parts[0], parts);
        var combined = RichDocumentSnapshot.Build(edit =>
        {
            var index = 1;
            foreach (var part in parts) index += edit.InsertFragment(edit.Body, index, part).Length;
            edit.DeleteBlock(new(2));
        });
        return (new(combined), parts);
    }

    private Task CopyStructuredAsync()
    {
        if (!CanCopyStructured) return Task.CompletedTask;
        var clipboard = CreateStructuredClipboard();
        return RichTextClipboard.SetStructuredAsync(clipboard.Payload, clipboard.Parts);
    }

    private async Task CutStructuredAsync()
    {
        var session = StructuredSession!;
        if (IsReadOnly || session.Document.ComposingSession is not null || !CanCopyStructured) return;
        var revision = session.Document.Revision;
        var generation = session.SelectionGeneration;
        await CopyStructuredAsync();
        if (!ReferenceEquals(StructuredSession, session) || IsReadOnly) return;
        session.Edit(edit => edit.SetSelection(edit.ReplaceSelection(session.Selection, "")),
            new("Cut", Origin: TextChangeOrigin.User), revision, generation);
    }

    private async Task PasteStructuredAsync(bool asPlainText)
    {
        var session = StructuredSession!;
        if (IsReadOnly || session.Document.ComposingSession is not null) return;
        var revision = session.Document.Revision;
        var generation = session.SelectionGeneration;
        var content = await RichTextClipboard.GetStructuredAsync(asPlainText);
        if (content is null || !ReferenceEquals(StructuredSession, session) || IsReadOnly ||
            session.Document.Revision != revision || session.SelectionGeneration != generation || session.Document.ComposingSession is not null) return;
        var args = new RichPastingEventArgs(content.Fragment, content.IsPlainText, content.Diagnostics);
        StructuredPasting?.Invoke(this, args);
        if (args.Cancel || !ReferenceEquals(StructuredSession, session) || IsReadOnly) return;
        try { session.Edit(edit =>
        {
            RichSelection result;
            if (session.Selection is RichTextSelectionSet set && content.Parts?.Count == set.Count && ReferenceEquals(args.Fragment, content.Fragment))
                result = args.UsePlainText
                    ? edit.ReplaceSelections(set, content.Parts.Select(part => part.Content.ProjectText().Text.ToString()).ToArray(), session.TypingCharacterFormat)
                    : edit.ReplaceSelections(set, content.Parts);
            else result = args.UsePlainText
                ? edit.ReplaceSelection(session.Selection, args.Fragment.Content.ProjectText().Text.ToString(), session.TypingCharacterFormat)
                : edit.ReplaceSelection(session.Selection, args.Fragment);
            if (MaxLength >= 0 && edit.Snapshot.ProjectText().Text.Length > MaxLength)
                throw new PasteLengthExceededException();
            edit.SetSelection(result);
        }, new("Paste", Origin: TextChangeOrigin.User), revision, generation); }
        catch (PasteLengthExceededException) { }
    }
}
