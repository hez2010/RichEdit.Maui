using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using RichEdit.Core;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public partial class RichReviewViewTests
{
    private static Microsoft.UI.Xaml.Window? _window;
    private static MauiApp? _app;

    [Fact]
    public Task ReviewViewsStayNativeReadOnlyAndFollowCanonicalResolution() => WindowsTestHost.RunAsync(async () =>
    {
        var document = RichDocument.FromPlainText("original");
        var group = new RichRevisionInfo("Reviewer");
        document.Edit(edit => edit.ReplaceText(new(edit.Body, new(2), 0), 8, "current"), new(Revision: group));
        var review = new RichReviewView { Source = document, Mode = RichReviewMode.Original };
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        var app = _app ??= MauiApp.CreateBuilder().UseMauiApp<ReviewApplication>().UseRichEdit().Build();
        window.Content = review.ToPlatform(new MauiContext(app.Services));
        window.AppWindow.Show(activateWindow: false);
        try
        {
            await Task.Delay(80);
            var renderer = (RichEditor)review.Content;
            var native = ((RichEditorHandler)renderer.NativeView.Handler!).PlatformView;
            Assert.True(native.IsReadOnly);
            Assert.Equal("original", renderer.Document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.False(renderer.CanUndo);
            var captured = document.CurrentSnapshot;
            var history = document.RetainedHistoryByteEstimate;
            var point = new RichTextPoint(review.Projection.Snapshot.Body, new(2), 1);
            review.Selection = new RichEdit.Core.RichTextSelection(point, point);
            Assert.Equal(TextEdit.Core.TextEditRejection.ReadOnly, renderer.ReplaceSelection("no").Rejection);
            review.Mode = RichReviewMode.Final;
            Assert.Equal("current", renderer.Document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.Same(captured, document.CurrentSnapshot);
            Assert.Equal(history, document.RetainedHistoryByteEstimate);
            document.Edit(edit => edit.RejectRevision(group.Group));
            await Task.Delay(80);
            Assert.Same(document.CurrentSnapshot, review.Projection.Source);
            Assert.Equal("original", renderer.Document.CurrentSnapshot.ProjectText().Text.ToString());
            Assert.True(native.IsReadOnly);
            Assert.False(renderer.CanUndo);
        }
        finally
        {
            window.Content = null;
            ((RichEditor)review.Content).NativeView.Handler?.DisconnectHandler();
            review.Handler?.DisconnectHandler();
        }
    });

    private sealed partial class ReviewApplication : Microsoft.Maui.Controls.Application;
}
