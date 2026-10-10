namespace RichEdit.Maui;

/// <summary>A data-backed, source-anchored widget. Its identity is stable within one widget source.</summary>
/// <param name="Id">The source-owned stable identity.</param>
/// <param name="Position">The captured UTF-16 source anchor.</param>
/// <param name="EstimatedSize">A finite, nonnegative layout estimate in device-independent units.</param>
/// <param name="Data">Application data passed to creation and recycling callbacks.</param>
/// <param name="Options">Placement and insertion affinity.</param>
/// <param name="AccessibleDescription">An optional native accessibility description for the created view.</param>
/// <param name="ContentVersion">An application generation that forces recreation when data changes in place.</param>
public sealed record RichTextWidget(string Id, int Position, Size EstimatedSize, object? Data = null,
    RichTextAdornmentOptions? Options = null, string? AccessibleDescription = null, long ContentVersion = 0);

/// <summary>A document revision and producer generation captured before computing widget data.</summary>
public sealed class RichTextWidgetRequest
{
    internal RichTextWidgetRequest(RichTextWidgetSource source, DocumentRevision revision, long generation)
    { Source = source; DocumentRevision = revision; Generation = generation; }
    internal RichTextWidgetSource Source { get; }
    /// <summary>Gets the captured source revision.</summary>
    public DocumentRevision DocumentRevision { get; }
    /// <summary>Gets this producer's monotonically increasing request generation.</summary>
    public long Generation { get; }
}

/// <summary>Owns a replaceable widget set with lazy view creation and revision-checked publication.</summary>
/// <remarks>Callbacks run on the UI thread. Offscreen views are detached before recycling; applications may retain them in a bounded pool.
/// Unchanged stable IDs retain their live views and measured sizes. Document replacement clears handles; publish fresh data for the new document.</remarks>
public sealed partial class RichTextWidgetSource : IDisposable
{
    private readonly RichTextAdornments _owner;
    private readonly Func<RichTextWidget, View> _create;
    private readonly Action<RichTextWidget, View>? _recycle;
    private readonly Dictionary<string, (RichTextWidget Widget, RichTextAdornment Handle)> _items = new(StringComparer.Ordinal);
    private long _generation;
    private bool _disposed;
    internal RichTextWidgetSource(RichTextAdornments owner, Func<RichTextWidget, View> create, Action<RichTextWidget, View>? recycle)
    { _owner = owner; _create = create; _recycle = recycle; }

    /// <summary>Supersedes earlier computations from this producer, including requests for unchanged source text.</summary>
    public RichTextWidgetRequest BeginUpdate()
    {
        Verify();
        return new(this, _owner.Editor.Document.Revision, checked(++_generation));
    }

    /// <summary>Publishes a complete producer-owned set when its document revision and request generation are still current.</summary>
    /// <remarks>Validate and copy descriptor data before updating handles. Factories are not invoked by publication.</remarks>
    public bool TryPublish(RichTextWidgetRequest request, IEnumerable<RichTextWidget> widgets)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(widgets);
        if (!Current(request)) return false;
        var next = new Dictionary<string, RichTextWidget>(StringComparer.Ordinal);
        foreach (var widget in widgets)
        {
            ArgumentNullException.ThrowIfNull(widget);
            if (string.IsNullOrEmpty(widget.Id) || !next.TryAdd(widget.Id, widget)) throw new ArgumentException("Widget identities must be nonempty and unique within their source.", nameof(widgets));
            new TextSpan(widget.Position, 0).Validate(_owner.Editor.Document.Length);
            if (!double.IsFinite(widget.EstimatedSize.Width) || !double.IsFinite(widget.EstimatedSize.Height) ||
                widget.EstimatedSize.Width < 0 || widget.EstimatedSize.Height < 0) throw new ArgumentException("Widget estimates must be finite and nonnegative.", nameof(widgets));
            (widget.Options ?? new()).Validate();
            if (widget.Options?.Baseline > widget.EstimatedSize.Height) throw new ArgumentException("The estimated height must include the inline baseline.", nameof(widgets));
        }
        if (!Current(request)) return false;
        foreach (var pair in _items.ToArray())
        {
            if (next.TryGetValue(pair.Key, out var widget) && pair.Value.Handle.IsValid && pair.Value.Handle.Position == widget.Position &&
                pair.Value.Widget with { Position = widget.Position } == widget)
            { next.Remove(pair.Key); continue; }
            pair.Value.Handle.Dispose();
            _items.Remove(pair.Key);
        }
        foreach (var widget in next.Values)
        {
            var handle = _owner.AddWidget(widget, () => _create(widget), view => _recycle?.Invoke(widget, view));
            _items.Add(widget.Id, (widget, handle));
        }
        return true;
    }

    private bool Current(RichTextWidgetRequest request) => ReferenceEquals(request.Source, this) && request.Generation == _generation && request.DocumentRevision == _owner.Editor.Document.Revision;
    private void Verify() { _owner.Editor.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }

    /// <summary>Removes this producer's widgets and recycles created views without changing source or history.</summary>
    public void Dispose()
    {
        _owner.Editor.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        foreach (var item in _items.Values) item.Handle.Dispose();
        _items.Clear();
    }
}
