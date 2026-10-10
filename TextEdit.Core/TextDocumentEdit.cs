using System.Collections.Immutable;

namespace TextEdit.Core;

/// <summary>A recorded text replacement with shared, immutable inserted and removed content.</summary>
public sealed class TextChange
{
    internal TextChange(TextSpan range, TextSnapshot removed, TextSnapshot inserted)
    {
        OldSpan = range;
        Removed = removed;
        Inserted = inserted;
    }
    /// <summary>Gets the replaced range in coordinates after preceding changes.</summary>
    public TextSpan OldSpan { get; }
    /// <summary>Gets the inserted range in coordinates after this change.</summary>
    public TextSpan NewSpan => new(OldSpan.Start, Inserted.Length);
    /// <summary>Gets the immutable removed text, retained for inverse operations.</summary>
    public TextSnapshot Removed { get; }
    /// <summary>Gets the immutable inserted text.</summary>
    public TextSnapshot Inserted { get; }
    internal TextChange Invert() => new(NewSpan, Inserted, Removed);
}

/// <summary>Builds sequential plain-text changes without mutating the live document.</summary>
public sealed class TextDocumentEdit
{
    private bool _open = true;
    private TextSnapshot _snapshot;
    private readonly List<TextChange> _changes = [];
    private TextSelectionSet? _selections;
    internal TextDocumentEdit(TextSnapshot snapshot) => _snapshot = snapshot;

    /// <summary>Gets the candidate after all preceding operations.</summary>
    public TextSnapshot Snapshot { get { VerifyOpen(); return _snapshot; } }

    /// <summary>Replaces a bounded UTF-16 range without normalizing line endings.</summary>
    public void Replace(TextSpan range, string text)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(text);
        var next = _snapshot.Replace(range, text);
        if (ReferenceEquals(next, _snapshot))
            return;
        _changes.Add(new(range, _snapshot.Slice(range), TextSnapshot.FromText(text)));
        _snapshot = next;
    }

    /// <summary>Inserts text at an exact UTF-16 offset.</summary>
    public void Insert(int position, string text) => Replace(new(position, 0), text);
    /// <summary>Deletes a bounded UTF-16 range.</summary>
    public void Delete(TextSpan range) => Replace(range, "");

    /// <summary>Applies disjoint replacements in the current candidate's original coordinates. Same-position insertions retain caller order.</summary>
    public void ReplaceBatch(IEnumerable<TextReplacement> replacements)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(replacements);
        var supplied = replacements.ToArray();
        foreach (var item in supplied)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Text);
            item.Range.Validate(_snapshot.Length);
        }
        var edits = supplied.Select((edit, order) => (Edit: edit, Order: order)).OrderBy(item => item.Edit.Range.Start)
            .ThenBy(item => item.Order).ToArray();
        for (var i = 1; i < edits.Length; i++)
        {
            var before = edits[i - 1].Edit.Range;
            var after = edits[i].Edit.Range;
            if (after.Start < before.End || after.Start == before.Start && (!before.IsEmpty || !after.IsEmpty))
                throw new ArgumentException("Batch replacements cannot overlap.", nameof(replacements));
        }
        foreach (var item in edits.Reverse())
            Replace(item.Edit.Range, item.Edit.Text);
    }

    /// <summary>Sets the invoking session's selection in final candidate coordinates.</summary>
    public void SetSelections(TextSelectionSet selections)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(selections);
        _selections = selections;
    }

    private void VerifyOpen()
    {
        if (!_open)
            throw new InvalidOperationException("A document edit is valid only during its transaction callback.");
    }

    internal (TextSnapshot Snapshot, ImmutableArray<TextChange> Changes, TextSelectionSet? Selections) Finish()
    {
        _open = false;
        _selections?.Validate(_snapshot.Length);
        return (_snapshot, [.. _changes], _selections);
    }

    internal void Close() => _open = false;
}
