using RichEdit.Core;

namespace RichEdit.Maui;

/// <summary>Transient character appearance over a typed rich-text selection.</summary>
public sealed record RichEditorDecoration(RichEdit.Core.RichTextSelection Range, RichTextDecorationStyle Style);

/// <summary>A transient widget anchored to a rich paragraph, independent of flattened native offsets.</summary>
public sealed record RichEditorWidget(string Id, RichTextPoint Anchor, Size EstimatedSize, object? Data = null,
    RichEditorWidgetOptions? Options = null, string? AccessibleDescription = null, long ContentVersion = 0);

/// <summary>Rich widget placement in device-independent units. Insertion affinity comes from the rich anchor.</summary>
public sealed record RichEditorWidgetOptions
{
    /// <summary>Gets whether the widget overlays text, reserves a margin, or participates in native text flow.</summary>
    public RichTextAdornmentPlacement Placement { get; init; }
    /// <summary>Gets the offset for overlay or margin placement.</summary>
    public Point Offset { get; init; }
    /// <summary>Gets the baseline for inline placement, measured from the widget's top.</summary>
    public double? Baseline { get; init; }
    internal RichTextAdornmentOptions Native(CaretAffinity affinity) => new()
    {
        Placement = Placement, Offset = Offset, Baseline = Baseline,
        Affinity = affinity == CaretAffinity.Upstream ? PositionAffinity.BeforeInsertion : PositionAffinity.AfterInsertion,
    };
}

/// <summary>Captures the document identity and one producer's request generation.</summary>
public sealed class RichPresentationRequest
{
    internal RichPresentationRequest(object producer, DocumentRevision revision, long generation)
    { Producer = producer; Revision = revision; Generation = generation; }
    internal object Producer { get; }
    /// <summary>Gets the captured document revision.</summary>
    public DocumentRevision Revision { get; }
    /// <summary>Gets the producer generation, which also orders requests for unchanged content.</summary>
    public long Generation { get; }
}

/// <summary>Owns typed, nonpersistent presentation for one rich editor. Use on the editor dispatcher.</summary>
/// <remarks>Anchors follow structural address maps. Removing an anchored paragraph drops its presentation;
/// undo does not resurrect dropped presentation. Replacing the document clears every producer.</remarks>
public sealed class RichEditorPresentation
{
    private readonly RichEditor _editor;
    private readonly List<RichEditorDecorationLayer> _layers = [];
    private readonly List<RichEditorWidgetSource> _sources = [];
    private bool _queued;
    internal RichEditorPresentation(RichEditor editor)
    {
        _editor = editor;
        editor.NativeView.StructuredSurfacesChanged += (_, _) => Schedule();
    }
    internal RichDocumentSnapshot Snapshot => _editor.Document.CurrentSnapshot;
    internal void Verify() => _editor.NativeView.VerifyAccess();

    /// <summary>Creates a decoration layer. Higher priorities override lower priorities; ties follow creation order.</summary>
    public RichEditorDecorationLayer CreateDecorationLayer(int priority = 0)
    {
        Verify();
        var layer = new RichEditorDecorationLayer(this, priority);
        _layers.Add(layer);
        return layer;
    }

    /// <summary>Creates a lazy widget producer. Native reservations and visible child views use the containing text surface.</summary>
    public RichEditorWidgetSource CreateWidgetSource(Func<RichEditorWidget, View> create, Action<RichEditorWidget, View>? recycle = null)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(create);
        var source = new RichEditorWidgetSource(this, create, recycle);
        _sources.Add(source);
        return source;
    }

    internal void Changed(RichDocumentChangedEventArgs change)
    {
        foreach (var layer in _layers) layer.Map(change.Map);
        foreach (var source in _sources) source.Map(change.Map);
        Schedule();
    }
    internal void Reset()
    {
        foreach (var layer in _layers.ToArray()) layer.Clear();
        foreach (var source in _sources.ToArray()) source.Clear();
    }
    internal void Remove(RichEditorDecorationLayer layer) => _layers.Remove(layer);
    internal void Remove(RichEditorWidgetSource source) => _sources.Remove(source);
    internal void Schedule()
    {
        if (_queued) return;
        _queued = true;
        _editor.Dispatcher.Dispatch(() => { _queued = false; Refresh(); });
    }
    internal void Refresh()
    {
        Verify();
        var scoped = _editor.NativeView.StructuredSurfaces().ToArray();
        var surfaces = scoped.Length == 0 ? [_editor.NativeView] : scoped;
        foreach (var layer in _layers.ToArray()) layer.Render(surfaces);
        foreach (var source in _sources.ToArray()) source.Render(surfaces);
    }
}

