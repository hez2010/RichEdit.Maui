namespace RichEdit.Maui;

public partial class RichEditorHandler
{
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
        _adornmentsConnected = true;
        VirtualView.ContentChanged += OnAdornmentContentChanged;
        VirtualView.Folding.Changed += OnAdornmentLayoutChanged;
        VirtualView.Decorations.Changed += OnAdornmentLayoutChanged;
        VirtualView.EffectiveAppearanceChanged += OnAdornmentLayoutChanged;
        ConnectAdornmentViewport();
        UpdateAdornments();
    }

    private void DisconnectAdornments()
    {
        if (!_adornmentsConnected) return;
        _adornmentsConnected = false;
        _adornmentLayoutQueued = false;
        VirtualView.ContentChanged -= OnAdornmentContentChanged;
        VirtualView.Folding.Changed -= OnAdornmentLayoutChanged;
        VirtualView.Decorations.Changed -= OnAdornmentLayoutChanged;
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
    private void OnAdornmentLayoutChanged(object? sender, EventArgs args) => QueueAdornmentLayout();

    private void QueueAdornmentLayout()
    {
        if (!_adornmentsConnected || _adornmentLayoutQueued || VirtualView.Adornments.Count == 0) return;
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
            foreach (var item in adornments)
            {
                var bounds = new Rect(-100000, -100000, 0, 0);
                if (VirtualView.Folding.FindRange(item.Position) is null && GetAdornmentAnchor(item.Position) is { Height: > 0 } anchor)
                {
                    var width = item.Placement == RichTextAdornmentPlacement.LeftMargin ? adornments.MarginWidth : viewport.Width;
                    var size = ((IView)item.View).Measure(width, viewport.Height);
                    var x = item.Placement == RichTextAdornmentPlacement.LeftMargin ? (width - size.Width) / 2 : anchor.X;
                    var proposed = new Rect(x + item.Offset.X, anchor.Y + (anchor.Height - size.Height) / 2 + item.Offset.Y, size.Width, size.Height);
                    if (proposed.IntersectsWith(viewport)) bounds = proposed;
                }
                item.LayoutBounds = bounds;
            }
            ((IView)adornments.Overlay).Measure(viewport.Width, viewport.Height);
            ((IView)adornments.Overlay).Arrange(viewport);
            adornments.Overlay.ArrangeViews();
            CommitAdornmentLayout();
        }
        finally { _arrangingAdornments = false; }
    }

    private partial void AttachAdornmentOverlay();
    private partial void DetachAdornmentOverlay();
    private partial void ConnectAdornmentViewport();
    private partial void DisconnectAdornmentViewport();
    private partial void SetAdornmentMargin(double width);
    private partial Rect GetAdornmentViewport();
    private partial Rect? GetAdornmentAnchor(int position);
    partial void CommitAdornmentLayout();
}
