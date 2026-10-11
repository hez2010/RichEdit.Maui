using RichEdit.Core;
using Microsoft.Maui;

namespace RichEdit.Maui.Tests;

public sealed class RichEditorPresentationTests
{
    [Fact]
    public Task TypedPresentationFollowsStructuralMovesAndRejectsStaleRequests() => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("first");
        RichNodeId second = default;
        document.Edit(edit => second = edit.InsertParagraph(edit.Body, 1, "second"));
        document.ClearHistory();
        var editor = new RichEditor { Document = document };
        var peer = new RichEditor { Document = document };
        using var layer = editor.Presentation.CreateDecorationLayer(10);
        using var widgets = editor.Presentation.CreateWidgetSource(_ => new Label());
        RichTextPoint Point(int offset) => new(document.CurrentSnapshot.Body, second, offset);
        var captured = document.CurrentSnapshot;
        var old = layer.BeginUpdate();
        var latest = layer.BeginUpdate();
        Assert.False(layer.TryPublish(old, []));
        Assert.True(layer.TryPublish(latest, [new(new(Point(1), Point(4)), new() { Italic = true })]));
        Assert.True(widgets.TryPublish(widgets.BeginUpdate(), [new("hint", Point(2), new(20, 16))]));
        Assert.Same(captured, document.CurrentSnapshot);
        Assert.False(document.CanUndo);
        Assert.False(peer.NativeView.Decorations.Project(peer.NativeView.Document.CurrentSnapshot).GetCharacterFormat(7).Italic);
        Assert.True(editor.NativeView.Decorations.Project(editor.NativeView.Document.CurrentSnapshot).GetCharacterFormat(7).Italic);
        var stale = widgets.BeginUpdate();
        document.Edit(edit => edit.MoveBlock(second, edit.Body, 0));
        await Task.Delay(40);
        Assert.False(widgets.TryPublish(stale, []));
        Assert.Equal(2, Assert.Single(editor.NativeView.Adornments).Position);
        Assert.True(editor.NativeView.Decorations.Project(editor.NativeView.Document.CurrentSnapshot).GetCharacterFormat(1).Italic);
        document.Edit(edit => edit.SplitParagraph(Point(2)));
        await Task.Delay(40);
        Assert.Equal(3, Assert.Single(editor.NativeView.Adornments).Position);
        document.Undo();
        await Task.Delay(40);
        Assert.Equal(2, Assert.Single(editor.NativeView.Adornments).Position);
        var replaced = layer.BeginUpdate();
        editor.Document = RichDocument.FromPlainText("replacement");
        Assert.False(layer.TryPublish(replaced, []));
        Assert.Empty(editor.NativeView.Adornments);
    });

    [Fact]
    public Task NativeRichDecorationDoesNotBecomeAuthoredFormatting() => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("abcd");
        var editor = new RichEditor { Document = document };
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.NativeView.Handler = handler;
        try
        {
            using var layer = editor.Presentation.CreateDecorationLayer();
            var start = new RichTextPoint(document.CurrentSnapshot.Body, new(2), 0);
            layer.TryPublish(layer.BeginUpdate(), [new(new(start, start with { Offset = 4 }), new() { Italic = true })]);
            var native = handler.PlatformView.Document.GetRange(1, 2);
            Assert.Equal(Microsoft.UI.Text.FormatEffect.On, native.CharacterFormat.Italic);
            editor.Selection = new RichEdit.Core.RichTextSelection(start with { Offset = 2 }, start with { Offset = 2 });
            handler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(40);
            Assert.Equal("abXcd", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>(), node => Assert.False(document.CurrentSnapshot.ResolveCharacter(node.Id).Italic));
            layer.Clear();
            Assert.Equal(Microsoft.UI.Text.FormatEffect.Off, handler.PlatformView.Document.GetRange(1, 2).CharacterFormat.Italic);
            editor.Undo();
            Assert.Equal("abcd", document.CurrentSnapshot.ProjectText().Text.ToString());
        }
        finally { editor.NativeView.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });
}