/// <summary>A replaceable rich decoration layer with revision and producer-generation checks.</summary>
public sealed partial class RichEditorDecorationLayer : IDisposable
{
    private readonly RichEditorPresentation _owner;
    private readonly Dictionary<NativeEditorView, RichTextDecorationLayer> _native = [];
    private RichEditorDecoration[] _items = [];
    private long _generation;
    private bool _disposed;
    internal RichEditorDecorationLayer(RichEditorPresentation owner, int priority) { _owner = owner; Priority = priority; }
    /// <summary>Gets the layer's explicit precedence.</summary>
    public int Priority { get; }
    /// <summary>Captures a request and supersedes earlier computations from this layer.</summary>
    public RichPresentationRequest BeginUpdate()
    { Verify(); return new(this, _owner.Snapshot.Revision, checked(++_generation)); }
    /// <summary>Atomically replaces nonempty, nonoverlapping text ranges. Stale or disposed requests return false.</summary>
    public bool TryPublish(RichPresentationRequest request, IEnumerable<RichEditorDecoration> decorations)
    {
        _owner.Verify();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decorations);
        if (!Current(request)) return false;
        var items = decorations.ToArray();
        var spans = new List<TextSpan>();
        var projection = _owner.Snapshot.ProjectText();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Style);
            RichTextDocumentSnapshot.Validate(item.Style.Apply(RichTextCharacterFormat.Default));
            var ranges = _owner.Snapshot.GetSelectedText(item.Range);
            if (!ranges.Any(range => range.Length != 0)) throw new ArgumentException("Decoration ranges must contain authored text.", nameof(decorations));
            spans.AddRange(ranges.Where(range => range.Length != 0).Select(range => new TextSpan(projection.GetOffset(range.Start), range.Length)));
        }
        var end = -1;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            if (span.Start < end) throw new ArgumentException("Decoration ranges within one layer cannot overlap.", nameof(decorations));
            end = span.End;
        }
        if (!Current(request)) return false;
        _items = items;
        _owner.Refresh();
        return true;
    }
    private bool Current(RichPresentationRequest request) => !_disposed && ReferenceEquals(request.Producer, this) &&
        request.Generation == _generation && request.Revision == _owner.Snapshot.Revision;
    private void Verify() { _owner.Verify(); ObjectDisposedException.ThrowIf(_disposed, this); }
    internal void Map(RichAddressMap map) => _items = _items.Select(item =>
        map.Map(item.Range.Anchor) is { } anchor && map.Map(item.Range.Active) is { } active
            ? item with { Range = new(anchor, active) } : null).OfType<RichEditorDecoration>().ToArray();
    internal void Render(NativeEditorView[] surfaces)
    {
        foreach (var pair in _native.ToArray())
            if (!surfaces.Contains(pair.Key)) { pair.Value.Dispose(); _native.Remove(pair.Key); }
        foreach (var surface in surfaces)
        {
            if (surface.Document.StructuredProjection is not { } projection) continue;
            if (!_native.TryGetValue(surface, out var layer)) _native.Add(surface, layer = surface.Decorations.CreateLayer(Priority));
            var items = _items.SelectMany(item => _owner.Snapshot.GetSelectedText(item.Range)
                .Where(range => range.Length != 0 && projection.ContainsParagraph(range.Start.Paragraph))
                .Select(range => new RichTextDecoration(new(projection.GetOffset(range.Start), range.Length), item.Style)))
                .OrderBy(item => item.Range.Start).ToArray();
            layer.TrySet(surface.Document.Revision, items);
        }
    }
    /// <summary>Clears appearance and invalidates in-flight requests without disposing the layer.</summary>
    public void Clear() { Verify(); _generation++; _items = []; foreach (var layer in _native.Values) layer.Clear(); }
    /// <summary>Restores underlying appearance and releases this producer.</summary>
    public void Dispose()
    {
        _owner.Verify();
        if (_disposed) return;
        Clear(); _disposed = true;
        foreach (var layer in _native.Values) layer.Dispose();
        _native.Clear(); _owner.Remove(this);
    }
}

