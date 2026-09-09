using System.ComponentModel;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A stable source-only selection facade.</summary>
public sealed partial class CodeTextSelection : INotifyPropertyChanged
{
    private readonly RichTextSelection _selection;
    internal CodeTextSelection(RichTextSelection selection)
    {
        _selection = selection;
        selection.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Range) or nameof(State) or nameof(Anchor) or nameof(Active) or nameof(Text))
                PropertyChanged?.Invoke(this, args);
        };
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Gets the normalized UTF-16 range.</summary>
    public RichTextRange Range => _selection.Range;
    /// <summary>Gets the anchor and active endpoints.</summary>
    public RichTextSelectionState State => _selection.State;
    /// <summary>Gets the fixed endpoint.</summary>
    public int Anchor => State.Anchor;
    /// <summary>Gets the caret endpoint.</summary>
    public int Active => State.Active;
    /// <summary>Gets the selected source.</summary>
    public string Text => _selection.Text;
    /// <summary>Selects a range with the caret at its end.</summary>
    /// <param name="range">The range to select.</param>
    public void Select(RichTextRange range) => _selection.Select(range);
    /// <summary>Sets a directional selection.</summary>
    /// <param name="selection">The anchor and active offsets.</param>
    public void Select(RichTextSelectionState selection) => _selection.Select(selection);
    /// <summary>Replaces the selection with source text and advances the caret.</summary>
    /// <param name="text">The replacement source.</param>
    public void ReplaceText(string? text) => _selection.ReplaceText(text);
}
