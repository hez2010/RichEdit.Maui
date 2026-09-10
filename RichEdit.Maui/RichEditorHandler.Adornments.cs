namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private const int AdornmentBatchSize = 24;
    private const int AdornmentPrefetchCharacters = 256;
    private bool _adornmentsConnected;
    private bool _adornmentLayoutQueued;
    private bool _arrangingAdornments;

    /// <inheritdoc />
    public override bool NeedsContainer => true;

    /// <inheritdoc />
    protected override void SetupContainer()
    {
        base.SetupContainer();
        if (_adornmentsConnected) AttachAdornmentOverlay();
    }

    private void ConnectAdornments()
    {
        LayoutAttachment = new();
        _adornmentsConnected = true;
        ConnectInputObservation();
        VirtualView.ContentChanged += OnAdornmentContentChanged;
        VirtualView.Folding.Changed += OnAdornmentLayoutChanged;
        VirtualView.Decorations.Changed += OnDecorationLayoutChanged;
        VirtualView.EffectiveAppearanceChanged += OnAdornmentLayoutChanged;
        ConnectAdornmentViewport();
        UpdateAdornments();
    }

    private void DisconnectAdornments()
    {
        if (!_adornmentsConnected) return;
        _adornmentsConnected = false;
        DisconnectInputObservation();
        _compositionOperationDepth = 0;
        VirtualView.SetCompositionState(default);
        LayoutAttachment = new();
        VirtualView.TextLayout.Invalidate();
        _adornmentLayoutQueued = false;
        VirtualView.ContentChanged -= OnAdornmentContentChanged;
        VirtualView.Folding.Changed -= OnAdornmentLayoutChanged;
        VirtualView.Decorations.Changed -= OnDecorationLayoutChanged;
        VirtualView.EffectiveAppearanceChanged -= OnAdornmentLayoutChanged;
        DisconnectAdornmentViewport();
        DetachAdornmentOverlay();
        VirtualView.Adornments.Overlay.Handler?.DisconnectHandler();
        foreach (var adornment in VirtualView.Adornments) adornment.View.Handler?.DisconnectHandler();
    }

    internal void UpdateAdornments()
    {
        if (!_adornmentsConnected || _arrangingAdornments) return;
        // Keep native text input in a stable parent throughout the editor's lifetime.
        // Reparenting it when an adornment is added can disturb selection and IME state.
        HasContainer = true;
        AttachAdornmentOverlay();
        SetAdornmentMargin(VirtualView.Adornments.MarginWidth);
        QueueAdornmentLayout();
    }

    private void OnAdornmentContentChanged(object? sender, RichTextContentChangedEventArgs args) => QueueAdornmentLayout();
    private void OnDecorationLayoutChanged(object? sender, EventArgs args) => QueueAdornmentLayout();
    private void OnAdornmentLayoutChanged(object? sender, EventArgs args)
    {
        _displayAdornmentVersion = -1;
        QueueAdornmentLayout();
    }

    private void QueueAdornmentLayout()
    {
        if (_adornmentsConnected) VirtualView.TextLayout.Invalidate();
        if (!_adornmentsConnected || _adornmentLayoutQueued || VirtualView.Adornments.Count == 0 && NativeProjection.IsEmpty) return;
        _adornmentLayoutQueued = true;
        VirtualView.Dispatcher.Dispatch(() =>
        {
            _adornmentLayoutQueued = false;
            if (_adornmentsConnected) ArrangeAdornments();
        });
    }

    private void ArrangeAdornments()
    {
        if (_arrangingAdornments) return;
        _arrangingAdornments = true;
        try
        {
            var viewport = GetAdornmentViewport();
            var adornments = VirtualView.Adornments;
            var layout = CaptureTextLayoutCore();
            var sourceRanges = layout?.Lines.SelectMany(static line => line.SourceRanges).ToArray() ?? [];
            var visibleStart = sourceRanges.Length == 0 ? HitTestTextCore(new(0, 0))?.Position ?? 0 : sourceRanges.Min(static range => range.Start);
            var visibleEnd = sourceRanges.Length == 0 ? HitTestTextCore(new(viewport.Width, viewport.Height))?.Position ?? VirtualView.Document.Length : sourceRanges.Max(static range => range.End);
            var realized = 0;
            var pending = false;
            foreach (var item in adornments)
            {
                // Keep a source margin around the viewport, using cached sizes beyond it.
                var nearby = item.Position >= Math.Max(0, visibleStart - AdornmentPrefetchCharacters) && item.Position <= visibleEnd + AdornmentPrefetchCharacters;
                if (!item.IsRealized && nearby)
                {
                    if (realized++ >= AdornmentBatchSize) { pending = true; continue; }
                    adornments.Overlay.SetRealized(item, true);
                    _displayAdornmentVersion = -1;
                }
                else if (item.IsRealized && !nearby && !HasFocusedContent(item.View)) adornments.Overlay.SetRealized(item, false);
                if (item.IsRealized && item.MeasureDirty) _displayAdornmentVersion = -1;
            }
            RefreshDisplayProjection(layout);
            layout = CaptureTextLayoutCore();
            sourceRanges = layout?.Lines.SelectMany(static line => line.SourceRanges).ToArray() ?? [];
            visibleStart = Math.Min(visibleStart, sourceRanges.Length == 0 ? visibleStart : sourceRanges.Min(static range => range.Start));
            visibleEnd = Math.Max(visibleEnd, sourceRanges.Length == 0 ? visibleEnd : sourceRanges.Max(static range => range.End));
            var reservations = NativeProjection.Reservations.Where(static entry => entry.ImagePosition >= 0)
                .ToDictionary(static entry => entry.Adornment);
            foreach (var item in adornments)
            {
                var bounds = new Rect(-100000, -100000, 0, 0);
                var anchorPosition = reservations.TryGetValue(item, out var entry) ? entry.SourcePosition : item.Position;
                if (!item.IsRealized || anchorPosition < visibleStart || anchorPosition > visibleEnd)
                {
                    item.LayoutBounds = bounds;
                    if (item.View.Bounds != bounds) ((IView)item.View).Arrange(bounds);
                    continue;
                }
                if (item.ReservesSpace)
                {
                    if (reservations.TryGetValue(item, out var reservation) &&
                        GetDisplayReservationBounds(reservation) is { } reserved && reserved.IntersectsWith(viewport)) bounds = reserved;
                }
                else if (VirtualView.Folding.FindRange(item.Position) is null && GetAdornmentAnchor(NativeProjection.ToDisplay(item.Position)) is { Height: > 0 } anchor)
                {
                    var width = item.Options.Placement == RichTextAdornmentPlacement.LeftMargin ? adornments.MarginWidth : viewport.Width;
                    var size = ((IView)item.View).Measure(width, viewport.Height);
                    var x = item.Options.Placement == RichTextAdornmentPlacement.LeftMargin ? (width - size.Width) / 2 : anchor.X;
                    var proposed = new Rect(x + item.Options.Offset.X, anchor.Y + (anchor.Height - size.Height) / 2 + item.Options.Offset.Y, size.Width, size.Height);
                    if (proposed.IntersectsWith(viewport)) bounds = proposed;
                }
                item.LayoutBounds = bounds;
            }
            ((IView)adornments.Overlay).Measure(viewport.Width, viewport.Height);
            ((IView)adornments.Overlay).Arrange(viewport);
            adornments.Overlay.ArrangeViews();
            CommitAdornmentLayout();
            if (pending) QueueAdornmentLayout();
        }
        finally { _arrangingAdornments = false; }
    }

    private static bool HasFocusedContent(IVisualTreeElement element) => element is VisualElement { IsFocused: true } || element.GetVisualChildren().Any(HasFocusedContent);

    private partial void AttachAdornmentOverlay();
    private partial void DetachAdornmentOverlay();
    private partial void ConnectAdornmentViewport();
    private partial void DisconnectAdornmentViewport();
    private partial void SetAdornmentMargin(double width);
    private partial Rect GetAdornmentViewport();
    private partial Rect? GetAdornmentAnchor(int position);
    private partial void OffsetAdornmentScroll(double verticalDelta);
    partial void CommitAdornmentLayout();
}
