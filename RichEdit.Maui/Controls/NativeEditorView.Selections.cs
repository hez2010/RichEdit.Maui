using TextEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    /// <summary>Identifies the two-way bindable <see cref="Selections"/> property.</summary>
    public static readonly BindableProperty SelectionsProperty = BindableProperty.Create(
        nameof(Selections), typeof(TextSelectionSet), typeof(NativeEditorView), defaultBindingMode: BindingMode.TwoWay,
        defaultValueCreator: static _ => TextSelectionSet.Caret(0),
        validateValue: static (view, value) =>
        {
            var editor = (NativeEditorView)view;
            editor.VerifyAccess();
            editor.VerifySelectionMutation();
            if (value is not TextSelectionSet selections)
                return false;
            selections.Validate(editor.Document.Length);
            return true;
        },
        propertyChanged: static (view, _, _) => ((NativeEditorView)view).OnSelectionsChanged());

    private bool _updatingSelections;
    private TextSelectionSet? _pendingSelections;
    private TextSelectionSet? _selectionsBeforeNotification;

    /// <summary>Gets or sets every selection. The primary selection uses the native caret and input system.</summary>
    public TextSelectionSet Selections
    {
        get => (TextSelectionSet)GetValue(SelectionsProperty);
        set => SetValue(SelectionsProperty, value);
    }

    /// <summary>Occurs after the selection set changes, including secondary selections.</summary>
    public event EventHandler? SelectionsChanged;

    /// <summary>Adds a caret, optionally making it the primary native selection.</summary>
    public void AddCaret(int position, bool makePrimary = true)
    {
        VerifyAccess();
        new TextSpan(position, 0).Validate(Document.Length);
        var items = Selections.ToList();
        var index = items.FindIndex(item => item.Range.IsEmpty && item.Active == position);
        if (index < 0)
        {
            index = items.Count;
            items.Add(new(position, position));
        }
        Selections = new(items, makePrimary ? index : Selections.PrimaryIndex);
    }

    /// <summary>Retains the current primary selection and removes all secondary selections.</summary>
    public void ClearSecondarySelections()
    {
        VerifyAccess();
        if (Selections.Count > 1)
            Selections = new((TextSelection[])[Selections.Primary]);
    }

    private void OnSelectionsChanged()
    {
        var primary = Selections.Primary;
        var previous = _updatingSelections;
        _updatingSelections = true;
        try { SetSelectionCore(new(primary.Anchor, primary.Active), fromPlatform: _synchronizingSelection || _deferNotifications); }
        finally { _updatingSelections = previous; }
        if (!_deferNotifications)
        {
            SynchronizeSourceSelection();
            SynchronizeStructuredSelection();
            Document.BreakTypingUndoGroup();
            Commands?.Refresh();
            SelectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SynchronizeSelectionSet(TextSelectionState selection)
    {
        if (_updatingSelections)
            return;
        var before = Selections;
        var primary = before.Primary with { Anchor = selection.Anchor, Active = selection.Active, PreferredVisualX = null };
        var items = _synchronizingSelection || _deferNotifications
            ? before.Select(item => item.Id == primary.Id ? primary : item).ToArray()
            : (TextSelection[])[primary];
        var next = new TextSelectionSet(items, Array.FindIndex(items, item => item.Id == primary.Id));
        if (!SameSelections(before, next))
            Selections = next;
    }

    private void SetSelectionsFromDocument(TextSelectionSet selections)
    {
        if (!SameSelections(Selections, selections))
            Selections = selections;
    }

    internal TextSelectionSet GetSelectionsAfterEdit(IEnumerable<RichTextChange> changes, int length)
    {
        if (_pendingSelections is { } requested)
        {
            requested.Validate(length);
            return requested;
        }

        var changeArray = changes.ToArray();
        var current = Selections;
        var items = current.Select(item =>
        {
            var mapped = new TextSelectionState(item.Anchor, item.Active).Map(changeArray, length);
            if (item.Id == current.Primary.Id)
                mapped = GetSelectionAfterEdit(changeArray, length);
            return item with { Anchor = mapped.Anchor, Active = mapped.Active, PreferredVisualX = null };
        }).ToArray();
        return new(items, current.PrimaryIndex);
    }

    private static bool SameSelections(TextSelectionSet first, TextSelectionSet second) =>
        first.Primary.Id == second.Primary.Id && first.SequenceEqual(second);

    /// <summary>Replaces all selections atomically and collapses each caret after its inserted text.</summary>
    /// <returns>False when the view is read-only, composition is active, or the complete edit exceeds its input limit.</returns>
    public bool ReplaceSelections(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        VerifyAccess();
        if (SourceSession is not null) return ReplaceSourceSelections(Selections.Select(_ => text).ToArray());
        return ReplaceSelectionFragments(Selections.Select(_ => RichTextDocumentFragment.FromPlainText(text)).ToArray());
    }

    internal bool ReplaceRichSelections(RichTextDocumentFragment fragment) =>
        ReplaceSelectionFragments(Selections.Select(_ => fragment).ToArray());

    private void ResetSelectionsForDocument(TextSelectionState selection)
    {
        var previous = _synchronizingSelection;
        _synchronizingSelection = true;
        try
        {
            Selections = new((TextSelection[])[Selections.Primary with { Anchor = selection.Anchor, Active = selection.Active }]);
        }
        finally { _synchronizingSelection = previous; }
    }

    private bool ReplaceSelectionFragments(IReadOnlyList<RichTextDocumentFragment> fragments)
    {
        var before = Selections;
        if (fragments.Count != before.Count)
            throw new ArgumentException("Supply one fragment per selection.", nameof(fragments));
        if (IsReadOnly || Document.HasActiveComposition)
            return false;

        var length = (long)Document.Length;
        for (var i = 0; i < before.Count; i++)
            length += fragments[i].Text.Length - before[i].Range.Length;
        if (length > int.MaxValue || MaxLength >= 0 && length > MaxLength && length > Document.Length)
            return false;

        var positions = new TextSelection[before.Count];
        var shift = 0;
        for (var i = 0; i < before.Count; i++)
        {
            var item = before[i];
            var end = item.Range.Start + shift + fragments[i].Text.Length;
            positions[i] = item with { Anchor = end, Active = end, PreferredVisualX = null };
            shift += fragments[i].Text.Length - item.Range.Length;
        }
        var pending = _pendingSelections;
        _pendingSelections = new(positions, before.PrimaryIndex);
        try
        {
            EditDocument(edit =>
            {
                for (var i = before.Count - 1; i >= 0; i--)
                    edit.ReplaceFragment(new(before[i].Range.Start, before[i].Range.Length), fragments[i]);
            });
            SetSelectionsFromDocument(_pendingSelections);
        }
        finally { _pendingSelections = pending; }
        return true;
    }

    private static (RichTextDocumentFragment Payload, RichTextDocumentFragment[] Parts) CreateSelectionClipboard(
        RichTextDocumentSnapshot snapshot, TextSelectionSet selections)
    {
        var parts = selections.Select(item => RichTextDocumentFragment.FromRange(snapshot,
            new(item.Range.Start, item.Range.Length))).ToArray();
        if (parts.Length == 1)
            return (parts[0], parts);

        var combined = new RichTextDocument();
        combined.Edit(edit =>
        {
            var position = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    edit.InsertText(position++, "\n");
                edit.ReplaceFragment(new(position, 0), parts[i]);
                position += parts[i].Text.Length;
            }
        }, new(RichTextUndoBehavior.ClearHistory));
        return (RichTextDocumentFragment.FromRange(combined.CurrentSnapshot, new(0, combined.Length)), parts);
    }
}