/// <summary>A typed rich widget producer. Factories run only near a native viewport; views recycle when their surface or anchor disappears.</summary>
public sealed partial class RichEditorWidgetSource : IDisposable
{
    private readonly RichEditorPresentation _owner;
    private readonly Func<RichEditorWidget, View> _create;
    private readonly Action<RichEditorWidget, View>? _recycle;
    private readonly Dictionary<NativeEditorView, RichTextWidgetSource> _native = [];
    private sealed class Binding(RichEditorWidget item) { internal RichEditorWidget Item = item; }
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);
    private RichEditorWidget[] _items = [];
    private long _generation;
    private bool _disposed;
    internal RichEditorWidgetSource(RichEditorPresentation owner, Func<RichEditorWidget, View> create, Action<RichEditorWidget, View>? recycle)
    { _owner = owner; _create = create; _recycle = recycle; }
    /// <summary>Captures a request and supersedes earlier computations from this producer.</summary>
    public RichPresentationRequest BeginUpdate() { Verify(); return new(this, _owner.Snapshot.Revision, checked(++_generation)); }
    /// <summary>Publishes unique stable IDs and validated rich anchors when the request remains current.</summary>
    public bool TryPublish(RichPresentationRequest request, IEnumerable<RichEditorWidget> widgets)
    {
        _owner.Verify();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(widgets);
        if (!Current(request)) return false;
        var items = widgets.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var projection = _owner.Snapshot.ProjectText();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) throw new ArgumentException("Widget IDs must be nonempty and unique.", nameof(widgets));
            _ = projection.GetOffset(item.Anchor);
            if (!double.IsFinite(item.EstimatedSize.Width) || !double.IsFinite(item.EstimatedSize.Height) || item.EstimatedSize.Width < 0 || item.EstimatedSize.Height < 0)
                throw new ArgumentException("Widget estimates must be finite and nonnegative.", nameof(widgets));
            (item.Options ?? new()).Native(item.Anchor.Affinity).Validate();
            if (item.Options?.Baseline > item.EstimatedSize.Height) throw new ArgumentException("The estimate must include the baseline.", nameof(widgets));
        }
        if (!Current(request)) return false;
        _items = items;
        _owner.Refresh();
        return true;
    }
    private bool Current(RichPresentationRequest request) => !_disposed && ReferenceEquals(request.Producer, this) &&
        request.Generation == _generation && request.Revision == _owner.Snapshot.Revision;
    private void Verify() { _owner.Verify(); ObjectDisposedException.ThrowIf(_disposed, this); }
    internal void Map(RichAddressMap map) => _items = _items.Select(item => map.Map(item.Anchor) is { } anchor
        ? item with { Anchor = anchor } : null).OfType<RichEditorWidget>().ToArray();
    internal void Render(NativeEditorView[] surfaces)
    {
        var ids = _items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _bindings.Keys.Where(id => !ids.Contains(id)).ToArray()) _bindings.Remove(id);
        foreach (var item in _items)
        {
            if (!_bindings.TryGetValue(item.Id, out var binding) || binding.Item with { Anchor = item.Anchor } != item)
                _bindings[item.Id] = new(item);
            else binding.Item = item;
        }
        foreach (var pair in _native.ToArray())
            if (!surfaces.Contains(pair.Key)) { pair.Value.Dispose(); _native.Remove(pair.Key); }
        foreach (var surface in surfaces)
        {
            if (surface.Document.StructuredProjection is not { } projection) continue;
            if (!_native.TryGetValue(surface, out var source)) _native.Add(surface, source = surface.Adornments.CreateSource(
                item => _create(((Binding)item.Data!).Item), (item, view) => _recycle?.Invoke(((Binding)item.Data!).Item, view)));
            var items = _items.Where(item => projection.ContainsParagraph(item.Anchor.Paragraph)).Select(item => new RichTextWidget(
                item.Id, projection.GetOffset(item.Anchor), item.EstimatedSize, _bindings[item.Id], (item.Options ?? new()).Native(item.Anchor.Affinity), item.AccessibleDescription, item.ContentVersion));
            source.TryPublish(source.BeginUpdate(), items);
        }
    }
    /// <summary>Removes all widgets and invalidates in-flight requests.</summary>
    public void Clear()
    {
        Verify(); _generation++; _items = [];
        foreach (var source in _native.Values) source.Dispose();
        _native.Clear(); _bindings.Clear();
    }
    /// <summary>Removes widgets, recycles live views, and releases this producer.</summary>
    public void Dispose()
    {
        _owner.Verify();
        if (_disposed) return;
        Clear(); _disposed = true; _owner.Remove(this);
    }
}
