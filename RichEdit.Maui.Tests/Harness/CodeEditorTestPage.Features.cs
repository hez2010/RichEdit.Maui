#if DEBUG && (ANDROID || IOS || MACCATALYST)
using CodeEdit.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

internal sealed partial class CodeEditorTestPage
{
    private Task RunFeatureTests(Func<string, string, Func<Task>, Task> test) => test("features completion and tracked placeholders use public APIs", "Con", async () =>
    {
        using var features = new CodeEditorFeatures(_editor, (Grid)_editor.Parent);
        _editor.SelectedRange = new(3, 0);
        await features.ShowCompletionsAsync();
        Equal(true, features.AcceptCompletion(0));
        Equal("Console", _editor.Document.Text);
        _editor.Undo();
        Equal("Con", _editor.Document.Text);
        _editor.Document = new CodeDocument();
        features.InsertSnippet();
        Equal("var name = value;", _editor.Document.Text);
        Equal("name", _editor.Selection.Text);
        Equal(true, _editor.TryApplyEdits(_editor.Document.Revision, (RichTextEdit[])[new(_editor.SelectedRange, "answer")]));
        Equal(true, features.NextPlaceholder());
        Equal("value", _editor.Selection.Text);
        Equal(true, features.NextPlaceholder(previous: true));
        Equal("answer", _editor.Selection.Text);
        _editor.Document = new CodeDocument("replacement");
        Equal(false, features.NextPlaceholder());
        await Task.Delay(200);
    });
}
#endif
