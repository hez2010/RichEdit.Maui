using TextEdit.Core;

namespace RichEdit.Maui;

/// <summary>Reports an input plan that was rejected before changing the document.</summary>
public sealed class RichTextInputRejectedEventArgs(Exception error) : EventArgs
{
    /// <summary>Gets the rule or validation error. The native editor has been restored to committed content.</summary>
    public Exception Error { get; } = error;
}

public partial class NativeEditorView
{
    /// <summary>Gets synchronous rules that extend native input into one atomic edit. Rich-text replacements use LF line endings.</summary>
    public TextInputRules InputRules
    {
        get { VerifyAccess(); return _inputRules ??= new(VerifyAccess); }
    }
    private TextInputRules? _inputRules;

    /// <summary>Occurs after a failed input plan restores committed native content without creating history.</summary>
    public event EventHandler<RichTextInputRejectedEventArgs>? InputRejected;

    private bool _planningInput;

    private NativeSelectionEdit? PlanNativeInput(RichTextDocumentSnapshot native, TextSelectionState selection,
        NativeSelectionEdit? expanded, TextSpan? replacementHint = null)
    {
        if (InputRules.Count == 0 || ReferenceEquals(expanded?.Snapshot, Document.CurrentSnapshot))
            return expanded;
        var before = Document.CurrentSnapshot;
        var candidate = expanded?.Snapshot ?? native;
        var changes = expanded?.Changes ?? RichTextDocument.CreateDelta(before, candidate, replacementHint ?? (StructuredSession is not null ? SelectedRange : null));
        if (!changes.Any(change => change.Kind == RichTextChangeKind.Text))
            return expanded;

        var selections = expanded?.Selections ?? new TextSelectionSet((TextSelection[])
            [Selections.Primary with { Anchor = selection.Anchor, Active = selection.Active, PreferredVisualX = null, AnchorVirtualSpaces = 0, ActiveVirtualSpaces = 0 }]);
        try
        {
            _planningInput = true;
            var plan = Document.PlanInput(() => InputRules.Plan(before.TextBuffer, candidate.TextBuffer, Selections, selections,
                changes.OfType<RichTextTextChange>().Select(change => new TextReplacement(
                    new(change.OldRange.Start, change.OldRange.Length), change.InsertedText)), _committingComposition));
            if (MaxLength >= 0 && plan.Snapshot.Length > MaxLength && plan.Snapshot.Length > before.Length)
                throw new InvalidOperationException("The complete input plan exceeds MaxLength.");
            if (SourceSession is null && plan.Replacements.Any(edit => edit.Text.Contains('\r')))
                throw new ArgumentException("Rich-text input rules must use LF line endings.");
            var transaction = new RichTextDocumentEdit(candidate);
            foreach (var edit in plan.Replacements)
                transaction.ReplaceText(new(edit.Range.Start, edit.Range.Length), edit.Text);
            return new(transaction.Snapshot, (RichTextChange[])[.. changes, .. transaction.Changes], native,
                (RichTextChange[])[.. expanded?.NativeCorrections ?? [], .. transaction.Changes], plan.Selections);
        }
        catch (Exception exception)
        {
            _rejectedInput = exception;
            return new(before, [], native, [], Selections);
        }
        finally { _planningInput = false; }
    }

    private Exception? _rejectedInput;
}
