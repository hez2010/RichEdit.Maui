using CodeEdit.Maui;
using TextEdit.Core;

namespace RichEdit.Maui.Tests;

public class MultipleSelectionTests
{
    [Fact]
    public Task CodeCommandsEditAllSelectionsAndEachLineOnlyOnce() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("a b\nc d"), IndentSize = 2 };
        editor.Selections = new((TextSelection[])[new(0, 1), new(2, 3), new(4, 5)]);
        editor.Indent();
        Assert.Equal("  a b\n  c d", editor.Document.Text);
        editor.Undo();
        Assert.Equal("a b\nc d", editor.Document.Text);
        Assert.False(editor.CanUndo);
        editor.ToggleLineComment();
        Assert.Equal("// a b\n// c d", editor.Document.Text);
        editor.Undo();
        editor.Selections = new((TextSelection[])[new(1, 1), new(5, 5)]);
        editor.InsertNewLine();
        Assert.Equal("a\n b\nc\n d", editor.Document.Text);
        editor.Undo();
        Assert.Equal(2, editor.Selections.Count);
        Assert.Equal("a b\nc d", editor.Document.Text);
    });

    [Fact]
    public void NormalizationPreservesThePrimaryIdentityAndDirection()
    {
        var primary = new TextSelection(7, 3);
        var selections = new TextSelectionSet((TextSelection[])[new(1, 4), primary, new(10, 10), new(10, 10)], 1);
        Assert.Equal(2, selections.Count);
        Assert.Equal(primary.Id, selections.Primary.Id);
        Assert.Equal(7, selections.Primary.Anchor);
        Assert.Equal(1, selections.Primary.Active);
    }

    [Fact]
    public Task ReplacementAndUndoRestoreEverySelection() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("one two three") };
        var before = new TextSelectionSet((TextSelection[])[new(3, 0), new(4, 7), new(8, 13)], 1);
        editor.Selections = before;
        Assert.True(editor.ReplaceSelections("X"));
        Assert.Equal("X X X", editor.Document.Text);
        Assert.Equal(new[] { 1, 3, 5 }, editor.Selections.Select(item => item.Active));
        Assert.All(editor.Selections, item => Assert.Equal(item.Anchor, item.Active));
        Assert.Equal(before.Primary.Id, editor.Selections.Primary.Id);
        editor.Undo();
        Assert.Equal("one two three", editor.Document.Text);
        Assert.Equal(before.ToArray(), editor.Selections.ToArray());
        Assert.False(editor.CanUndo);
        editor.Redo();
        Assert.Equal("X X X", editor.Document.Text);
        Assert.Equal(3, editor.Selections.Count);
    });

    [Fact]
    public Task RejectedBatchChangesNeitherSelectionsNorHistory() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("abc"), MaxLength = 4 };
        var selections = new TextSelectionSet((TextSelection[])[new(0, 0), new(3, 3)]);
        editor.Selections = selections;
        Assert.False(editor.ReplaceSelections("x"));
        Assert.Equal("abc", editor.Document.Text);
        Assert.Same(selections, editor.Selections);
        Assert.False(editor.CanUndo);
        editor.MaxLength = -1;
        editor.IsReadOnly = true;
        Assert.False(editor.ReplaceSelections("x"));
        editor.IsReadOnly = false;
        Assert.True(editor.ReplaceSelections("x"));
        Assert.Equal("xabcx", editor.Document.Text);
        editor.Document = new("new");
        Assert.Single(editor.Selections);
        Assert.InRange(editor.Selections.Primary.Active, 0, 3);
    });

    [Fact]
    public Task OtherViewMapsSelectionsWithoutAdoptingTheOriginSet() => WindowsTestHost.RunAsync(() =>
    {
        var document = new CodeDocument("abcdef");
        var first = new CodeEditor { Document = document };
        var second = new CodeEditor { Document = document };
        first.Selections = new((TextSelection[])[new(1, 1), new(4, 4)]);
        second.Selections = new((TextSelection[])[new(2, 2), new(6, 6)]);
        var identity = second.Selections.Primary.Id;
        first.ReplaceSelections("X");
        Assert.Equal("aXbcdXef", document.Text);
        Assert.Equal(new[] { 3, 8 }, second.Selections.Select(item => item.Active));
        Assert.Equal(identity, second.Selections.Primary.Id);
        second.Undo();
        Assert.Equal(new[] { 1, 4 }, second.Selections.Select(item => item.Active));
    });
}
