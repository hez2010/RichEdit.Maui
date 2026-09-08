using Android.Views;
using Android.Widget;
using Microsoft.Maui.Platform;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichEdit.Maui.Platforms.Android.RichEditText _adornmentView = null!;
    private ViewTreeObserver? _adornmentObserver;
    private int _appliedAdornmentMargin;
    private AdornmentHost? _adornmentHost;

    private partial void AttachAdornmentOverlay()
    {
        if (ContainerView is not ViewGroup container) return;
        var overlay = VirtualView.Adornments.Overlay.ToPlatform(MauiContext!);
        if (_adornmentHost is null)
        {
            _adornmentHost = new AdornmentHost(Context, () =>
            {
                if (!_arrangingAdornments) QueueAdornmentLayout();
            });
            _adornmentHost.AddView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            container.AddView(_adornmentHost);
        }
    }

    private partial void DetachAdornmentOverlay()
    {
        if (_adornmentHost is not { } host) return;
        host.RemoveAllViews();
        (host.Parent as ViewGroup)?.RemoveView(host);
        host.Dispose();
        _adornmentHost = null;
    }

    private partial void ConnectAdornmentViewport()
    {
        _adornmentView = PlatformView;
        _adornmentView.ViewAttachedToWindow += OnAdornmentNativeAttached;
        _adornmentView.ViewDetachedFromWindow += OnAdornmentNativeDetached;
        if (_adornmentView.IsAttachedToWindow) ObserveAdornmentViewport();
    }

    private void ObserveAdornmentViewport()
    {
        StopObservingAdornmentViewport();
        _adornmentObserver = _adornmentView.ViewTreeObserver;
        if (_adornmentObserver is null) return;
        _adornmentObserver.ScrollChanged += OnAdornmentViewportChanged;
        _adornmentObserver.GlobalLayout += OnAdornmentViewportChanged;
    }

    private partial void DisconnectAdornmentViewport()
    {
        _adornmentView.ViewAttachedToWindow -= OnAdornmentNativeAttached;
        _adornmentView.ViewDetachedFromWindow -= OnAdornmentNativeDetached;
        StopObservingAdornmentViewport();
        SetAdornmentMargin(0);
        _adornmentView = null!;
    }

    private void StopObservingAdornmentViewport()
    {
        if (_adornmentObserver is { IsAlive: true })
        {
            _adornmentObserver.ScrollChanged -= OnAdornmentViewportChanged;
            _adornmentObserver.GlobalLayout -= OnAdornmentViewportChanged;
        }
        _adornmentObserver = null;
    }

    private partial void SetAdornmentMargin(double width)
    {
        var pixels = checked((int)Math.Round(width * (_adornmentView.Resources?.DisplayMetrics?.Density ?? 1f)));
        if (_appliedAdornmentMargin == pixels) return;
        _adornmentView.SetPadding(_adornmentView.PaddingLeft + pixels - _appliedAdornmentMargin,
            _adornmentView.PaddingTop, _adornmentView.PaddingRight, _adornmentView.PaddingBottom);
        _appliedAdornmentMargin = pixels;
    }

    private partial Rect GetAdornmentViewport()
    {
        var density = _adornmentView.Resources?.DisplayMetrics?.Density ?? 1f;
        return new(0, 0, _adornmentView.Width / density, _adornmentView.Height / density);
    }

    private partial Rect? GetAdornmentAnchor(int position)
    {
        if (_adornmentView.Layout is not { } layout) return null;
        var density = _adornmentView.Resources?.DisplayMetrics?.Density ?? 1f;
        var line = layout.GetLineForOffset(position);
        return new((layout.GetPrimaryHorizontal(position) + _adornmentView.CompoundPaddingLeft - _adornmentView.ScrollX) / density,
            (layout.GetLineTop(line) + _adornmentView.CompoundPaddingTop - _adornmentView.ScrollY) / density,
            1, (layout.GetLineBottom(line) - layout.GetLineTop(line)) / density);
    }

    private void OnAdornmentViewportChanged(object? sender, EventArgs args) => QueueAdornmentLayout();

    private void OnAdornmentNativeAttached(object? sender, EventArgs args)
    {
        ObserveAdornmentViewport();
        QueueAdornmentLayout();
    }

    private void OnAdornmentNativeDetached(object? sender, EventArgs args) => StopObservingAdornmentViewport();

    partial void CommitAdornmentLayout()
    {
        if (_adornmentHost is not { } host) return;
        host.Measure(global::Android.Views.View.MeasureSpec.MakeMeasureSpec(_adornmentView.Width, MeasureSpecMode.Exactly),
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(_adornmentView.Height, MeasureSpecMode.Exactly));
        host.Layout(0, 0, _adornmentView.Width, _adornmentView.Height);
    }

    // This overlay always has the viewport's size. Child changes must lay out only its subtree:
    // propagating RequestLayout to the text control makes Android scroll back to its caret.
    private sealed class AdornmentHost : FrameLayout
    {
        private readonly Action? _requestLayout;

        internal AdornmentHost(global::Android.Content.Context context, Action requestLayout) : base(context)
        {
            _requestLayout = requestLayout;
        }

        public override void RequestLayout()
        {
            ForceLayout();
            _requestLayout?.Invoke();
        }
    }
}
