using System.Collections.Immutable;

namespace TextEdit.Core;

/// <summary>A view-owned provisional text root protected by one document input lease.</summary>
/// <remarks>Updates never enter document history. Commit reconciles mirrored carets and input rules before publishing one transaction.</remarks>
public sealed class TextComposition : IDisposable
{
    private readonly TextEditorSession _session;
    private readonly TextSnapshot _before;
    private readonly TextSelectionSet _beforeSelections;
    private readonly List<TextChange> _changes = [];
    private TextSpan _primaryRange;
    private bool _closed;
    private bool _canMirror = true;

    internal TextComposition(TextEditorSession session)
    {
        _session = session;
        _before = session.Document.CurrentSnapshot;
        _beforeSelections = session.Selections;
        Snapshot = _before.WithRevision(default);
        Selection = _beforeSelections.Primary;
        _primaryRange = Selection.Range;
    }

    /// <summary>Gets provisional source with an invalid document revision.</summary>
    public TextSnapshot Snapshot { get; private set; }
    /// <summary>Gets the native primary selection in provisional coordinates.</summary>
    public TextSelection Selection { get; private set; }
    /// <summary>Gets a generation incremented by every provisional update.</summary>
    public long Generation { get; private set; }
    /// <summary>Gets whether the completed primary replacement can be replicated to secondary selections.</summary>
    public bool CanMirrorSecondarySelections => _canMirror;

    /// <summary>Stages sequential native operations and their resulting primary selection.</summary>
    public void Update(Action<TextDocumentEdit> update, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(selection);
        VerifyOpen();
        var plan = _session.Document.Plan(() =>
        {
            var edit = new TextDocumentEdit(Snapshot);
            try { update(edit); return edit.Finish(); }
            finally { edit.Close(); }
        });
        if (plan.Selections is not null)
            throw new ArgumentException("Supply the provisional primary selection to Update instead of setting a document selection.", nameof(update));
        selection.Range.Validate(plan.Snapshot.Length);
        var length = Snapshot.Length;
        foreach (var change in plan.Changes)
        {
            if (change.OldSpan.Start < _primaryRange.Start || change.OldSpan.End > _primaryRange.End)
                _canMirror = false;
            var map = new PositionMap(length, [new(change.OldSpan, change.Inserted.Length)]);
            _primaryRange = map.Map(_primaryRange, PositionAffinity.BeforeInsertion, PositionAffinity.AfterInsertion)!.Value;
            length = map.NewLength;
        }
        _changes.AddRange(plan.Changes);
        Snapshot = plan.Snapshot.WithRevision(default);
        Selection = selection with { Id = _beforeSelections.Primary.Id };
        Generation++;
        _session.NotifyComposition();
    }

    /// <summary>Commits final text, mirrored simple replacements, and input rules as one history unit.</summary>
    public TextEditResult Commit(string? description = null)
    {
        VerifyOpen();
        var plan = _session.Document.Plan(PlanCompletion);
        _session.EndComposition(this);
        _closed = true;
        try
        {
            return _session.Edit(edit =>
            {
                foreach (var change in plan.Changes)
                    edit.Replace(change.OldSpan, change.Inserted.ToString());
                edit.SetSelections(plan.Selections);
            }, new(description, Origin: TextChangeOrigin.User), expectedRevision: _before.Revision);
        }
        finally { _session.Document.BreakTypingGroup(); _session.NotifyComposition(); }
    }

    private (ImmutableArray<TextChange> Changes, TextSelectionSet Selections) PlanCompletion()
    {
        var changes = _changes.ToList();
        var selections = _changes.Count == 0
            ? new TextSelectionSet(_beforeSelections.Select(item => item.Id == Selection.Id ? Selection : item), _beforeSelections.PrimaryIndex)
            : new TextSelectionSet((TextSelection[])[Selection]);
        var candidate = Snapshot;
        if (_changes.Count != 0 && _canMirror && _beforeSelections.Count > 1 && Selection.Range.IsEmpty &&
            Selection.Active >= _primaryRange.Start && Selection.Active <= _primaryRange.End)
        {
            var text = Snapshot.GetText(_primaryRange);
            var caretOffset = Selection.Active - _primaryRange.Start;
            var map = new PositionMap(_before.Length, _changes.Select(change => new PositionChange(change.OldSpan, change.Inserted.Length)));
            var edit = new TextDocumentEdit(candidate);
            try
            {
                foreach (var selection in _beforeSelections.Reverse())
                    if (selection.Id != _beforeSelections.Primary.Id)
                        edit.Replace(map.Map(selection.Range)!.Value, text);
                var mirrors = edit.Finish();
                candidate = mirrors.Snapshot;
                changes.AddRange(mirrors.Changes);
            }
            finally { edit.Close(); }
            var shift = 0;
            var result = new List<TextSelection>();
            foreach (var selection in _beforeSelections)
            {
                var position = selection.Range.Start + shift + caretOffset;
                result.Add(selection with { Anchor = position, Active = position, PreferredVisualX = null });
                shift += text.Length - selection.Range.Length;
            }
            selections = new(result, _beforeSelections.PrimaryIndex);
        }
        if (_changes.Count != 0 && _canMirror && Selection.Range.IsEmpty &&
            Selection.Active >= _primaryRange.Start && Selection.Active <= _primaryRange.End &&
            _beforeSelections.Any(item => item.AnchorVirtualSpaces != 0 || item.ActiveVirtualSpaces != 0))
        {
            var text = Snapshot.GetText(_primaryRange);
            var plan = TextSelectionReplacement.Plan(_before, _beforeSelections, _beforeSelections.Select(_ => text).ToArray(), Selection.Active - _primaryRange.Start);
            var edit = new TextDocumentEdit(_before);
            try
            {
                edit.ReplaceBatch(plan.Replacements);
                var result = edit.Finish();
                changes = result.Changes.ToList();
                candidate = result.Snapshot;
                selections = plan.Selections;
            }
            finally { edit.Close(); }
        }
        if (_session.InputRules.Count != 0 && changes.Count != 0)
        {
            var rules = _session.InputRules.Plan(_before, candidate, _beforeSelections, selections,
                changes.Select(change => new TextReplacement(change.OldSpan, change.Inserted.ToString())), isCompositionCommit: true);
            var edit = new TextDocumentEdit(candidate);
            try
            {
                edit.ReplaceBatch(rules.Replacements);
                changes.AddRange(edit.Finish().Changes);
            }
            finally { edit.Close(); }
            selections = rules.Selections;
        }
        return ([.. changes], selections);
    }

    /// <summary>Discards provisional text, retaining the original document, selections, version, and history.</summary>
    public void Cancel()
    {
        if (_closed)
            return;
        VerifyOpen();
        _session.EndComposition(this);
        _closed = true;
        _session.NotifyComposition();
    }
    private void VerifyOpen()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _session.Document.VerifyIdle();
    }
    /// <summary>Cancels an uncommitted composition.</summary>
    public void Dispose() => Cancel();
}
