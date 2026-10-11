using CodeEdit.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;

namespace RichEdit.Maui.Tests;

public partial class CodeEditorTests
{
    [Fact(Explicit = true)]
    public Task LargeNativeSourceAndLongLineKeepSplitViewsAndHistoryCoherent() => WithPerformanceSourceSurface(async editor =>
    {
        var host = (Microsoft.Maui.Controls.Grid)editor.Parent;
        var peer = new CodeEditor();
        host.Children.Add(peer);
        ((IView)host).Measure(660, 350);
        ((IView)host).Arrange(new(0, 0, 660, 350));
        try { await SourcePerformanceWorkload.RunLarge(editor, peer); }
        finally
        {
            host.Children.Remove(peer);
            peer.TextView.Handler?.DisconnectHandler();
            peer.Handler?.DisconnectHandler();
        }
    }, TimeSpan.FromMinutes(5));

    [Fact(Explicit = true)]
    public Task ModernSourceDenseWorkloadMeasuresLazyWidgetsAndLatencyPercentiles() => WithPerformanceSourceSurface(
        SourcePerformanceWorkload.RunDense, TimeSpan.FromMinutes(3));

    [Fact]
    public Task NativeTextReservationsKeepGeometrySourceAccessibilityAndWrapFallback() => WithPerformanceSourceSurface(async editor =>
    {
        editor.Document = new("ab\ncd");
        editor.WordWrap = false;
        var handler = (RichEditorHandler)editor.TextView.Handler!;
        using var small = editor.Adornments.Add(1, new Microsoft.Maui.Controls.Label { Text = "hint", WidthRequest = 38, HeightRequest = 12 },
            new() { Placement = RichTextAdornmentPlacement.Inline });
        using var tall = editor.Adornments.Add(4, new Microsoft.Maui.Controls.Label { Text = "tall", WidthRequest = 30, HeightRequest = 60 },
            new() { Placement = RichTextAdornmentPlacement.Inline });
        await WaitFor(() => handler.NativeProjection.Reservations.Length == 2);
        var slot = handler.NativeProjection.Reservations.Single(item => ReferenceEquals(item.Adornment, small));
        var big = handler.NativeProjection.Reservations.Single(item => ReferenceEquals(item.Adornment, tall));
        var range = handler.PlatformView.Document.GetRange(slot.ImagePosition, slot.ImagePosition + 1);
        Assert.Equal('\u00A0', range.Character);
        Assert.Equal('\uFFFC', handler.PlatformView.Document.GetRange(big.ImagePosition, big.ImagePosition + 1).Character);
        range.GetRect(Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var bounds, out _);
        Assert.InRange(bounds.Width, 37, 39);
        Assert.Equal("ab\ncd", editor.Document.Text);
        Assert.False(editor.Document.IsModified);
        editor.SelectedRange = new(0, 0);
        handler.PlatformView.Document.Selection.TypeText("X");
        Assert.Equal("Xab\ncd", editor.Document.Text);
        await Task.Delay(50);
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(handler.PlatformView);
        var provider = (Microsoft.UI.Xaml.Automation.Provider.ITextProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Text);
        Assert.Equal(editor.Document.Text, provider.DocumentRange.GetText(-1));
        editor.Undo();
        Assert.Equal("ab\ncd", editor.Document.Text);
        var beforeWrap = editor.Document.CurrentSnapshot;
        editor.WordWrap = true;
        await WaitFor(() => handler.NativeProjection.Reservations.All(item => item.Text.Contains('\uFFFC')));
        Assert.Same(beforeWrap, editor.Document.CurrentSnapshot);
        editor.WordWrap = false;
        await WaitFor(() => handler.NativeProjection.Reservations.Any(item => ReferenceEquals(item.Adornment, small) && item.Text.Contains('\u00A0')));
        Assert.Same(beforeWrap, editor.Document.CurrentSnapshot);
        editor.SelectedRange = new(0, 1);
        handler.PlatformView.Document.Selection.CharacterFormat.Bold = Microsoft.UI.Text.FormatEffect.On;
        await Task.Delay(80);
        Assert.Same(beforeWrap, editor.Document.CurrentSnapshot);
        Assert.Equal(Microsoft.UI.Text.FormatEffect.Off, handler.PlatformView.Document.GetRange(0, 1).CharacterFormat.Bold);

        static async Task WaitFor(Func<bool> ready)
        {
            for (var pass = 0; pass < 100 && !ready(); pass++) await Task.Delay(20);
            Assert.True(ready());
        }
    });

    private static Task WithPerformanceSourceSurface(Func<CodeEditor, Task> action, TimeSpan? timeout = null) => WindowsTestHost.RunAsync(async () =>
    {
        var app = _controlTestApp ??= MauiApp.CreateBuilder().UseMauiApp<CodeTestApplication>().UseCodeEditor().Build();
        var editor = new CodeEditor();
        var host = new Microsoft.Maui.Controls.Grid { Children = { editor } };
        var window = _gutterWindow ??= new Microsoft.UI.Xaml.Window();
        window.Content = host.ToPlatform(new MauiContext(app.Services));
        try
        {
            window.Activate();
            ((IView)host).Measure(660, 350);
            ((IView)host).Arrange(new(0, 0, 660, 350));
            await action(editor);
        }
        finally
        {
            window.Content = null;
            editor.Handler?.DisconnectHandler();
            editor.TextView.Handler?.DisconnectHandler();
        }
    }, timeout);
}
