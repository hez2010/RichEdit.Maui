using RichEdit.Core;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;

namespace RichEdit.Maui.Tests;

public sealed partial class RichEditorPresentationTests
{
    private static Microsoft.UI.Xaml.Window? _window;
    private static MauiApp? _app;
    private sealed partial class PresentationApplication : Application;

    [Fact]
    public Task BlockWidgetsReflowWithinTheirTableCellAfterColumnResize() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId table = default, paragraph = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 1, new RichLength[] { new(2400), new(2400) });
            paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "cell text");
            edit.SetCellFormat(new(table, 0, 0, 1, 1), new() { Padding = RichCellPadding.Uniform(new(120)) });
        }));
        var rich = new RichEditor { Document = document, FontSize = 16 };
        var app = _app ??= MauiApp.CreateBuilder().UseMauiApp<PresentationApplication>().UseRichEdit().Build();
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        window.Content = rich.ToPlatform(new MauiContext(app.Services));
        window.Activate();
        Label? label = null;
        try
        {
            await Task.Delay(100);
            var point = new RichTextPoint(document.CurrentSnapshot.Body, paragraph, 0);
            rich.Selection = new RichEdit.Core.RichTextSelection(point, point);
            using var widgets = rich.Presentation.CreateWidgetSource(_ => label = new Label
            {
                Text = "A table annotation wraps within its cell and follows column resizing.", FontSize = 16,
            });
            Assert.True(widgets.TryPublish(widgets.BeginUpdate(), new RichEditorWidget[]
            { new("cell-note", point, new(140, 30), Options: new() { Placement = RichTextAdornmentPlacement.AboveLine }) }));
            await WaitFor(() => label is { Bounds.Width: > 0, Bounds.Height: > 30 } && label.Bounds.Width <= 144);
            var narrowHeight = label!.Bounds.Height;
            Assert.False(document.CanUndo);
            Assert.Empty(document.CurrentSnapshot.Assets);
            document.Edit(edit => edit.SetColumnWidths(table, new RichLength[] { new(4800), new(2400) }));
            await WaitFor(() => label.Bounds.Width > 200 && label.Bounds.Height < narrowHeight);
            Assert.InRange(label.Bounds.Width, 200, 304);
            Assert.Equal(new RichEdit.Core.RichTextSelection(point, point), rich.Selection);
            rich.Undo();
            await WaitFor(() => label.Bounds.Width <= 144 && label.Bounds.Height >= narrowHeight - 1);
            Assert.Empty(document.CurrentSnapshot.Assets);
            Assert.Equal("cell text\t", document.CurrentSnapshot.ProjectText(table).Text.ToString());
        }
        finally { window.Content = null; rich.NativeView.Handler?.DisconnectHandler(); rich.Handler?.DisconnectHandler(); }

        async Task WaitFor(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 3000;
            while (!ready() && Environment.TickCount64 < deadline) await Task.Delay(20);
            Assert.True(ready(), $"Widget bounds {label?.Bounds}; " + string.Join("; ", rich.NativeView.Adornments.Select(item => $"size {item.MeasuredSize}, constraint {item.MeasuredWidthConstraint}, realized {item.IsRealized}")));
        }
    });

    [Fact]
    public Task InlineWidgetReservesNativeTableLayoutWithoutChangingTheRichDocument() => WindowsTestHost.RunAsync(async () =>
    {
        RichNodeId paragraph = default;
        var document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            var table = edit.InsertTable(edit.Body, 1, 1, new RichLength[] { new(3600) });
            paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "abcdefgh");
        }));
        var rich = new RichEditor { Document = document, FontSize = 18 };
        var app = _app ??= MauiApp.CreateBuilder().UseMauiApp<PresentationApplication>().UseRichEdit().Build();
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        window.Content = rich.ToPlatform(new MauiContext(app.Services));
        window.Activate();
        try
        {
            await Task.Delay(80);
            var point = new RichTextPoint(document.CurrentSnapshot.Body, paragraph, 4);
            var before = rich.TextLayout.GetCaretBounds(rich.TextLayout.Capture()!, point)!.Value;
            var snapshot = document.CurrentSnapshot;
            Label? view = null;
            var views = new Dictionary<string, Label>();
            using var widgets = rich.Presentation.CreateWidgetSource(widget =>
            {
                var label = new Label { Text = widget.Id, WidthRequest = widget.EstimatedSize.Width, HeightRequest = widget.EstimatedSize.Height };
                views[widget.Id] = label;
                if (widget.Id == "hint") view = label;
                return label;
            });
            Assert.True(widgets.TryPublish(widgets.BeginUpdate(), new RichEditorWidget[]
            { new("hint", point, new(30, 16), Options: new() { Placement = RichTextAdornmentPlacement.Inline }) }));
            await Task.Delay(200);
            var after = rich.TextLayout.GetCaretBounds(rich.TextLayout.Capture()!, point)!.Value;
            Assert.NotNull(view);
            Assert.True(view.Bounds.Width > 0, $"Widget bounds: {view.Bounds}");
            Assert.True(after.X - before.X >= 29, $"Caret before {before}; after {after}");
            Assert.Same(snapshot, document.CurrentSnapshot);
            Assert.False(document.CanUndo);
            Assert.True(widgets.TryPublish(widgets.BeginUpdate(), new RichEditorWidget[]
            { new("hint", point, new(30, 30), Options: new() { Placement = RichTextAdornmentPlacement.Inline, Baseline = 8 }, ContentVersion: 1) }));
            await Task.Delay(120);
            var capture = rich.TextLayout.Capture()!;
            var sourceOffset = capture.TextProjection.GetOffset(point);
            var line = capture.Lines.First(line => line.SourceRanges.Any(range => range.Start <= sourceOffset && sourceOffset < range.End));
            Assert.InRange(Math.Abs(view.Bounds.Top + 8 - line.Baseline), 0, 1);
            rich.Selection = new RichEdit.Core.RichTextSelection(point, point);
            var handler = (RichEditorHandler)rich.NativeView.Handler!;
            handler.PlatformView.Document.Selection.TypeText("X");
            await Task.Delay(100);
            Assert.Contains("abcdXefgh", document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Empty(document.CurrentSnapshot.Nodes.Values.OfType<RichImage>());
            Assert.Empty(document.CurrentSnapshot.Assets);
            Assert.Single(handler.NativeProjection.Reservations);
            rich.Undo();
            await Task.Delay(80);
            Assert.Contains("abcdefgh", document.CurrentSnapshot.ProjectText().Text.ToString());
            var beforeRemoval = document.CurrentSnapshot;
            var slot = Assert.Single(handler.NativeProjection.Reservations);
            handler.PlatformView.Document.GetRange(slot.ImagePosition, slot.ImagePosition + 1).Delete(Microsoft.UI.Text.TextRangeUnit.Character, 0);
            await Task.Delay(80);
            Assert.Same(beforeRemoval, document.CurrentSnapshot);
            var repaired = Assert.Single(handler.NativeProjection.Reservations);
            Assert.Equal('\uFFFC', handler.PlatformView.Document.GetRange(repaired.ImagePosition, repaired.ImagePosition + 1).Character);
            Assert.True(widgets.TryPublish(widgets.BeginUpdate(), new RichEditorWidget[]
            {
                new("above", point, new(40, 20), Options: new() { Placement = RichTextAdornmentPlacement.AboveLine }),
                new("below", point, new(40, 24), Options: new() { Placement = RichTextAdornmentPlacement.BelowLine }),
            }));
            await Task.Delay(150);
            var current = rich.TextLayout.GetCaretBounds(rich.TextLayout.Capture()!, point)!.Value;
            Assert.True(views["above"].Bounds.Width > 0);
            Assert.True(views["below"].Bounds.Width > 0);
            Assert.True(views["above"].Bounds.Bottom <= current.Top + 1, $"Above {views["above"].Bounds}, text {current}");
            var belowSlot = handler.NativeProjection.Reservations.Single(item => item.Adornment.Options.Placement == RichTextAdornmentPlacement.BelowLine);
            var belowRange = handler.PlatformView.Document.GetRange(belowSlot.ImagePosition, belowSlot.ImagePosition + 1);
            belowRange.GetRect(Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var belowRect, out _);
            Assert.True(views["below"].Bounds.Top >= current.Bottom - 1, $"Below {views["below"].Bounds}, text {current}, native {belowRect}, font {belowRange.CharacterFormat.Size}");
            Assert.Same(beforeRemoval, document.CurrentSnapshot);
            Assert.True(document.CanRedo);
        }
        finally { window.Content = null; rich.NativeView.Handler?.DisconnectHandler(); rich.Handler?.DisconnectHandler(); }
    });

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
