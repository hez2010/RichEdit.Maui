using RichEdit.Core;
using Microsoft.Maui;

namespace RichEdit.Maui.Tests;

public sealed class RichEditorPresentationTests
{
    [Fact]
    public Task NativeTableDecorationsPaintClearAndDoNotAuthorFormatting() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId paragraph = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            var table = edit.InsertTable(edit.Body, 1, 1, new RichLength[] { new(2400) });
            paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "table text");
        }));
        var editor = new RichEditor { Document = document };
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.NativeView.Handler = handler;
        try
        {
            using var layer = editor.Presentation.CreateDecorationLayer();
            var point = new RichTextPoint(document.CurrentSnapshot.Body, paragraph, 0);
            var before = document.CurrentSnapshot;
            Assert.True(layer.TryPublish(layer.BeginUpdate(), new RichEditorDecoration[]
            { new(new(point, point with { Offset = 5 }), new() { Italic = true }) }));
            var native = handler.PlatformView.Document;
            int Start() { var range = native.GetRange(0, 0); range.SetRange(0, range.StoryLength); return range.Text.IndexOf("table", StringComparison.Ordinal); }
            var index = Start();
            Assert.True(index >= 0);
            Assert.Equal(Microsoft.UI.Text.FormatEffect.On, native.GetRange(index, index + 1).CharacterFormat.Italic);
            Assert.Same(before, document.CurrentSnapshot);
            Assert.False(document.CanUndo);
            editor.Selection = new RichEdit.Core.RichTextSelection(point with { Offset = 5 }, point with { Offset = 5 });
            native.Selection.TypeText("X");
            await Task.Delay(50);
            Assert.Contains("tableX text", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.All(document.CurrentSnapshot.Nodes.Values.OfType<RichText>(), run => Assert.False(document.CurrentSnapshot.ResolveCharacter(run.Id).Italic));
            layer.Clear();
            index = Start();
            Assert.Equal(Microsoft.UI.Text.FormatEffect.Off, native.GetRange(index, index + 1).CharacterFormat.Italic);
            editor.Undo();
            Assert.Contains("table text", document.CurrentSnapshot.ProjectText().Text.ToString());
        }
        finally { editor.NativeView.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [Fact]
    public Task TypedPresentationFollowsStructuralMovesAndRejectsStaleRequests() => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("first");
        RichNodeId second = default;
        document.Edit(edit => second = edit.InsertParagraph(edit.Body, 1, "second"));
        document.ClearHistory();
        var editor = new RichEditor { Document = document };
        var peer = new RichEditor { Document = document };
        editor.Presentation.MarginWidth = 24;
        Assert.Equal(24, editor.NativeView.Adornments.MarginWidth);
        Assert.Equal(0, peer.NativeView.Adornments.MarginWidth);
        using var layer = editor.Presentation.CreateDecorationLayer(10);
        using var widgets = editor.Presentation.CreateWidgetSource(_ => new Label());
        RichTextPoint Point(int offset) => new(document.CurrentSnapshot.Body, second, offset);
        var captured = document.CurrentSnapshot;
        var old = layer.BeginUpdate();
        var latest = layer.BeginUpdate();
        Assert.False(layer.TryPublish(old, []));
        Assert.True(layer.TryPublish(latest, (RichEditorDecoration[])[new(new(Point(1), Point(4)), new() { Italic = true })]));
        Assert.True(widgets.TryPublish(widgets.BeginUpdate(), (RichEditorWidget[])[new("hint", Point(2), new(20, 16))]));
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
            layer.TryPublish(layer.BeginUpdate(), (RichEditorDecoration[])[new(new(start, start with { Offset = 4 }), new() { Italic = true })]);
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
