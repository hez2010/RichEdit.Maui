using TextEdit.Core;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    /// <summary>Identifies the two-way bindable <see cref="Selections"/> property.</summary>
    public static readonly BindableProperty SelectionsProperty = BindableProperty.Create(
        nameof(Selections), typeof(TextSelectionSet), typeof(CodeEditor), defaultBindingMode: BindingMode.TwoWay,
        defaultValueCreator: static view => ((CodeEditor)view).TextView.Selections,
        validateValue: static (view, value) =>
        {
            var editor = (CodeEditor)view;
            editor.VerifyAccess();
            if (value is not TextSelectionSet selections)
                return false;
            selections.Validate(editor.Document.CurrentSnapshot.TextBuffer);
            return true;
        },
        propertyChanged: static (view, _, value) =>
        {
            var editor = (CodeEditor)view;
            if (!editor._synchronizingSelection)
                editor.TextView.Selections = (TextSelectionSet)value;
        });

    /// <summary>Gets or sets every source selection. The primary selection controls native input.</summary>
    public TextSelectionSet Selections
    {
        get => TextView.Selections;
        set => SetValue(SelectionsProperty, value);
    }

    /// <summary>Occurs after primary or secondary selections change.</summary>
    public event EventHandler? SelectionsChanged;

    /// <summary>Replaces all selections in one checked transaction and undo entry.</summary>
    public bool ReplaceSelections(string text) => TextView.ReplaceSelections(text);

    /// <summary>Adds a source caret, optionally making it the primary native selection.</summary>
    public void AddCaret(int position, bool makePrimary = true) => TextView.AddCaret(position, makePrimary);

    /// <summary>Retains the primary selection and removes all secondary selections.</summary>
    public void ClearSecondarySelections() => TextView.ClearSecondarySelections();

    private void InitializeSelections()
    {
        TextView.SelectionsChanged += (_, _) =>
        {
            var previous = _synchronizingSelection;
            _synchronizingSelection = true;
            try { SetValue(SelectionsProperty, TextView.Selections); }
            finally { _synchronizingSelection = previous; }
            SelectionsChanged?.Invoke(this, EventArgs.Empty);
        };
    }
}
