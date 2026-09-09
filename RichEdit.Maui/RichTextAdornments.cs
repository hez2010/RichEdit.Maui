using System.Collections;
using Microsoft.Maui.Layouts;

namespace RichEdit.Maui;

/// <summary>Specifies where an adornment is displayed relative to its source position.</summary>
public enum RichTextAdornmentPlacement
{
    /// <summary>Overlays the text at the source position without reserving inline space.</summary>
    Text,
    /// <summary>Uses the reserved left margin, aligned with the source position's visual line.</summary>
    LeftMargin,
}

/// <summary>A view attached to a UTF-16 source position, outside the document and its history.</summary>
public sealed partial class RichTextAdornment : IDisposable
{
    private readonly RichTextAdornments _owner;

    internal RichTextAdornment(RichTextAdornments owner, int position, View view, RichTextAdornmentPlacement placement, Point offset)
    {
        _owner = owner;
        Position = position;
        View = view;
        Placement = placement;
        Offset = offset;
    }

    /// <summary>Gets the current source position. Insertions at this position move the anchor after the inserted text.</summary>
    public int Position { get; internal set; }
    /// <summary>Gets the application-owned content, including its normal input and accessibility behavior.</summary>
    public View View { get; }
    /// <summary>Gets the placement of this view.</summary>
    public RichTextAdornmentPlacement Placement { get; }
    /// <summary>Gets the displacement from the anchor in device-independent units.</summary>
    public Point Offset { get; }
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
    /// <param name="placement">Text overlay or reserved left margin.</param>
    /// <param name="offset">An optional displacement in device-independent units.</param>
    /// <returns>A handle that can be removed or disposed independently.</returns>
    public RichTextAdornment Add(int position, View view, RichTextAdornmentPlacement placement = RichTextAdornmentPlacement.Text, Point offset = default)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, _editor.Document.Length);
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        if (!double.IsFinite(offset.X) || !double.IsFinite(offset.Y)) throw new ArgumentOutOfRangeException(nameof(offset));
        if (view.Parent is not null || ReferenceEquals(view, _editor))
            throw new ArgumentException("An adornment view must not already have a parent.", nameof(view));
        var item = new RichTextAdornment(this, position, view, placement, offset);
        _items.Add(item);
        Overlay.Add(view);
        view.MeasureInvalidated += OnMeasureInvalidated;
        Update();
        return item;
    }

    /// <summary>Removes one adornment without affecting other owners' views.</summary>
    /// <param name="adornment">The handle returned by <see cref="Add"/>.</param>
    /// <returns>Whether the adornment belonged to this collection.</returns>
    public bool Remove(RichTextAdornment adornment)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(adornment);
        if (!_items.Remove(adornment)) return false;
        RemoveView(adornment.View);
        Update();
        return true;
    }

    /// <summary>Removes all adornments, preserving the configured margin width.</summary>
    public void Clear()
    {
        _editor.VerifyAccess();
        if (_items.Count == 0) return;
        foreach (var item in _items) RemoveView(item.View);
        _items.Clear();
        Update();
    }

    /// <inheritdoc />
    public IEnumerator<RichTextAdornment> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void MapThrough(RichTextChangeSet changes)
    {
        if (_items.Count == 0 || !changes.IsTextChanged) return;
        if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset)) { Clear(); return; }
        foreach (var change in changes.Changes.OfType<RichTextTextChange>())
        {
            for (var index = _items.Count - 1; index >= 0; index--)
            {
                var item = _items[index];
                if (change.OldRange.Start <= item.Position && item.Position < change.OldRange.End)
                {
                    _items.RemoveAt(index);
                    RemoveView(item.View);
                }
                else item.Position = RichTextSelectionState.MapOffset(item.Position, change);
            }
        }
    }

    private void RemoveView(View view)
    {
        view.MeasureInvalidated -= OnMeasureInvalidated;
        Overlay.Remove(view);
        view.Handler?.DisconnectHandler();
    }

    private void OnMeasureInvalidated(object? sender, EventArgs args) => Update();
    private void Update() => (_editor.Handler as RichEditorHandler)?.UpdateAdornments();
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

    internal void ArrangeViews()
    {
        foreach (var item in _adornments) ((IView)item.View).Arrange(item.LayoutBounds);
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
