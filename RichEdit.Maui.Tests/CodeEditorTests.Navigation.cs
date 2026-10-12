using CodeEdit.Maui;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task SingleVirtualCaretCreatesItsPresentationWithoutAnyWidgetsOrSecondaryCarets() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("x");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(80);
            editor.Selections = new((TextSelection[])[new(1, 1) { AnchorVirtualSpaces = 3, ActiveVirtualSpaces = 3 }]);
            await Task.Delay(40);
            Assert.Empty(fixture.Handler.SecondaryCaretBounds);
            Assert.Equal(Microsoft.UI.Text.CaretType.Normal, fixture.Handler.PlatformView.Document.CaretType);
            Assert.Equal(4, fixture.Handler.PlatformView.Document.Selection.StartPosition);
            Assert.Equal("x", editor.Document.Text);
            Assert.False(editor.CanUndo);
            editor.Selections = TextSelectionSet.Caret(1);
            await Task.Delay(30);
            Assert.Empty(fixture.Handler.SecondaryCaretBounds);
            Assert.Equal(Microsoft.UI.Text.CaretType.Normal, fixture.Handler.PlatformView.Document.CaretType);
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task VirtualClipboardAndLengthLimitDoNotMaterializeSourceUntilAccepted() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        var editor = new CodeEditor { Document = new("a") };
        editor.Selections = new((TextSelection[])[new(1, 1) { AnchorVirtualSpaces = 2, ActiveVirtualSpaces = 5 }]);
        Assert.True(editor.Commands.Copy.CanExecute(null));
        await editor.CopyAsync();
        Assert.Equal("a", editor.Document.Text);
        Assert.False(editor.CanUndo);
        editor.Document = new("x");
        editor.Selections = new((TextSelection[])[new(1, 1) { AnchorVirtualSpaces = 2, ActiveVirtualSpaces = 2 }]);
        editor.MaxLength = 5;
        await editor.PasteAsync();
        Assert.Equal("x", editor.Document.Text);
        Assert.False(editor.CanUndo);
        editor.MaxLength = 6;
        await editor.PasteAsync();
        Assert.Equal("x     ", editor.Document.Text);
        editor.Undo();
        Assert.Equal(2, editor.Selections.Primary.ActiveVirtualSpaces);
    });

    [Fact]
    public Task RectangularVirtualCaretsUseNativeRowsAndMaterializeOneUndoableEdit() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("abcd\r\nx\r\nabcdef");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            var layout = editor.TextLayout.Capture()!;
            var first = editor.TextLayout.GetCaretBounds(layout, 3)!.Value;
            var last = editor.TextLayout.GetCaretBounds(layout, 12)!.Value;
            Assert.True(editor.SelectRectangle(new(first.X, first.Center.Y), new(first.X, last.Center.Y), virtualSpace: true));
            Assert.Equal(3, editor.Selections.Count);
            Assert.Equal(new[] { 3, 7, 12 }, editor.Selections.Select(item => item.Active));
            Assert.Equal(2, editor.Selections[1].ActiveVirtualSpaces);
            Assert.False(editor.CanUndo);
            var before = editor.Selections;
            editor.Selections = new((TextSelection[])[editor.Selections[1]]);
            Assert.True(editor.TextView.SendKeyDown(EditorKey.Backspace, EditorKeyModifiers.None));
            Assert.Equal(1, editor.Selections.Primary.ActiveVirtualSpaces);
            Assert.Equal("abcd\r\nx\r\nabcdef", editor.Document.Text);
            Assert.False(editor.CanUndo);
            layout = editor.TextLayout.Capture()!;
            first = editor.TextLayout.GetCaretBounds(layout, 3)!.Value;
            last = editor.TextLayout.GetCaretBounds(layout, 12)!.Value;
            RichTextPointerEventArgs Pointer(Point point, RichTextPointerButtons buttons = RichTextPointerButtons.Primary,
                EditorKeyModifiers modifiers = EditorKeyModifiers.Alt) => new(new(point.X - layout.Origin.X, point.Y - layout.Origin.Y),
                    RichTextPointerDeviceKind.Mouse, buttons, modifiers);
            Assert.True(editor.TextView.ObservePointer(Pointer(first.Center), true));
            Assert.Equal(2, editor.Selections.Count);
            editor.AllowVirtualSpace = true;
            Assert.True(editor.TextView.ObservePointer(Pointer(new(first.X, last.Center.Y)), false));
            Assert.Equal(3, editor.Selections.Count);
            Assert.True(editor.TextView.ObservePointer(Pointer(last.Center, RichTextPointerButtons.None), false));
            Assert.False(editor.TextView.ObservePointer(Pointer(last.Center, modifiers: EditorKeyModifiers.None), true));
            Assert.Single(editor.Selections);
            Assert.Equal(0, editor.Selections.Primary.ActiveVirtualSpaces);
            // Native typing at a virtual primary is corrected together with all secondary edits.
            editor.Selections = new(before, 1);
            await Task.Delay(30);
            Assert.Equal(Microsoft.UI.Text.CaretType.Normal, fixture.Handler.PlatformView.Document.CaretType);
            fixture.Handler.PlatformView.Document.Selection.TypeText("!");
            Assert.Equal("abc!d\r\nx  !\r\nabc!def", editor.Document.Text);
            Assert.All(editor.Selections, item => Assert.Equal(0, item.ActiveVirtualSpaces));
            editor.Undo();
            Assert.Equal("abcd\r\nx\r\nabcdef", editor.Document.Text);
            Assert.Equal(2, editor.Selections[1].ActiveVirtualSpaces);
            Assert.False(editor.CanUndo);
        }
        finally { window.Content = null; }
    });

    [Fact]
    public Task NativeWordMovementExtendsEveryCaretAndPreservesSource() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("alpha beta\r\ngamma delta");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            editor.Selections = new((TextSelection[])[new(0, 0), new(12, 12)]);
            var identity = editor.Selections.Primary.Id;
            Assert.True(editor.MoveSelections(EditorKey.Right, extend: true, byWord: true));
            Assert.Equal(2, editor.Selections.Count);
            Assert.Equal(identity, editor.Selections.Primary.Id);
            Assert.Equal(new[] { 0, 12 }, editor.Selections.Select(item => item.Anchor));
            Assert.All(editor.Selections, item => Assert.InRange(item.Range.Length, 5, 6));
            Assert.False(editor.CanUndo);
            Assert.True(editor.ReplaceSelections("X"));
            editor.Undo();
            Assert.Equal("alpha beta\r\ngamma delta", editor.Document.Text);
            Assert.True(editor.MoveSelections(EditorKey.Left, byWord: true));
            Assert.All(editor.Selections, item => Assert.True(item.Range.IsEmpty));
        }
        finally { window.Content = null; }
    });
}
