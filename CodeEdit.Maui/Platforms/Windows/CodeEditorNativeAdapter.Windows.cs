using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;

namespace CodeEdit.Maui;

internal sealed partial class CodeEditorNativeAdapter
{
    private RichEditBox PlatformView = null!;
    private bool _isComposing;
    private ScrollViewer? _scrollViewer;
    internal partial bool IsComposing => _isComposing;
    internal partial bool Post(Action action) => PlatformView.DispatcherQueue.TryEnqueue(() => action());

    private partial void Connect()
    {
        var platformView = PlatformView = _handler.PlatformView;
        platformView.Loaded += OnCodeLoaded;
        platformView.SizeChanged += OnCodeSizeChanged;
        platformView.TextCompositionStarted += OnCompositionStarted;
        platformView.TextCompositionEnded += OnCompositionEnded;
        if (platformView.IsLoaded) ConnectScroller();
    }

    private partial void Disconnect()
    {
        var platformView = PlatformView;
        platformView.Loaded -= OnCodeLoaded;
        platformView.SizeChanged -= OnCodeSizeChanged;
        platformView.TextCompositionStarted -= OnCompositionStarted;
        platformView.TextCompositionEnded -= OnCompositionEnded;
        if (_scrollViewer is not null) _scrollViewer.ViewChanged -= OnScrollChanged;
        _scrollViewer = null;
        _isComposing = false;
    }

    internal partial void UpdateConfiguration()
    {
        PlatformView.TextWrapping = Owner.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(PlatformView, Owner.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    internal partial IReadOnlyList<VisibleCodeLine> GetVisibleLines()
    {
        var result = new List<VisibleCodeLine>();
        if (PlatformView.ActualHeight <= 0) return result;
        // TOM coordinates belong to the text surface inside the template's scroller.
        // Transform its origin to the RichEditBox so padding and scrolling both count.
        var origin = (_scrollViewer?.Content as UIElement)?.TransformToVisual(PlatformView)
            .TransformPoint(new Windows.Foundation.Point(0, 0)) ?? new Windows.Foundation.Point(PlatformView.Padding.Left, PlatformView.Padding.Top);
        var top = PlatformView.Document.GetRangeFromPoint(new Windows.Foundation.Point(0, Math.Max(0, -origin.Y)), PointOptions.ClientCoordinates);
        var first = Owner.Lines.GetLineIndex(Math.Clamp(top.StartPosition, 0, Owner.Document.Length));
        for (var index = first; index < Owner.LineCount; index++)
        {
            var range = Owner.Lines.GetRange(index);
            PlatformView.Document.GetRange(range.Start, range.Start).GetRect(
                PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
            var y = rect.Y + origin.Y;
            if (y >= PlatformView.ActualHeight) break;
            if (rect.Height > 0 && y + rect.Height > 0) result.Add(new(index + 1, (float)y, (float)rect.Height));
        }
        return result;
    }

    internal partial void ScrollToSelection() => PlatformView.Document.Selection.ScrollIntoView(PointOptions.None);

    private void OnCompositionStarted(RichEditBox sender, TextCompositionStartedEventArgs args) => _isComposing = true;
    private void OnCompositionEnded(RichEditBox sender, TextCompositionEndedEventArgs args)
    {
        _isComposing = false;
        Owner.ScheduleHighlighting();
    }
    private void OnCodeSizeChanged(object sender, SizeChangedEventArgs args) => Owner.InvalidateGutter();
    private void OnScrollChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Owner.InvalidateGutter();
    private void OnCodeLoaded(object sender, RoutedEventArgs args)
    {
        ConnectScroller();
        Owner.InvalidateGutter();
    }

    private void ConnectScroller()
    {
        if (_scrollViewer is not null) _scrollViewer.ViewChanged -= OnScrollChanged;
        _scrollViewer = FindScrollViewer(PlatformView);
        if (_scrollViewer is not null) _scrollViewer.ViewChanged += OnScrollChanged;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
        return null;
    }
}
