using System.Collections;
using Microsoft.Maui.Layouts;

namespace RichEdit.Maui;

/// <summary>Specifies where an adornment is displayed relative to its source position.</summary>
public enum RichTextAdornmentPlacement
{
    /// <summary>Overlays the text at the source position without reserving inline space.</summary>
    Overlay,
    /// <summary>Uses the reserved left margin, aligned with the source position's visual line.</summary>
    LeftMargin,
    /// <summary>Reserves measured space in the text flow at the source anchor.</summary>
    Inline,
    /// <summary>Reserves a row before the anchor's logical source line.</summary>
    AboveLine,
    /// <summary>Reserves a row after the anchor's logical source line.</summary>
    BelowLine,
}

/// <summary>Controls the layout and insertion affinity of a source-anchored view.</summary>
public sealed record RichTextAdornmentOptions
{
    /// <summary>Gets where the content participates in layout.</summary>
    public RichTextAdornmentPlacement Placement { get; init; }
    /// <summary>Gets which side of an insertion retains the anchor.</summary>
    public RichTextTrackingAffinity Affinity { get; init; } = RichTextTrackingAffinity.AfterInsertion;
    /// <summary>Gets an overlay or margin displacement in device-independent units.</summary>
    public Point Offset { get; init; }
    /// <summary>Gets the inline baseline measured from the content's top; null aligns its bottom.</summary>
    public double? Baseline { get; init; }

    internal void Validate()
    {
        if (!Enum.IsDefined(Placement)) throw new ArgumentOutOfRangeException(nameof(Placement));
        RichTextTracking.Validate(Affinity, RichTextTrackingDeletionBehavior.Invalidate);
        if (!double.IsFinite(Offset.X) || !double.IsFinite(Offset.Y)) throw new ArgumentOutOfRangeException(nameof(Offset));
        if (Placement is not (RichTextAdornmentPlacement.Overlay or RichTextAdornmentPlacement.LeftMargin) && Offset != Point.Zero)
            throw new ArgumentException("A space-reserving adornment cannot have an offset.", nameof(Offset));
        if (Baseline is { } baseline && (Placement != RichTextAdornmentPlacement.Inline || !double.IsFinite(baseline) || baseline < 0))
            throw new ArgumentOutOfRangeException(nameof(Baseline));
    }
}

/// <summary>A view attached to a UTF-16 source position, outside the document and its history.</summary>
public sealed partial class RichTextAdornment : IDisposable
{
    private readonly RichTextAdornments _owner;
    private RichTextTrackedPosition _anchor;
    private RichTextAdornmentOptions _options;
    private int _lastPosition;

    internal RichTextAdornment(RichTextAdornments owner, RichTextTrackedPosition anchor, View view, RichTextAdornmentOptions options)
    {
        _owner = owner;
        _anchor = anchor;
        _lastPosition = anchor.Position!.Value;
        View = view;
        _options = options;
    }

    /// <summary>Gets the current source position, tracking edits with the configured insertion affinity.</summary>
    public int Position => _anchor.Position is { } position ? _lastPosition = position : _lastPosition;
    /// <summary>Gets the application-owned content, including its normal input and accessibility behavior.</summary>
    public View View { get; }
    /// <summary>Gets or sets the placement, sizing, and insertion-affinity options.</summary>
    public RichTextAdornmentOptions Options
    {
        get => _options;
        set => _owner.SetOptions(this, value);
    }
    internal bool IsValid => _anchor.Position is not null;
    internal bool ReservesSpace => Options.Placement is RichTextAdornmentPlacement.Inline or RichTextAdornmentPlacement.AboveLine or RichTextAdornmentPlacement.BelowLine;
    internal Size MeasuredSize { get; set; }
    internal bool MeasureDirty { get; set; } = true;
    internal bool IsRealized { get; set; }
    internal void SetOptions(RichTextTracking tracking, RichTextAdornmentOptions options)
    {
        var position = Position;
        if (options.Affinity != _options.Affinity)
        {
            _anchor.Dispose();
            _anchor = tracking.TrackPosition(position, options.Affinity);
        }
        _options = options;
        MeasureDirty = true;
    }
    internal void Release() { _lastPosition = Position; _anchor.Dispose(); }
    internal Rect LayoutBounds { get; set; } = new(-100000, -100000, 0, 0);
    /// <summary>Removes the adornment. Calling this more than once has no effect.</summary>
    public void Dispose() => _owner.Remove(this);
}

/// <summary>Owns source-anchored MAUI views for one editor.</summary>
/// <remarks>
/// Adding and removing views never changes document content, selection, saved state, or undo/redo history.
/// Anchors track text edits. Replacing or deleting text containing an anchor removes it; document replacement
/// clears all adornments. Undo does not recreate removed views. Hidden and offscreen anchors are not displayed.
/// Views are clipped to the editor viewport. Applications own content, actions, offsets, and overlap policy.
/// All mutations require the editor's UI thread.
/// </remarks>
public sealed partial class RichTextAdornments : IReadOnlyList<RichTextAdornment>
{
    private readonly RichEditor _editor;
    private readonly List<RichTextAdornment> _items = [];
    private double _marginWidth;
    internal RichTextAdornmentLayout Overlay { get; }
    internal long Version { get; private set; }

    internal RichTextAdornments(RichEditor editor)
    {
        _editor = editor;
        Overlay = new(this);
        editor.AddLogicalChild(Overlay);
    }

