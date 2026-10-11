using CodeEdit.Maui;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task RectangleSelectsDisjointBidiIntervalsFromNativeGlyphs() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("ab אבג cd");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.Activate();
        try
        {
            await Task.Delay(100);
            var layout = editor.TextLayout.Capture()!;
            var a = editor.TextLayout.GetRangeBounds(layout, new(0, 1)).Single();
            var bet = editor.TextLayout.GetRangeBounds(layout, new(4, 1)).Single();
            Assert.True(editor.SelectRectangle(new(a.Left, a.Center.Y), new(bet.Right - 0.1, a.Center.Y)));
            Assert.Equal(new[] { new TextSpan(0, 3), new TextSpan(4, 2) }, editor.Selections.Select(s => s.Range));
            Assert.True(editor.ReplaceSelections("X"));
            Assert.Equal("XאX cd", editor.Document.Text);
            editor.Undo();
            Assert.Equal("ab אבג cd", editor.Document.Text);
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task RectangleDragRetainsEarlierRowsDuringNativeAutoscroll() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Join("\r\n", Enumerable.Repeat("0123456789", 200)));
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.Activate();
        try
        {
            await Task.Delay(100);
            var layout = editor.TextLayout.Capture()!;
            var anchor = editor.TextLayout.GetCaretBounds(layout, 3)!.Value.Center;
            var activeX = editor.TextLayout.GetCaretBounds(layout, 6)!.Value.Left;
            var initiallyVisible = layout.Lines.Count;
            RichTextPointerEventArgs Pointer(Point point, RichTextPointerButtons buttons = RichTextPointerButtons.Primary) =>
                new(new(point.X - layout.Origin.X, point.Y - layout.Origin.Y), RichTextPointerDeviceKind.Mouse, buttons, EditorKeyModifiers.Alt);
            Assert.True(editor.TextView.ObservePointer(Pointer(anchor), true));
            Assert.True(editor.TextView.ObservePointer(Pointer(new(activeX, layout.TextViewport.Bottom + 24)), false));
            var deadline = Environment.TickCount64 + 2000;
            while (editor.Selections.Count <= initiallyVisible + 2 && Environment.TickCount64 < deadline) await Task.Delay(20);
            Assert.True(editor.Selections.Count > initiallyVisible + 2, $"Only {editor.Selections.Count} rows; initial viewport {initiallyVisible}.");
            Assert.Equal(new TextSpan(3, 3), editor.Selections[0].Range);
            Assert.All(editor.Selections, s => Assert.Equal(3, s.Range.Length));
            Assert.True(editor.TextView.ObservePointer(Pointer(new(activeX, layout.TextViewport.Bottom), RichTextPointerButtons.None), false));
            var selected = editor.Selections;
            await Task.Delay(60);
            Assert.Equal(selected, editor.Selections);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            editor.TextView.ObservePointer(new(Point.Zero, RichTextPointerDeviceKind.Mouse, RichTextPointerButtons.None, EditorKeyModifiers.None), false);
            window.Content = null;
        }
    });

    [Fact]
    public Task SourceAnchorsRestoreAfterEditsAndAlignedRevealPreservesSelections() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture(string.Join("\r\n", Enumerable.Range(0, 200).Select(i => $"line {i}: abcdefghijklmnopqrstuvwxyz")));
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.Activate();
        try
        {
            await Task.Delay(100);
            editor.Selections = new((TextSelection[])[new(1, 3), new(7, 7)], 1);
            var selections = editor.Selections;
            var token = new object();
            var events = new List<CodeViewportChangedEventArgs>();
            editor.ViewportChanged += (_, args) => events.Add(args);
            var position = editor.GetLineRange(80).Start;
            Assert.True(await editor.RevealAsync(position, CodeRevealAlignment.Center, token));
            var layout = editor.TextLayout.Capture()!;
            var caret = editor.TextLayout.GetCaretBounds(layout, position)!.Value;
            Assert.InRange(Math.Abs(caret.Center.Y - layout.TextViewport.Center.Y), 0, 2);
            Assert.Equal(selections, editor.Selections);
            Assert.Contains(events, e => ReferenceEquals(e.CauseToken, token));
            using var anchor = editor.CaptureScrollAnchor()!;
            var captured = anchor.Position!.Value;
            Assert.True(await editor.RevealAsync(editor.GetLineRange(150).Start, CodeRevealAlignment.End));
            Assert.True(editor.TryApplyEdits(editor.Document.Revision, (TextReplacement[])[new(new(0, 0), "prefix\r\n")]));
            Assert.Equal(captured + 8, anchor.Position);
            var afterEdit = editor.Selections;
            Assert.True(await editor.RestoreScrollAnchorAsync(anchor, token));
            var restored = editor.CaptureViewport()!;
            Assert.Equal(anchor.Position, restored.Position);
            Assert.InRange(Math.Abs(anchor.Offset.Y - restored.Offset.Y), 0, 2);
            Assert.Equal(afterEdit, editor.Selections);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => editor.RevealAsync(0, cancellationToken: cancellation.Token));
            anchor.Dispose();
            Assert.False(await editor.RestoreScrollAnchorAsync(anchor));
        }
        finally { window.Content = null; }
    });
}
