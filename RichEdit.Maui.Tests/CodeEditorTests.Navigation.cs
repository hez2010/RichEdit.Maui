using CodeEdit.Maui;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task NativeWordMovementExtendsEveryCaretAndPreservesSource() => WindowsTestHost.RunAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("alpha beta\r\ngamma delta");
        var editor = fixture.Editor;
        var window = _tabWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = fixture.Handler.ContainerView ?? fixture.Handler.PlatformView;
        window.Activate();
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
