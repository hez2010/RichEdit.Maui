using TextEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class SourceReadbackTests
{
    [Fact]
    public Task NativeMultipleCaretDeletionKeepsUnicodeGraphemeBoundaries() => WindowsTestHost.RunAsync(() =>
    {
        var source = new TextDocument("\u0600x\n\u0600x");
        var view = new NativeEditorView();
        view.BindSourceDocument(source);
        view.Selections = new((TextSelection[])[new(2, 2), new(5, 5)]);
        view.UpdateDocumentFromPlatform(RichTextDocumentSnapshot.CreateSourceProjection(source.CurrentSnapshot.Replace(new(0, 2), "")),
            0, 0, new object());
        Assert.Equal("\n", source.CurrentSnapshot.ToString());
        view.Undo();
        Assert.Equal("\u0600x\n\u0600x", source.CurrentSnapshot.ToString());

        var cluster = "a" + new string('\u0301', 80);
        source = new TextDocument(cluster + "X\n" + cluster + "X");
        view.BindSourceDocument(source);
        view.Selections = new((TextSelection[])[new(0, 0), new(cluster.Length + 2, cluster.Length + 2)]);
        view.UpdateDocumentFromPlatform(RichTextDocumentSnapshot.CreateSourceProjection(source.CurrentSnapshot.Replace(new(0, cluster.Length), "")),
            0, 0, new object());
        Assert.Equal("X\nX", source.CurrentSnapshot.ToString());
        view.Undo();
        Assert.Equal(cluster + "X\n" + cluster + "X", source.CurrentSnapshot.ToString());
    });

    [Fact]
    public Task MultipleCaretSourceInputDoesNotMaterializeTheDocument() => WindowsTestHost.RunAsync(() =>
    {
        var source = new TextDocument(new string('x', 10 * 1024 * 1024));
        var view = new NativeEditorView();
        view.BindSourceDocument(source);
        var before = source.CurrentSnapshot;
        view.Selections = new((TextSelection[])[new(4, 4), new(before.Length - 4, before.Length - 4)]);
        var observed = RichTextDocumentSnapshot.CreateSourceProjection(before.Replace(new(4, 0), "Y"));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        view.UpdateDocumentFromPlatform(observed, 5, 0, new object());
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(before.Length + 2, source.Length);
        Assert.Equal("Y", source.CurrentSnapshot.GetText(new(4, 1)));
        Assert.Equal("Y", source.CurrentSnapshot.GetText(new(before.Length - 3, 1)));
        Assert.Equal(2, view.Selections.Count);
        Assert.True(allocated < 512 * 1024, $"Two local source insertions allocated {allocated} bytes.");
        view.Undo();
        Assert.True(before.ContentEquals(source.CurrentSnapshot));
        Assert.False(source.CanUndo);
    });

    [Fact]
    public Task WidenedNativeEditPreservesUntouchedCarriageReturnsAndLineFeeds() => WindowsTestHost.RunAsync(() =>
    {
        var source = new TextDocument("a\r\nb\rc\n");
        var view = new NativeEditorView { SourceNewLine = "\n" };
        view.BindSourceDocument(source);
        var observed = RichTextDocumentSnapshot.CreateSourceProjection(TextSnapshot.FromText("a\nbXY\nc\n"));
        var restored = view.RestoreNativeSource(observed, new(5, 5), new(3, 2));
        Assert.Equal("a\r\nbXY\rc\n", restored.Snapshot.Text);
        Assert.Equal(6, restored.Selection.Active);
    });

    [Fact]
    public Task WidenedNativeSourceReadbackDoesNotMaterializeUnchangedContext() => WindowsTestHost.RunAsync(() =>
    {
        var source = new TextDocument(new string('x', 1024 * 1024) + "\r\nlast\rline\n");
        var view = new NativeEditorView();
        view.BindSourceDocument(source);
        var projection = view.Document.SourceLineEndings!;
        foreach (var position in new[] { 0, source.Length / 2, projection.Display.Length })
        {
            var observed = RichTextDocumentSnapshot.CreateSourceProjection(projection.Display.Replace(new(position, 0), "Y"));
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var restored = view.RestoreNativeSource(observed, new(position + 1, position + 1), new(0, source.Length));
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.True(allocated < 256 * 1024, $"Widened source readback at {position} allocated {allocated} bytes.");
            var canonical = projection.ToSource(position);
            Assert.Equal("Y", restored.Snapshot.GetText(new(canonical, 1)));
            Assert.Equal(canonical + 1, restored.Selection.Active);
            Assert.True(source.CurrentSnapshot.Replace(new(canonical, 0), "Y").ContentEquals(restored.Snapshot.TextBuffer));
        }
    });

    [Fact]
    public Task KnownNativeReplacementRestoresMixedEndingsWithBoundedAllocation() => WindowsTestHost.RunAsync(() =>
    {
        var text = string.Concat(Enumerable.Repeat(new string('x', 100) + "\r\n", 100_000));
        var source = new TextDocument(text);
        var view = new NativeEditorView { SourceNewLine = "\r\n" };
        view.BindSourceDocument(source);
        var projection = view.Document.SourceLineEndings!;
        var canonical = new TextSpan(source.Length / 2 + 4, 0);
        var display = projection.ToDisplay(canonical);
        var observed = RichTextDocumentSnapshot.CreateSourceProjection(projection.Display.Replace(display, "a\nb"));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var restored = view.RestoreNativeSource(observed, new(display.Start + 3, display.Start + 3), canonical);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < 512 * 1024, $"One bounded native replacement allocated {allocated} bytes.");
        Assert.Equal(source.Length + 4, restored.Snapshot.Length);
        Assert.Equal("a\r\nb", restored.Snapshot.GetText(new(canonical.Start, 4)));
        Assert.Equal(canonical.Start + 4, restored.Selection.Active);
        Assert.Equal("\r\n", restored.Snapshot.GetText(new(100, 2)));
        Assert.False(source.CanUndo);
        return Task.CompletedTask;
    });
}
