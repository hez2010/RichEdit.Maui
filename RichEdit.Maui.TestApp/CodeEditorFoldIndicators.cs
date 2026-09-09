using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

// Fold discovery, button appearance, grouping, and expansion are sample policy.
internal sealed class CodeEditorFoldIndicators : IDisposable
{
    private readonly CodeEditor _editor;
    private readonly List<RichTextAdornment> _indicators = [];

    internal CodeEditorFoldIndicators(CodeEditor editor)
    {
        _editor = editor;
        editor.Folding.Changed += OnChanged;
        editor.TextChanged += OnChanged;
        Refresh();
    }

    public void Dispose()
    {
        _editor.Folding.Changed -= OnChanged;
        _editor.TextChanged -= OnChanged;
        foreach (var indicator in _indicators) indicator.Dispose();
        _indicators.Clear();
        _editor.Adornments.MarginWidth = 0;
    }

    private void OnChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        foreach (var indicator in _indicators) indicator.Dispose();
        _indicators.Clear();
        var rows = new List<(int Position, List<RichTextRange> Ranges)>();
        var coveredEnd = -1;
        foreach (var range in _editor.Folding.CollapsedRanges)
        {
            if (range.Start < coveredEnd) continue;
            var hidden = _editor.Folding.GetCollapsedRange(range.Start)!.Value;
            // Line folds leave an empty tail after the visible header, where an overlay cannot cover code.
            if (IsLineEnd(range.Start) && IsLineEnd(hidden.End))
                _indicators.Add(_editor.Adornments.Add(hidden.End, CreateExpandButton(range, "…"), offset: new Point(6, 0)));
            // With the sample's unwrapped code, intervals separated by no visible newline share a row.
            if (rows.Count > 0 && !_editor.Document.Text.AsSpan(coveredEnd, range.Start - coveredEnd).Contains('\n'))
            {
                rows[^1].Ranges.Add(range);
                rows[^1] = (hidden.End, rows[^1].Ranges);
            }
            else rows.Add((hidden.End, [range]));
            coveredEnd = hidden.End;
        }

        _editor.Adornments.MarginWidth = rows.Count == 0 ? 0 : rows.Max(row => row.Ranges.Count) * 26 + 4;
        foreach (var row in rows)
        {
            var buttons = new HorizontalStackLayout { Spacing = 2 };
            foreach (var range in row.Ranges)
                buttons.Add(CreateExpandButton(range, "▸"));
            _indicators.Add(_editor.Adornments.Add(row.Position, buttons, RichTextAdornmentPlacement.LeftMargin));
        }
    }

    private bool IsLineEnd(int position) => _editor.GetLineRange(_editor.GetPosition(position).Line).End == position;

    private Button CreateExpandButton(RichTextRange range, string text)
    {
        var button = new Button
        {
            Text = text, FontSize = 13, Padding = 0,
            WidthRequest = 24, HeightRequest = 20, MinimumWidthRequest = 0, MinimumHeightRequest = 0,
            BackgroundColor = Color.FromArgb("#E8EEF8"), TextColor = Color.FromArgb("#244A80"), CornerRadius = 3,
            Command = new Command(() => _editor.Folding.Expand(range)),
        };
        var description = $"Expand folded range at line {_editor.GetPosition(range.Start).Line} ({range.Length} characters)";
        SemanticProperties.SetDescription(button, description);
        ToolTipProperties.SetText(button, description);
        return button;
    }
}
