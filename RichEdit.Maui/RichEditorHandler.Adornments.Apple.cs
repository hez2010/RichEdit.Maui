#if IOS || MACCATALYST
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichEdit.Maui.Platforms.Apple.RichTextView _adornmentView = null!;
    private IDisposable? _adornmentScrollObservation;
    private IDisposable? _adornmentBoundsObservation;
    private IDisposable? _adornmentContentSizeObservation;
    private double _appliedAdornmentMargin;

    private partial void AttachAdornmentOverlay()
    {
        if (ContainerView is not { } container) return;
        var overlay = VirtualView.Adornments.Overlay.ToPlatform(MauiContext!);
        if (overlay.Superview != container) container.AddSubview(overlay);
    }

    private partial void DetachAdornmentOverlay()
    {
        if (VirtualView.Adornments.Overlay.Handler?.PlatformView is UIView overlay) overlay.RemoveFromSuperview();
    }

    private partial void ConnectAdornmentViewport()
    {
        _adornmentView = PlatformView;
        _adornmentScrollObservation = _adornmentView.AddObserver("contentOffset", NSKeyValueObservingOptions.New, _ => QueueAdornmentLayout());
        _adornmentBoundsObservation = _adornmentView.AddObserver("bounds", NSKeyValueObservingOptions.New, _ => QueueAdornmentLayout());
        _adornmentContentSizeObservation = _adornmentView.AddObserver("contentSize", NSKeyValueObservingOptions.New, _ => QueueAdornmentLayout());
    }

    private partial void DisconnectAdornmentViewport()
    {
        _adornmentScrollObservation?.Dispose();
        _adornmentBoundsObservation?.Dispose();
        _adornmentContentSizeObservation?.Dispose();
        _adornmentScrollObservation = null;
        _adornmentBoundsObservation = null;
        _adornmentContentSizeObservation = null;
        SetAdornmentMargin(0);
        _adornmentView = null!;
    }

    private partial void SetAdornmentMargin(double width)
    {
        if (_appliedAdornmentMargin == width) return;
        var inset = _adornmentView.TextContainerInset;
        _adornmentView.TextContainerInset = new(inset.Top, inset.Left + (nfloat)(width - _appliedAdornmentMargin), inset.Bottom, inset.Right);
        _appliedAdornmentMargin = width;
    }

    private partial Rect GetAdornmentViewport() => new(0, 0, _adornmentView.Bounds.Width, _adornmentView.Bounds.Height);

    private partial Rect? GetAdornmentAnchor(int position)
    {
        using var nativePosition = _adornmentView.GetPosition(_adornmentView.BeginningOfDocument, position);
        if (nativePosition is null) return null;
        var rect = _adornmentView.GetCaretRectForPosition(nativePosition);
        return new(rect.X - _adornmentView.ContentOffset.X, rect.Y - _adornmentView.ContentOffset.Y, rect.Width, rect.Height);
    }
}
#endif