    /// <summary>Gets or sets the reserved left-margin width in device-independent units; the default is zero.</summary>
    public double MarginWidth
    {
        get => _marginWidth;
        set
        {
            _editor.VerifyAccess();
            if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_marginWidth == value) return;
            _marginWidth = value;
            Update();
        }
    }

    /// <inheritdoc />
    public int Count => _items.Count;
    /// <inheritdoc />
    public RichTextAdornment this[int index] => _items[index];

    /// <summary>Adds an unparented view at a UTF-16 source position, including the end of the document.</summary>
    /// <param name="position">The source anchor.</param>
    /// <param name="view">The view to display. Its measured size determines the adornment bounds.</param>
    /// <param name="options">Optional placement, insertion affinity, and sizing.</param>
    /// <returns>A handle that can be removed or disposed independently.</returns>
    public RichTextAdornment Add(int position, View view, RichTextAdornmentOptions? options = null)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, _editor.Document.Length);
        options ??= new();
        options.Validate();
        if (view.Parent is not null || ReferenceEquals(view, _editor))
            throw new ArgumentException("An adornment view must not already have a parent.", nameof(view));
        var item = new RichTextAdornment(this, _editor.Document.Tracking.TrackPosition(position, options.Affinity), view, options);
        _items.Add(item);
        Overlay.Own(view);
        view.MeasureInvalidated += OnMeasureInvalidated;
        Update();
        return item;
    }

    /// <summary>Adds a view only when the revision still addresses the current document.</summary>
    /// <param name="revision">The revision used to compute the anchor.</param>
    /// <param name="position">The source anchor.</param>
    /// <param name="view">Unparented application content.</param>
    /// <param name="adornment">The new handle, or null for a stale revision.</param>
    /// <param name="options">Optional placement, insertion affinity, and sizing.</param>
    /// <returns>Whether the view was attached.</returns>
    public bool TryAdd(RichTextRevision revision, int position, View view, out RichTextAdornment? adornment, RichTextAdornmentOptions? options = null)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(view);
        adornment = null;
        if (revision != _editor.Document.Revision) return false;
        adornment = Add(position, view, options);
        return true;
    }

    internal void SetOptions(RichTextAdornment item, RichTextAdornmentOptions options)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ObjectDisposedException.ThrowIf(!_items.Contains(item), item);
        if (item.Options == options) return;
        item.SetOptions(_editor.Document.Tracking, options);
        Update();
    }

    /// <summary>Removes one adornment without affecting other owners' views.</summary>
    /// <param name="adornment">The handle returned by <see cref="Add"/>.</param>
    /// <returns>Whether the adornment belonged to this collection.</returns>
    public bool Remove(RichTextAdornment adornment)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(adornment);
        if (!_items.Remove(adornment)) return false;
        adornment.Release();
        RemoveView(adornment.View);
        Update();
        return true;
    }

    /// <summary>Removes all adornments, preserving the configured margin width.</summary>
    public void Clear()
    {
        _editor.VerifyAccess();
        if (_items.Count == 0) return;
        foreach (var item in _items) { item.Release(); RemoveView(item.View); }
        _items.Clear();
        Update();
    }

    /// <inheritdoc />
    public IEnumerator<RichTextAdornment> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void MapThrough(RichTextChangeSet changes)
    {
        if (_items.Count == 0 || !changes.IsTextChanged) return;
        Version++;
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var item = _items[index];
            if (!item.IsValid)
            {
                _items.RemoveAt(index);
                item.Release();
                RemoveView(item.View);
            }
        }
    }

    private void RemoveView(View view)
    {
        view.MeasureInvalidated -= OnMeasureInvalidated;
        Overlay.Release(view);
        view.Handler?.DisconnectHandler();
    }

    private void OnMeasureInvalidated(object? sender, EventArgs args)
    {
        if (_items.Find(item => ReferenceEquals(item.View, sender)) is { } item) item.MeasureDirty = true;
        Update();
    }
    private void Update()
    {
        Version++;
        (_editor.Handler as RichEditorHandler)?.UpdateAdornments();
    }
}

// Position changes must only arrange the overlay. Invalidating its desired size would remeasure the
// native text control, which can scroll back to its caret while the user is scrolling elsewhere.
internal sealed partial class RichTextAdornmentLayout : Layout
{
    private readonly RichTextAdornments _adornments;

    internal RichTextAdornmentLayout(RichTextAdornments adornments)
    {
        _adornments = adornments;
        InputTransparent = true;
        CascadeInputTransparent = false;
        IsClippedToBounds = true;
    }

    protected override ILayoutManager CreateLayoutManager() => new AdornmentLayoutManager(this);

    internal void Own(View view) => AddLogicalChild(view);
    internal void Release(View view)
    {
        if (Contains(view)) Remove(view);
        else RemoveLogicalChild(view);
    }
    internal void SetRealized(RichTextAdornment item, bool realized)
    {
        if (item.IsRealized == realized) return;
        if (realized)
        {
            item.MeasureDirty = true;
            RemoveLogicalChild(item.View);
            var index = _adornments.TakeWhile(other => !ReferenceEquals(other, item)).Count(other => other.IsRealized && other.Position <= item.Position) +
                _adornments.SkipWhile(other => !ReferenceEquals(other, item)).Skip(1).Count(other => other.IsRealized && other.Position < item.Position);
            Insert(index, item.View);
        }
        else
        {
            Remove(item.View);
            AddLogicalChild(item.View);
            item.View.Handler?.DisconnectHandler();
        }
        item.IsRealized = realized;
    }

    internal void ArrangeViews()
    {
        foreach (var item in _adornments)
            if (item.IsRealized) ((IView)item.View).Arrange(item.LayoutBounds);
    }

    private sealed class AdornmentLayoutManager(RichTextAdornmentLayout layout) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
        public Size ArrangeChildren(Rect bounds)
        {
            layout.ArrangeViews();
            return bounds.Size;
        }
    }
}
