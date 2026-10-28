#if (DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST)
namespace RichEdit.Maui.Tests;

internal sealed partial class CodeEditorTestPage
{
    private async Task RunTabTests(Func<string, string, Func<Task>, Task> test)
    {
        (int TabSize, int PrefixLength, int TabCount)[] cases =
        [
            (4, 3, 1), (4, 4, 1), (4, 5, 1),
            (4, 7, 1), (4, 8, 1), (4, 9, 1),
            (4, 4, 3), (4, 8, 3),
            (8, 7, 1), (8, 8, 1), (8, 9, 1), (8, 8, 3),
            (3, 3, 1), (3, 6, 3), (4, 0, 3),
        ];
        foreach (var wrap in new[] { false, true })
        foreach (var fontSize in new[] { 12d, 13.5, 14, 14.02 })
        {
            foreach (var (tabSize, prefixLength, tabCount) in cases)
            {
                var expectedColumn = (prefixLength / tabSize + tabCount) * tabSize;
                var tabbedLine = new string('x', prefixLength) + new string('\t', tabCount) + "=";
                var source = tabbedLine + "\n" + new string(' ', expectedColumn) + "=";
                await test($"layout tabs advance past boundaries (wrap={wrap}, font={fontSize}, size={tabSize}, prefix={prefixLength}, count={tabCount})", source, async () =>
                {
                    var snapshot = _editor.Document.CurrentSnapshot;
                    _editor.SelectedRange = new(1, 0);
                    _editor.FontSize = fontSize;
                    _editor.TabSize = tabSize;
                    _editor.WordWrap = wrap;
                    await Task.Delay(60);
                    var handler = (RichEditorHandler)_textView.Handler!;
                    double X(int position) => handler.GetTextCaretBounds(position, RichTextCaretAffinity.Downstream)?.Left
                        ?? throw new InvalidOperationException("Missing native tab caret.");
                    var actual = X(tabbedLine.Length - 1);
                    var expected = X(tabbedLine.Length + 1 + expectedColumn);
                    Equal(true, Math.Abs(actual - expected) <= 1.000001,
                        $"expected column {expectedColumn} at X={expected}, actual X={actual}");
                    Equal(true, ReferenceEquals(snapshot, _editor.Document.CurrentSnapshot));
                    Equal(source, _editor.Document.Text);
                    Equal(new TextSpan(1, 0), _editor.SelectedRange);
                    Equal(false, _editor.Document.IsModified);
                    Equal(false, _editor.CanUndo);
                });
            }

            string[] fields = ["1", "22", "333", "4444", "55555", "666666", "7777777", "88888888", "999999999"];
            var lines = fields.Select(field => "\t" + field + new string('\t', (19 - (4 + field.Length)) / 4 + 1) + "= value").ToArray();
            var fieldSource = string.Join("\n", lines) + "\n" + new string(' ', 20) + "= value";
            await test($"layout tabs align field separators (wrap={wrap}, font={fontSize})", fieldSource, async () =>
            {
                var snapshot = _editor.Document.CurrentSnapshot;
                _editor.FontSize = fontSize;
                _editor.WordWrap = wrap;
                await Task.Delay(60);
                var handler = (RichEditorHandler)_textView.Handler!;
                double X(int position) => handler.GetTextCaretBounds(position, RichTextCaretAffinity.Downstream)?.Left
                    ?? throw new InvalidOperationException("Missing native field caret.");
                var expected = X(fieldSource.LastIndexOf('='));
                var start = 0;
                foreach (var line in lines)
                {
                    var actual = X(start + line.IndexOf('='));
                    Equal(true, Math.Abs(actual - expected) <= 1.000001, $"{line}: expected X={expected}, actual X={actual}");
                    start += line.Length + 1;
                }
                Equal(true, ReferenceEquals(snapshot, _editor.Document.CurrentSnapshot));
                Equal(fieldSource, _editor.Document.Text);
                Equal(false, _editor.CanUndo);
                Equal(false, _editor.Document.IsModified);
            });
        }

#if ANDROID
        foreach (var wrap in new[] { false, true })
        foreach (var fontSize in new[] { 13.5, 14.02 })
        {
            string[] fields = ["1", "22", "333", "4444", "55555", "666666", "7777777", "88888888", "999999999"];
            var lines = fields.Select(field => "\t" + field + new string('\t', (19 - (4 + field.Length)) / 4 + 1) + "= value").ToArray();
            var source = string.Join("\n", lines) + "\n" + new string(' ', 20) + "= value";
            await test($"layout tabs honor fractional native advances (wrap={wrap}, font={fontSize})", source, async () =>
            {
                var handler = (RichEditorHandler)_textView.Handler!;
                var native = handler.PlatformView;
                var flags = native.PaintFlags;
                var snapshot = _editor.Document.CurrentSnapshot;
                try
                {
                    native.PaintFlags |= Android.Graphics.PaintFlags.SubpixelText | Android.Graphics.PaintFlags.LinearText;
                    _editor.FontSize = fontSize;
                    _editor.WordWrap = wrap;
                    await Task.Delay(60);
                    var advance = native.Paint!.GetRunAdvance(" ", 0, 1, 0, 1, false, 1);
                    Equal(true, Math.Abs(advance - Math.Round(advance)) > 0.001,
                        $"fixture has a fractional space advance: {advance}, measured={native.Paint.MeasureText(" ")}, flags={native.PaintFlags}");
                    var layout = native.Layout ?? throw new InvalidOperationException("Missing native tab layout.");
                    var expected = layout.GetPrimaryHorizontal(source.LastIndexOf('='));
                    var aligned = layout.GetPrimaryHorizontal(lines[0].IndexOf('='));
                    // Android tab stops have integer pixel positions. At column
                    // 20, five tab intervals may each lose less than one pixel.
                    Equal(true, Math.Abs(aligned - expected) < 5,
                        $"expected column 20 near X={expected}, actual X={aligned}");
                    var start = 0;
                    foreach (var line in lines)
                    {
                        var actual = layout.GetPrimaryHorizontal(start + line.IndexOf('='));
                        Equal(true, Math.Abs(actual - aligned) < 1, $"{line}: expected X={aligned}, actual X={actual}");
                        start += line.Length + 1;
                    }
                    Equal(true, ReferenceEquals(snapshot, _editor.Document.CurrentSnapshot));
                    Equal(source, _editor.Document.Text);
                    Equal(false, _editor.CanUndo);
                    Equal(false, _editor.Document.IsModified);
                }
                finally
                {
                    native.PaintFlags = flags;
                }
            });
        }
#endif
    }
}
#endif
