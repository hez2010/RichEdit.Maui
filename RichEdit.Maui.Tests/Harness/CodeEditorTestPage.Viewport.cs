#if DEBUG && (ANDROID || IOS || MACCATALYST)
using CodeEdit.Maui;

namespace RichEdit.Maui.Tests;

internal sealed partial class CodeEditorTestPage
{
    private async Task RunViewportTests(Func<string, string, Func<Task>, Task> test)
    {
        await test(
        "viewport source anchors and aligned reveal preserve selections",
        string.Join("\n", Enumerable.Range(0, 200).Select(i => $"line {i}: abcdefghijklmnopqrstuvwxyz")), async () =>
    {
        _editor.Selections = new((TextSelection[])[new(1, 3), new(7, 7)], 1);
        var selection = _editor.Selections;
        var token = new object();
        var observedToken = false;
        void Changed(object? sender, CodeViewportChangedEventArgs args) => observedToken |= ReferenceEquals(token, args.CauseToken);
        _editor.ViewportChanged += Changed;
        try
        {
            await Task.Delay(100);
            var position = _editor.GetLineRange(80).Start;
            Equal(true, await _editor.RevealAsync(position, CodeRevealAlignment.Center, token), "reveal completed");
            var layout = _editor.TextLayout.Capture()!;
            var caret = _editor.TextLayout.GetCaretBounds(layout, position)!.Value;
            Equal(true, Math.Abs(caret.Center.Y - layout.TextViewport.Center.Y) < 2, "centered source caret");
            Equal(selection, _editor.Selections, "selection after reveal");
            Equal(true, observedToken, "viewport cause token");
            using var anchor = _editor.CaptureScrollAnchor()!;
            var captured = anchor.Position!.Value;
            Equal(true, await _editor.RevealAsync(_editor.GetLineRange(150).Start, CodeRevealAlignment.End));
            Equal(true, _editor.TryApplyEdits(_editor.Document.Revision, (TextReplacement[])[new(new(0, 0), "prefix\n")]));
            Equal(captured + 7, anchor.Position, "tracked anchor after insertion");
            var afterEdit = _editor.Selections;
            Equal(true, await _editor.RestoreScrollAnchorAsync(anchor, token));
            var restored = _editor.CaptureViewport()!;
            Equal(anchor.Position, restored.Position, "restored source position");
            Equal(true, Math.Abs(anchor.Offset.Y - restored.Offset.Y) < 2, "restored offset");
            Equal(afterEdit, _editor.Selections, "selection after restoration");
        }
        finally { _editor.ViewportChanged -= Changed; }
        });

        await test("viewport rectangle selects disjoint bidi intervals", "ab אבג cd", async () =>
        {
            await Task.Delay(100);
            var layout = _editor.TextLayout.Capture()!;
            var a = _editor.TextLayout.GetRangeBounds(layout, new(0, 1)).Single();
            var bet = _editor.TextLayout.GetRangeBounds(layout, new(4, 1)).Single();
            Equal(true, _editor.SelectRectangle(new(a.Left, a.Center.Y), new(bet.Right - 0.1, a.Center.Y)));
            Equal(true, _editor.Selections.Select(s => s.Range).SequenceEqual(new[] { new TextSpan(0, 3), new TextSpan(4, 2) }),
                string.Join(", ", _editor.Selections.Select(s => s.Range)));
            Equal(true, _editor.ReplaceSelections("X"));
            Equal("XאX cd", _editor.Document.Text);
            _editor.Undo();
            Equal("ab אבג cd", _editor.Document.Text);
        });

        await test("viewport rectangle drag retains rows during native autoscroll",
            string.Join("\n", Enumerable.Repeat("0123456789", 200)), async () =>
        {
            await Task.Delay(100);
            var layout = _editor.TextLayout.Capture()!;
            var anchor = _editor.TextLayout.GetCaretBounds(layout, 3)!.Value.Center;
            var activeX = _editor.TextLayout.GetCaretBounds(layout, 6)!.Value.Left;
            var initiallyVisible = layout.Lines.Count;
            RichTextPointerEventArgs Pointer(Point point, RichTextPointerButtons buttons = RichTextPointerButtons.Primary) =>
                new(new(point.X - layout.Origin.X, point.Y - layout.Origin.Y), RichTextPointerDeviceKind.Mouse, buttons, EditorKeyModifiers.Alt);
            try
            {
                Equal(true, _textView.ObservePointer(Pointer(anchor), true));
                Equal(true, _textView.ObservePointer(Pointer(new(activeX, layout.TextViewport.Bottom + 24)), false));
                var deadline = Environment.TickCount64 + 2000;
                while (_editor.Selections.Count <= initiallyVisible + 2 && Environment.TickCount64 < deadline) await Task.Delay(20);
                Equal(true, _editor.Selections.Count > initiallyVisible + 2, $"{_editor.Selections.Count} selected rows, {initiallyVisible} initially visible");
                Equal(new TextSpan(3, 3), _editor.Selections[0].Range, "retained first row");
                Equal(true, _editor.Selections.All(s => s.Range.Length == 3), "native rectangle widths");
                Equal(true, _textView.ObservePointer(Pointer(new(activeX, layout.TextViewport.Bottom), RichTextPointerButtons.None), false));
                var selected = _editor.Selections;
                await Task.Delay(60);
                Equal(selected, _editor.Selections, "release stops drag");
                Equal(false, _editor.CanUndo);
            }
            finally { _textView.ObservePointer(Pointer(Point.Zero, RichTextPointerButtons.None), false); }
        });
    }
}
#endif
