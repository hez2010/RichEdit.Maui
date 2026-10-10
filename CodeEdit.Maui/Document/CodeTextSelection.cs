using System.ComponentModel;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A stable source-only selection facade.</summary>
public sealed partial class CodeTextSelection : INotifyPropertyChanged
{
    private readonly CodeEditor _editor;

    internal CodeTextSelection(CodeEditor editor)
    {
        _editor = editor;
        editor.ContentChanged += (_, _) => PropertyChanged?.Invoke(this, new(nameof(Text)));
        editor.SelectionChanged += (_, _) =>
        {
            foreach (var property in new[] { nameof(Range), nameof(State), nameof(Anchor), nameof(Active), nameof(Text) })
                PropertyChanged?.Invoke(this, new(property));
        };
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the normalized UTF-16 range.</summary>
    public TextSpan Range => _editor.SelectedRange;

    /// <summary>Gets the anchor and active endpoints.</summary>
    public TextSelectionState State => _editor.SelectionState;

    /// <summary>Gets the fixed endpoint.</summary>
    public int Anchor => State.Anchor;

    /// <summary>Gets the caret endpoint.</summary>
    public int Active => State.Active;

    /// <summary>Gets the selected source.</summary>
    public string Text => _editor.Document.GetText(Range);

    /// <summary>Selects a range with the caret at its end.</summary>
    /// <param name="range">The range to select.</param>
    public void Select(TextSpan range) => _editor.SelectedRange = range;

    /// <summary>Sets a directional selection.</summary>
    /// <param name="selection">The anchor and active offsets.</param>
    public void Select(TextSelectionState selection) => _editor.SelectionState = selection;

    /// <summary>Replaces the selection with source text and advances the caret.</summary>
    /// <param name="text">The replacement source.</param>
    public void ReplaceText(string? text) => _editor.ReplaceSelections(text ?? "");
}
