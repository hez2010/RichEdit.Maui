using CodeEdit.Maui;
using Microsoft.UI.Text;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact]
    public Task CheckedSourceEditsRejectResultsAfterSelectionMoves() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("name"), SelectedRange = new(4, 0) };
        var revision = editor.Document.Revision;
        var generation = editor.SelectionGeneration;
        editor.SelectedRange = new(0, 0);
        Assert.False(editor.TryApplyEdits(revision, (TextReplacement[])[new(new(0, 4), "stale")], expectedSelectionGeneration: generation));
        Assert.Equal("name", editor.Document.Text);
        Assert.True(editor.TryApplyEdits(revision, (TextReplacement[])[new(new(0, 4), "fresh")], expectedSelectionGeneration: editor.SelectionGeneration));
        editor.Undo();
        Assert.Equal("name", editor.Document.Text);
    });

    [Fact]
    public Task UnchangedNativeReadbackPreservesSelectedMixedEndings() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new CodeEditorFixture("a\r\nb\rc\n");
        var editor = fixture.Editor;
        editor.SelectAll();
        var source = editor.TextView.RestoreNativeSource(RichTextDocumentSnapshot.FromPlainText("a\nb\nc\n"), new(0, 6));
        Assert.Same(editor.TextView.Document.CurrentSnapshot, source.Snapshot);
        Assert.Equal(new TextSelectionState(0, 7), source.Selection);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task NativeSourceLimitCountsExactCrLfCodeUnits() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new CodeEditorFixture("a\r\nb");
        var editor = fixture.Editor;
        editor.MaxLength = 4;
        editor.SelectedRange = new(4, 0);
        fixture.Handler.PlatformView.Document.Selection.TypeText("x");
        Assert.Equal("a\r\nb", editor.Document.Text);
        Assert.False(editor.CanUndo);
        Assert.Equal(new TextSpan(4, 0), editor.SelectedRange);
    });

    [Fact]
    public Task SourceNoOpReplacementUpdatesSelectionWithoutCreatingHistory() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("same\r\n"), SelectedRange = new(0, 4) };
        editor.Selection.ReplaceText("same");
        Assert.Equal(new TextSpan(4, 0), editor.SelectedRange);
        Assert.False(editor.CanUndo);
        Assert.True(editor.TryApplyEdits(editor.Document.Revision, [], new(6, 6)));
        Assert.Equal(new TextSpan(6, 0), editor.SelectedRange);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public Task MixedEndingsUseCanonicalOffsetsThroughNativeTypingAndUndo() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new CodeEditorFixture("a\r\nb\rc\n");
        var editor = fixture.Editor;
        var source = editor.Document;
        editor.SelectedRange = new(4, 0);
        Assert.Equal(3, fixture.Handler.PlatformView.Document.Selection.StartPosition);
        using var tracked = source.Tracking.TrackPosition(5);
        var observations = new List<string>();
        source.Changed += (_, change) =>
        {
            Assert.Same(change.After, editor.TextView.Document.CurrentSnapshot.TextBuffer);
            Assert.Equal(source.Revision, editor.TextView.Document.Revision);
            Assert.Equal(new TextSpan(6, 0), editor.SelectedRange);
            observations.Add(change.After.ToString());
        };
        fixture.Handler.PlatformView.Document.Selection.TypeText("XY");
        Assert.Equal(new[] { "a\r\nbXY\rc\n" }, observations);
        Assert.Equal(7, tracked.Position);
    });

    [Fact]
    public Task SourceNewlinePolicyAndIndentationCommitTogetherAcrossCarets() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new CodeEditorFixture("  a\r\n    b");
        var editor = fixture.Editor;
        editor.NewLine = "\r\n";
        editor.Selections = new((TextSelection[])[new(3, 3), new(10, 10)]);
        var changes = 0;
        editor.ContentChanged += (_, _) => changes++;
        fixture.Handler.PlatformView.Document.Selection.TypeText("\r");
        Assert.Equal("  a\r\n  \r\n    b\r\n    ", editor.Document.Text);
        Assert.Equal(1, changes);
        Assert.Equal(new[] { 7, 20 }, editor.Selections.Select(selection => selection.Active));
        editor.Undo();
        Assert.Equal("  a\r\n    b", editor.Document.Text);
        Assert.False(editor.CanUndo);
        Assert.Equal(new[] { 3, 10 }, editor.Selections.Select(selection => selection.Active));
    });

    [Fact]
    public Task ExactSourceEditsCanSplitCrLfAndKeepTheNativeMapCoherent() => WindowsTestHost.RunAsync(() =>
    {
        using var fixture = new CodeEditorFixture("a\r\nb");
        var editor = fixture.Editor;
        editor.Document.Edit(edit => edit.InsertText(2, "x"));
        Assert.Equal("a\rx\nb", editor.Document.Text);
        Assert.Equal(3, editor.LineCount);
        editor.SelectedRange = new(4, 1);
        fixture.Handler.PlatformView.Document.Selection.GetText(TextGetOptions.None, out var selected);
        Assert.Equal("b", selected);
        editor.Selection.ReplaceText("B\r\nC");
        Assert.Equal("a\rx\nB\r\nC", editor.Document.Text);
        editor.Undo();
        editor.Undo();
        Assert.Equal("a\r\nb", editor.Document.Text);
    });

    [Fact]
    public Task SourceClipboardPreservesMixedEndingsAndDistributesSelections() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var fixture = new CodeEditorFixture("a\r\nb\rc\n");
        var editor = fixture.Editor;
        editor.Selections = new((TextSelection[])[new(0, 3), new(3, 7)]);
        // Touching selections normalize to one selection; use distinct ranges for distribution.
        editor.Selections = new((TextSelection[])[new(0, 3), new(4, 7)]);
        await editor.CopyAsync();
        editor.Document = new("XY");
        editor.Selections = new((TextSelection[])[new(0, 0), new(2, 2)]);
        await editor.PasteAsync();
        Assert.Equal("a\r\nXY\rc\n", editor.Document.Text);
        editor.Undo();
        Assert.Equal("XY", editor.Document.Text);
    });

    [Fact]
    public Task SourceCompositionHoldsTheCanonicalLeaseAndCommitsOnce() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new("a\r\nb") };
        var view = editor.TextView;
        editor.SelectedRange = new(4, 0);
        view.SetCompositionState(new(true, new(4, 1)));
        view.UpdateDocumentFromPlatform(RichTextDocumentSnapshot.CreateSourceProjection(TextSnapshot.FromText("a\r\nbx")),
            5, 0, new object());
        Assert.Equal("a\r\nb", editor.Document.Text);
        Assert.Throws<InvalidOperationException>(() => editor.Document.Edit(edit => edit.InsertText(0, "other")));
        Assert.Throws<InvalidOperationException>(() => editor.Document.CaptureSave());
        view.SetCompositionState(default);
        Assert.Equal("a\r\nbx", editor.Document.Text);
        editor.Undo();
        Assert.Equal("a\r\nb", editor.Document.Text);
    });
}
