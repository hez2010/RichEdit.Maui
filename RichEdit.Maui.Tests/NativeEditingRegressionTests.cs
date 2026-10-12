using Microsoft.Maui;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using RichEdit.Core;
using Window = Microsoft.UI.Xaml.Window;

namespace RichEdit.Maui.Tests;

public sealed class NativeEditingRegressionTests
{
    private static Window? _window;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReferenceDocumentTypingRetainsNativeContentAndViewport(bool listItem) => WindowsTestHost.RunAsync(async () =>
    {
        using var input = typeof(NativeEditingRegressionTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var editor = new NativeEditorView();
        editor.BindStructuredDocument(new RichDocument(imported.Snapshot));
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = handler;
        var window = _window ??= new Window();
        window.Content = handler.ContainerView ?? handler.PlatformView;
        window.AppWindow.Resize(new(700, 500));
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(200);
            var native = handler.PlatformView;
            var paragraph = editor.Document.Text.IndexOf("The text you are reading is aligned left", StringComparison.Ordinal);
            Assert.True(paragraph >= 0);
            var insertionPoint = paragraph + 12;
            if (listItem)
            {
                var snapshot = imported.Snapshot;
                var list = snapshot.Nodes.Values.OfType<RichList>().First();
                var item = snapshot.Get<RichListItem>(list.Items[0]);
                insertionPoint = snapshot.ProjectText().GetOffset(new(snapshot.Body, item.Blocks[0], 3));
            }
            editor.SelectedRange = new(insertionPoint, 0);
            native.Focus(FocusState.Programmatic);
            editor.ScrollIntoView(editor.SelectedRange);
            await Task.Delay(200);
            var scroll = WindowsScrollingTests.FindScroller(native)!;
            native.Document.Selection.GetRect(PointOptions.ClientCoordinates, out var caret, out _);
            scroll.ChangeView(null, Math.Max(0, caret.Y - 150), null, true);
            await Task.Delay(200);
            var beforeOffset = scroll.VerticalOffset;
            var events = 0;
            native.TextChanging += (_, args) => { if (args.IsContentChanging) events++; };
            var before = editor.Document.Text;
            var start = editor.SelectedRange.Start;
            var insertion = native.Document.Selection.StartPosition;
            var font = native.Document.GetRange(insertion - 1, insertion).CharacterFormat.GetClone();
            foreach (var character in "abc\rdef")
            {
                var count = events;
                if (character == '\r') Assert.False(editor.SendKeyDown(EditorKey.Enter, EditorKeyModifiers.None));
                native.Document.Selection.TypeText(character.ToString());
                await Task.Delay(60);
                Assert.Equal(count + 1, events);
            }
            Assert.Equal(before.Insert(start, "abc\ndef"), editor.Document.Text);
            var typed = native.Document.GetRange(insertion, insertion + 1).CharacterFormat;
            Assert.Equal(font.Size, typed.Size);
            Assert.Equal(font.Name, typed.Name);
            Assert.InRange(scroll.VerticalOffset, beforeOffset - 1, beforeOffset + 40);
        }
        finally { window.Content = null; editor.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task RepeatedTypingPreservesNativeRangesInBothSharedViews() => WindowsTestHost.RunAsync(async () =>
    {
        using var input = typeof(NativeEditingRegressionTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var document = new RichDocument(imported.Snapshot);
        var views = new[] { new NativeEditorView(), new NativeEditorView() };
        var handlers = views.Select(view =>
        {
            view.BindStructuredDocument(document);
            var handler = new RichEditorHandler();
            handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
            view.Handler = handler;
            return handler;
        }).ToArray();
        try
        {
            var tails = handlers.Select(handler =>
            {
                var story = handler.PlatformView.Document.GetRange(0, 0);
                return handler.PlatformView.Document.GetRange(story.StoryLength - 25, story.StoryLength - 5);
            }).ToArray();
            var expectedTails = tails.Select(range => range.Text).ToArray();
            var before = views[0].Document.Text;
            views[0].SelectedRange = new(20, 0);
            const string inserted = "native input without replacing either story";
            foreach (var character in inserted) handlers[0].PlatformView.Document.Selection.TypeText(character.ToString());
            await Task.Delay(100);
            Assert.Equal(before.Insert(20, inserted), views[0].Document.Text);
            for (var index = 0; index < tails.Length; index++) Assert.Equal(expectedTails[index], tails[index].Text);
            for (var index = 0; index < inserted.Length && views[0].Document.Text != before; index++) document.Undo();
            Assert.Equal(before, views[0].Document.Text);
        }
        finally
        {
            for (var index = 0; index < views.Length; index++) { views[index].Handler = null; ((IElementHandler)handlers[index]).DisconnectHandler(); }
        }
    });
}
