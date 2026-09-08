using Microsoft.Maui.Platform;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichEditBox _adornmentView = null!;
    private ScrollViewer? _adornmentScroller;
    private double _appliedAdornmentMargin;

    private partial void AttachAdornmentOverlay()
    {
        if (ContainerView is not Microsoft.UI.Xaml.Controls.Panel container) return;
        var overlay = VirtualView.Adornments.Overlay.ToPlatform(MauiContext!);
        if (!container.Children.Contains(overlay)) container.Children.Add(overlay);
    }

    private partial void DetachAdornmentOverlay()
    {
        if (ContainerView is Microsoft.UI.Xaml.Controls.Panel container && VirtualView.Adornments.Overlay.Handler?.PlatformView is UIElement overlay)
            container.Children.Remove(overlay);
    }

    private partial void ConnectAdornmentViewport()
    {
        _adornmentView = PlatformView;
        _adornmentView.Loaded += OnAdornmentLoaded;
        _adornmentView.SizeChanged += OnAdornmentSizeChanged;
        if (_adornmentView.IsLoaded) ConnectAdornmentScroller();
    }

    private partial void DisconnectAdornmentViewport()
    {
        _adornmentView.Loaded -= OnAdornmentLoaded;
        _adornmentView.SizeChanged -= OnAdornmentSizeChanged;
        if (_adornmentScroller is not null) _adornmentScroller.ViewChanged -= OnAdornmentScrolled;
        _adornmentScroller = null;
        SetAdornmentMargin(0);
        _adornmentView = null!;
    }

    private partial void SetAdornmentMargin(double width)
    {
        if (_appliedAdornmentMargin == width) return;
        var padding = _adornmentView.Padding;
        _adornmentView.Padding = new(padding.Left + width - _appliedAdornmentMargin, padding.Top, padding.Right, padding.Bottom);
        _appliedAdornmentMargin = width;
    }

    private partial Rect GetAdornmentViewport() => new(0, 0, _adornmentView.ActualWidth, _adornmentView.ActualHeight);

    partial void CommitAdornmentLayout() => (VirtualView.Adornments.Overlay.Handler?.PlatformView as UIElement)?.InvalidateArrange();

    private partial Rect? GetAdornmentAnchor(int position)
    {
        if (!_adornmentView.IsLoaded) return null;
        var origin = (_adornmentScroller?.Content as UIElement)?.TransformToVisual(_adornmentView).TransformPoint(new(0, 0))
            ?? new global::Windows.Foundation.Point(_adornmentView.Padding.Left, _adornmentView.Padding.Top);
        _adornmentView.Document.GetRange(position, position).GetRect(
            PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        return new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);
    }

    private void OnAdornmentLoaded(object sender, RoutedEventArgs args)
    {
        ConnectAdornmentScroller();
        QueueAdornmentLayout();
    }

    private void OnAdornmentSizeChanged(object sender, SizeChangedEventArgs args) => QueueAdornmentLayout();
    private void OnAdornmentScrolled(object? sender, ScrollViewerViewChangedEventArgs args) => QueueAdornmentLayout();

    private void ConnectAdornmentScroller()
    {
        if (_adornmentScroller is not null) _adornmentScroller.ViewChanged -= OnAdornmentScrolled;
        _adornmentScroller = FindScroller(_adornmentView);
        if (_adornmentScroller is not null) _adornmentScroller.ViewChanged += OnAdornmentScrolled;

        static ScrollViewer? FindScroller(DependencyObject view)
        {
            if (view is ScrollViewer scroller) return scroller;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(view); index++)
                if (FindScroller(VisualTreeHelper.GetChild(view, index)) is { } child) return child;
            return null;
        }
    }
}
