using TextEdit.Core;

namespace RichEdit.Maui.Tests;

public sealed class SourceReadbackTests
{
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
