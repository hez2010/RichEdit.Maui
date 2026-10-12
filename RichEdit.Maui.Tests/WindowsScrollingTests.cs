using Microsoft.Maui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RichEdit.Maui.Tests;

public sealed class WindowsScrollingTests
{
    private static Microsoft.UI.Xaml.Window? _window;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ScrollingCanLeaveThePrimaryCaretOutsideTheViewport(bool multipleCarets, bool structured) => WindowsTestHost.RunAsync(async () =>
    {
        var editor = new NativeEditorView
        {
            Document = RichTextDocument.FromPlainText(string.Join("\n", Enumerable.Range(0, 250).Select(i => $"Line {i}: native scrolling"))),
        };
        if (structured) editor.BindStructuredDocument(RichEdit.Core.RichDocument.FromPlainText(editor.Document.Text));
        var handler = new RichEditorHandler();
        handler.SetMauiContext(new MauiContext(WindowsTestHost.MauiApp.Services));
        editor.Handler = handler;
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        window.Content = handler.ContainerView ?? handler.PlatformView;
        window.AppWindow.Resize(new(700, 450));
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(120);
            editor.SelectedRange = new(0, 0);
            if (multipleCarets) editor.Selections = new((TextSelection[])[new(0, 0), new(12, 12)], 0);
            var selections = editor.Selections;
            handler.PlatformView.Focus(FocusState.Programmatic);
            await Task.Delay(150);
            var scroll = FindScroller(handler.PlatformView)!;
            Assert.True(scroll.ScrollableHeight > 1000);
            scroll.ChangeView(null, 800, null, true);
            await Task.Delay(300);
            Assert.True(scroll.VerticalOffset > 600, $"The viewport returned to the caret: {scroll.VerticalOffset}.");
            Assert.Equal(selections, editor.Selections);
            scroll.ChangeView(null, 1400, null, true);
            await Task.Delay(200);
            Assert.True(scroll.VerticalOffset > 1200, $"The second scroll returned to the caret: {scroll.VerticalOffset}.");
            Assert.Equal(selections, editor.Selections);
        }
        finally { window.Content = null; editor.Handler = null; ((IElementHandler)handler).DisconnectHandler(); }
    });

    [WinRT.DynamicWindowsRuntimeCast(typeof(ScrollViewer))]
    internal static ScrollViewer? FindScroller(DependencyObject element)
    {
        if (element is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindScroller(VisualTreeHelper.GetChild(element, index)) is { } child) return child;
        return null;
    }
}
