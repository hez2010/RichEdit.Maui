using Microsoft.Maui;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using RichEdit.Core;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;
using Visibility = Microsoft.UI.Xaml.Visibility;
using Window = Microsoft.UI.Xaml.Window;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class WindowsSplitViewTests
{
    private static Window? _window;

    [Theory]
    [InlineData(ElementTheme.Light)]
    [InlineData(ElementTheme.Dark)]
    public Task RevealingSharedViewPreservesImportedFontsAndSizes(ElementTheme theme) => WindowsTestHost.RunAsync(async () =>
    {
        var first = new RichEditor { FontSize = 17 };
        var second = new RichEditor { Document = first.Document, FontSize = 17 };
        var firstHandler = new RichEditorHandler();
        var secondHandler = new RichEditorHandler();
        var context = new MauiContext(WindowsTestHost.MauiApp.Services);
        firstHandler.SetMauiContext(context);
        secondHandler.SetMauiContext(context);
        first.NativeView.Handler = firstHandler;
        second.NativeView.Handler = secondHandler;
        var peer = new Grid { Visibility = Visibility.Collapsed };
        peer.Children.Add(secondHandler.ContainerView ?? secondHandler.PlatformView);
        var root = new Grid { RequestedTheme = theme };
        root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(peer, 1);
        root.Children.Add(firstHandler.ContainerView ?? firstHandler.PlatformView);
        root.Children.Add(peer);
        var window = _window ??= new Window();
        window.Content = root;
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(100);
            using var input = typeof(WindowsSplitViewTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
            var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
            var document = new RichDocument(imported.Snapshot);
            var snapshot = document.CurrentSnapshot;
            first.Document = document;
            second.Document = document;
            var projection = snapshot.ProjectText();
            var point = projection.GetPoint(projection.Text.ToString().IndexOf("Font size 22", StringComparison.Ordinal))!.Value;
            second.Selection = new RichEdit.Core.RichTextSelection(point with { Offset = point.Offset + 4 }, point);
            var selection = second.Selection;
            AssertFonts(firstHandler);
            peer.Visibility = Visibility.Visible;
            await Task.Delay(100);
            AssertFonts(firstHandler);
            AssertFonts(secondHandler);
            Assert.Equal(selection, second.Selection);
            Assert.Same(snapshot, document.CurrentSnapshot);
            Assert.False(document.CanUndo);

            first.Selection = new RichEdit.Core.RichTextSelection(point, point with { Offset = point.Offset + 4 });
            first.ToggleBold();
            Assert.True(document.CanUndo);
            AssertFonts(firstHandler);
            AssertFonts(secondHandler);
            peer.Visibility = Visibility.Collapsed;
            await Task.Delay(30);
            peer.Visibility = Visibility.Visible;
            await Task.Delay(100);
            AssertFonts(secondHandler);
            Assert.Equal(selection, second.Selection);
            Assert.True(document.CanUndo);
            second.Undo();
            AssertFonts(firstHandler);
            AssertFonts(secondHandler);
            Assert.False(document.CanUndo);
            Assert.True(document.CanRedo);
        }
        finally
        {
            window.Content = null;
            first.NativeView.Handler = null;
            second.NativeView.Handler = null;
            ((IElementHandler)firstHandler).DisconnectHandler();
            ((IElementHandler)secondHandler).DisconnectHandler();
        }
    });

    private static void AssertFonts(RichEditorHandler handler)
    {
        var document = handler.PlatformView.Document;
        var story = document.GetRange(0, 0);
        story.SetRange(0, story.StoryLength);
        foreach (var (text, size) in new[] { ("Font size 10", 10f), ("Font size 12", 12f), ("Font size 22", 22f), ("1.1 item", 12f) })
        {
            var start = story.Text.IndexOf(text, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing '{text}' in native document.");
            var format = document.GetRange(start, start + text.Length).CharacterFormat;
            Assert.Equal("Times New Roman", format.Name);
            Assert.Equal(size, format.Size);
        }
    }
}
