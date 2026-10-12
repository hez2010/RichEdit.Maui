using CodeEdit.Maui;

namespace RichEdit.Maui.TestApp;

public sealed partial class CodeEditorPage
{
    private void OnAddCaretBelow(object? sender, EventArgs args) { Editor.AddCaretBelow(); Editor.Focus(); }
    private void OnSingleCaret(object? sender, EventArgs args) { Editor.ClearSecondarySelections(); Editor.Focus(); }

    private void OnVirtualCarets(object? sender, EventArgs args)
    {
        if (!Editor.TryCompleteComposition()) return;
        var text = Editor.Document.CurrentSnapshot.TextBuffer;
        Editor.AllowVirtualSpace = true;
        Editor.Selections = new(Editor.Selections.Select(selection =>
        {
            var end = text.GetLine(text.GetPosition(selection.Active).Line).Span.End;
            return selection with { Anchor = end, Active = end, AnchorVirtualSpaces = Editor.TabSize, ActiveVirtualSpaces = Editor.TabSize };
        }), Editor.Selections.PrimaryIndex);
        Editor.Focus();
    }


    private void OnSelectOccurrences(object? sender, EventArgs args)
    {
        var query = Editor.Document.GetText(Editor.SelectedRange);
        if (query.Length == 0) query = QueryEntry.Text ?? "";
        var matches = _actions.FindAll(query, new(MatchCase: true));
        if (matches.Count == 0) { SearchPanel.IsVisible = true; SearchStatusLabel.Text = "Select a word or enter a search term first."; return; }
        Editor.Selections = new(matches.Select(range => new TextSelection(range.Start, range.End)));
        Editor.Focus();
    }

    private async void OnGoToLine(object? sender, EventArgs args)
    {
        var value = await DisplayPromptAsync("Go to line", $"Line number (1–{Editor.LineCount})", "Go", "Cancel", keyboard: Keyboard.Numeric);
        if (!int.TryParse(value, out var line) || line < 1) return;
        _actions.GoToLine(line);
        Editor.Focus();
    }

    private async void OnLineEndings(object? sender, EventArgs args)
    {
        var choice = await DisplayActionSheetAsync("Line endings", "Cancel", null, "Insert LF", "Insert CRLF", "Insert CR", "Convert document to LF", "Convert document to CRLF");
        if (choice is null or "Cancel") return;
        var ending = choice.EndsWith("CRLF", StringComparison.Ordinal) ? "\r\n" : choice.EndsWith("CR", StringComparison.Ordinal) ? "\r" : "\n";
        if (choice.StartsWith("Convert", StringComparison.Ordinal))
        {
            if (Editor.IsReadOnly) { SearchPanel.IsVisible = true; SearchStatusLabel.Text = "Turn off Read only before converting line endings."; return; }
            if (!Editor.TryCompleteComposition()) { SearchPanel.IsVisible = true; SearchStatusLabel.Text = "Finish the current input before converting line endings."; return; }
            Editor.Document.Edit(edit => edit.NormalizeLineEndings(ending), new(Description: choice));
        }
        Editor.NewLine = ending;
        UpdateStatus();
    }

    private void OnTabWidthChanged(object? sender, EventArgs args)
    {
        if (Editor is null || !int.TryParse(TabWidthPicker.SelectedItem as string, out var width)) return;
        Editor.TabSize = width;
    }

    private void OnSplitViewChanged(object? sender, CheckedChangedEventArgs args)
    {
        if (CodePeerPane is null) return;
        CodePeerPane.IsVisible = args.Value;
        SplitPaneLayout.Update(CodeColumns, CodePeerPane);
        UpdatePeerColorizer();
    }

    private void UpdatePeerColorizer()
    {
        if (CodePeerPane.IsVisible && _colorizer is not null) _peerColorizer ??= new(CodePeer);
        else { _peerColorizer?.Dispose(); _peerColorizer = null; }
        ApplyPalette();
    }
}
