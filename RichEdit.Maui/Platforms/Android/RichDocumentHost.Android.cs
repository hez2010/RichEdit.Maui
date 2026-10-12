using Android.Content;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using RichEdit.Core;
using NativeView = Android.Views.View;
using NativeGrid = Android.Widget.GridLayout;

namespace RichEdit.Maui;

internal sealed class RichDocumentHostView(RichEditor owner) : Microsoft.Maui.Controls.View
{
    internal RichEditor Owner { get; } = owner;
}

internal sealed class RichDocumentHostHandler : ViewHandler<RichDocumentHostView, RichDocumentHost>
{
    public RichDocumentHostHandler() : base(ViewMapper) { }
    protected override RichDocumentHost CreatePlatformView() => new(Context, VirtualView.Owner, MauiContext!);
    protected override void ConnectHandler(RichDocumentHost platformView) { base.ConnectHandler(platformView); platformView.Connect(); }
    protected override void DisconnectHandler(RichDocumentHost platformView) { platformView.Disconnect(); base.DisconnectHandler(platformView); }
}

// Android has no RTF table editor. Keep editing and layout in native controls:
// a scroll host, native grids, and native editable text flows over one rich session.
internal sealed class RichDocumentHost : Android.Widget.ScrollView
{
    private readonly RichEditor _owner;
    private readonly NativeEditorView _coordinator;
    private readonly IMauiContext _context;
    private readonly Dictionary<RichTextViewScope, NativeEditorView> _surfaces = [];
    private readonly Dictionary<RichNodeId, Android.Widget.FrameLayout> _cells = [];
    private readonly Dictionary<RichNodeId, GradientDrawable> _cellBackgrounds = [];
    private readonly Dictionary<RichNodeId, RichCellFormat> _paintedCellFormats = [];
    private HashSet<RichNodeId>? _selectedCells;
    private (int Selection, int Border)? _cellColors;
    private readonly Dictionary<RichNodeId, RichTable> _tables = [];
    private readonly Dictionary<RichNodeId, LinearLayout> _containers = [];
    private readonly Dictionary<RichNodeId, (LinearLayout Row, TextView Marker)> _listRows = [];
    private readonly Dictionary<RichNodeId, (RichTableScrollView Scroll, RichNativeTableGrid Grid)> _nativeTables = [];
    private LinearLayout? _body;
    private bool _connected;
    private bool _queued;
    private RichDocumentSnapshot? _shape;

    internal RichDocumentHost(Context context, RichEditor owner, IMauiContext mauiContext) : base(context)
    {
        _owner = owner; _coordinator = owner.NativeView; _context = mauiContext;
        FillViewport = true;
        SetClipToPadding(true);
        // A native scroll viewport must contain its child flows even when the
        // surrounding MAUI layout allows descendants to draw outside its bounds.
        OutlineProvider = ViewOutlineProvider.Bounds;
        ClipToOutline = true;
    }

    internal void Connect()
    {
        if (_connected) return;
        _connected = true;
        _owner.ContentChanged += OnContentChanged;
        _owner.DocumentChanged += OnDocumentReplaced;
        _owner.SelectionChanged += OnSelectionChanged;
        _owner.CompositionChanged += OnCompositionChanged;
        _coordinator.PropertyChanged += OnAppearanceChanged;
        Rebuild();
    }
    internal void Disconnect()
    {
        if (!_connected) return;
        _connected = false;
        _owner.ContentChanged -= OnContentChanged;
        _owner.DocumentChanged -= OnDocumentReplaced;
        _owner.SelectionChanged -= OnSelectionChanged;
        _owner.CompositionChanged -= OnCompositionChanged;
        _coordinator.PropertyChanged -= OnAppearanceChanged;
        foreach (var surface in _surfaces.Values) Release(surface);
        _surfaces.Clear(); ClearCellBackgrounds(); _selectedCells = null; _cellColors = null; _tables.Clear();
        _containers.Clear(); _listRows.Clear(); _nativeTables.Clear();
        RemoveAllViews(); _body = null; _shape = null;
    }
    private void OnContentChanged(object? sender, RichDocumentChangedEventArgs args)
    {
        QueueRebuild();
    }
    private void OnDocumentReplaced(object? sender, RichDocumentReplacedEventArgs args) { _shape = null; QueueRebuild(); }
    private void OnSelectionChanged(object? sender, EventArgs args) { PaintCellSelection(); }
    private void OnCompositionChanged(object? sender, EventArgs args) { if (_owner.Composition is null) QueueRebuild(); }
    private void OnAppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(NativeEditorView.FontFamily) or nameof(NativeEditorView.FontSize) or nameof(NativeEditorView.TextColor) or
            nameof(NativeEditorView.IsReadOnly) or nameof(NativeEditorView.IsSpellCheckEnabled) or nameof(NativeEditorView.IsTextPredictionEnabled) or
            nameof(NativeEditorView.Keyboard) or nameof(NativeEditorView.AcceptsTab) or nameof(NativeEditorView.MaxLength))) return;
        foreach (var surface in _surfaces.Values) CopyAppearance(surface);
        PaintCellSelection();
    }
    private void QueueRebuild()
    {
        if (!_connected || _queued) return;
        _queued = true;
        Post(() => { _queued = false; if (_connected && _owner.Composition is null) Rebuild(); });
    }
    // Compare only native containers. Inline runs and paragraph text cannot change
    // the host topology; never sort or stringify the complete document on typing.
    private static bool SameStructure(RichDocumentSnapshot before, RichDocumentSnapshot after, RichNodeId id)
    {
        if (!before.Nodes.TryGetValue(id, out var first) || !after.Nodes.TryGetValue(id, out var second) || first.GetType() != second.GetType()) return false;
        if (first is RichParagraph) return true;
        if (first.Parent != second.Parent) return false;
        if (first is RichTable table && second is RichTable nextTable && !table.Columns.SequenceEqual(nextTable.Columns)) return false;
        if (first is RichTableCell cell && second is RichTableCell nextCell &&
            (cell.Column != nextCell.Column || cell.ColumnSpan != nextCell.ColumnSpan || cell.RowSpan != nextCell.RowSpan)) return false;
        if (first is RichList list && second is RichList nextList &&
            (list.Numbered != nextList.Numbered || list.NumberStyle != nextList.NumberStyle || list.StartNumber != nextList.StartNumber)) return false;
        using var oldChildren = RichDocumentSnapshot.Children(first).Where(child => before.Get<RichNode>(child) is not RichParagraph).GetEnumerator();
        using var newChildren = RichDocumentSnapshot.Children(second).Where(child => after.Get<RichNode>(child) is not RichParagraph).GetEnumerator();
        while (oldChildren.MoveNext())
            if (!newChildren.MoveNext() || oldChildren.Current != newChildren.Current || !SameStructure(before, after, oldChildren.Current)) return false;
        return !newChildren.MoveNext();
    }

    private void Rebuild()
    {
        if (_owner.Composition is not null) return;
        var snapshot = _owner.Document.CurrentSnapshot;
        if (_shape is { } previousShape && previousShape.Body == snapshot.Body && SameStructure(previousShape, snapshot, snapshot.Body))
        { _shape = snapshot; RequestLayout(); PaintCellSelection(); return; }
        using var selectionUpdate = _coordinator.BeginStructuredSurfaceUpdate();
        var focused = FindFocus() is not null;
        var used = new HashSet<RichTextViewScope>();
        var nodes = new HashSet<RichNodeId>();
        _selectedCells = null; _tables.Clear();
        var body = Container(snapshot.Body, used);
        if (!ReferenceEquals(body, _body))
        {
            RemoveAllViews();
            AddView(body, new Android.Widget.ScrollView.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));
        }
        _body = body; _shape = snapshot;
        foreach (var pair in _surfaces.Where(pair => !used.Contains(pair.Key)).ToArray()) { Release(pair.Value); _surfaces.Remove(pair.Key); }
        foreach (var id in _containers.Keys.Where(id => !nodes.Contains(id)).ToArray()) _containers.Remove(id);
        foreach (var id in _listRows.Keys.Where(id => !nodes.Contains(id)).ToArray()) _listRows.Remove(id);
        foreach (var id in _nativeTables.Keys.Where(id => !nodes.Contains(id)).ToArray()) _nativeTables.Remove(id);
        foreach (var id in _cells.Keys.Where(id => !nodes.Contains(id)).ToArray())
        {
            _cells[id].Background = null; _cells.Remove(id);
            _cellBackgrounds[id].Dispose(); _cellBackgrounds.Remove(id); _paintedCellFormats.Remove(id);
        }
        PaintCellSelection();
        // A new empty list item has no focusable bounds until its first layout.
        if (focused)
            AndroidX.Core.View.OneShotPreDrawListener.Add(this, new Java.Lang.Runnable(() =>
            {
                if (_connected) _coordinator.FocusStructuredSelection();
            }));

        LinearLayout Container(RichNodeId parent, HashSet<RichTextViewScope> scopes)
        {
            nodes.Add(parent);
            if (!_containers.TryGetValue(parent, out var stack))
                _containers.Add(parent, stack = new LinearLayout(Context) { Orientation = Orientation.Vertical });
            var children = new List<(NativeView, ViewGroup.LayoutParams)>();
            var blocks = RichDocumentSnapshot.Children(snapshot.Get<RichNode>(parent)).ToArray();
            RichNodeId? previous = null;
            foreach (var block in blocks.Where(id => snapshot.Get<RichNode>(id) is RichTable or RichList))
            {
                children.Add((Flow(new(parent, previous, block), scopes), new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)));
                if (snapshot.Get<RichNode>(block) is RichTable table)
                    children.Add((Table(table, scopes), new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)));
                else
                {
                    var list = snapshot.Get<RichList>(block);
                    for (var index = 0; index < list.Items.Count; index++)
                    {
                        var item = list.Items[index]; nodes.Add(item);
                        if (!_listRows.TryGetValue(item, out var nativeRow))
                        {
                            nativeRow = (new LinearLayout(Context) { Orientation = Orientation.Horizontal },
                                new TextView(Context) { Gravity = GravityFlags.Top | GravityFlags.End });
                            _listRows.Add(item, nativeRow);
                        }
                        var (row, marker) = nativeRow;
                        var label = list.Numbered ? RichTextListFormatter.FormatNumber(list.StartNumber + index, (RichListNumberStyle)list.NumberStyle) + "." : "•";
                        if (marker.Text != label) marker.Text = label;
                        marker.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)(_owner.FontSize ?? 14));
                        marker.SetPadding(0, Pixels(4), Pixels(4), 0);
                        ReconcileChildren(row, new (NativeView, ViewGroup.LayoutParams)[]
                        {
                            (marker, new LinearLayout.LayoutParams(Pixels(28), LayoutParams.WrapContent)),
                            (Container(item, scopes), new LinearLayout.LayoutParams(0, LayoutParams.WrapContent, 1)),
                        });
                        children.Add((row, new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)));
                    }
                }
                previous = block;
            }
            children.Add((Flow(new(parent, previous, null), scopes), new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)));
            ReconcileChildren(stack, children);
            return stack;
        }
        NativeView Table(RichTable table, HashSet<RichTextViewScope> scopes)
        {
            _tables.Add(table.Id, table);
            nodes.Add(table.Id);
            if (!_nativeTables.TryGetValue(table.Id, out var nativeTable))
            {
                nativeTable = (new RichTableScrollView(Context!, _coordinator) { HorizontalScrollBarEnabled = true },
                    new RichNativeTableGrid(Context!) { UseDefaultMargins = false });
                nativeTable.Scroll.AddView(nativeTable.Grid, new HorizontalScrollView.LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent));
                _nativeTables.Add(table.Id, nativeTable);
            }
            var grid = nativeTable.Grid;
            grid.ColumnCount = Math.Max(grid.ColumnCount, table.Columns.Length); grid.RowCount = Math.Max(grid.RowCount, table.Rows.Count);
            var children = new List<(NativeView, ViewGroup.LayoutParams)>();
            for (var row = 0; row < table.Rows.Count; row++)
                foreach (var cellId in snapshot.Get<RichTableRow>(table.Rows[row]).Cells)
                {
                    var cell = snapshot.Get<RichTableCell>(cellId);
                    nodes.Add(cell.Id);
                    if (!_cells.TryGetValue(cell.Id, out var frame))
                    {
                        _cells.Add(cell.Id, frame = new Android.Widget.FrameLayout(Context!));
                        var background = new RichCellDrawable(Resources?.DisplayMetrics?.Density ?? 1);
                        frame.Background = background; _cellBackgrounds.Add(cell.Id, background);
                    }
                    ReconcileChildren(frame, new (NativeView, ViewGroup.LayoutParams)[] { (Container(cell.Id, scopes), new Android.Widget.FrameLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)) });
                    var parameters = new NativeGrid.LayoutParams(NativeGrid.InvokeSpec(row, cell.RowSpan, NativeGrid.Fill),
                        NativeGrid.InvokeSpec(cell.Column, cell.ColumnSpan, NativeGrid.Fill))
                    { Width = Pixels(table.Columns.Skip(cell.Column).Take(cell.ColumnSpan).Sum(width => width.Points)), Height = LayoutParams.WrapContent };
                    children.Add((frame, parameters));
                }
            ReconcileChildren(grid, children);
            grid.ColumnCount = table.Columns.Length; grid.RowCount = table.Rows.Count;
            return nativeTable.Scroll;
        }
    }

    // Keep surviving native controls attached. Rebuilding their ancestors would
    // discard native focus and scroll anchors even for an unrelated row edit.
    private static void ReconcileChildren(ViewGroup parent, IReadOnlyList<(NativeView View, ViewGroup.LayoutParams Parameters)> children)
    {
        var retained = children.Select(child => child.View).ToHashSet(ReferenceEqualityComparer.Instance);
        for (var index = parent.ChildCount - 1; index >= 0; index--)
            if (!retained.Contains(parent.GetChildAt(index)!)) parent.RemoveViewAt(index);
        for (var index = 0; index < children.Count; index++)
        {
            var (view, parameters) = children[index];
            if (ReferenceEquals(parent.GetChildAt(index), view)) view.LayoutParameters = parameters;
            else
            {
                (view.Parent as ViewGroup)?.RemoveView(view);
                parent.AddView(view, index, parameters);
            }
        }
        while (parent.ChildCount > children.Count) parent.RemoveViewAt(parent.ChildCount - 1);
    }

    private NativeView Flow(RichTextViewScope scope, HashSet<RichTextViewScope> used)
    {
        used.Add(scope);
        if (_surfaces.TryGetValue(scope, out var previous) && !ReferenceEquals(previous.StructuredSession, _coordinator.StructuredSession))
        { Release(previous); _surfaces.Remove(scope); }
        if (!_surfaces.TryGetValue(scope, out var surface))
        {
            surface = new NativeEditorView { AutoSize = EditorAutoSizeOption.TextChanges };
            CopyAppearance(surface);
            surface.BindStructuredScope(_owner.Document, scope, _coordinator);
            _surfaces.Add(scope, surface);
            surface.StructuredInputBoundsProvider = () => SurfaceBounds(surface);
        }
        var native = surface.ToPlatform(_context);
        if (surface.Handler is RichEditorHandler handler)
        {
            handler.PlatformView.Background = null;
            handler.PlatformView.SetMinHeight(0);
            handler.PlatformView.SetPadding(Pixels(4), Pixels(4), Pixels(4), Pixels(4));
            handler.PlatformView.StructuredTableCell = TableCellContext(scope.Root);
        }
        return native;
    }
    private void CopyAppearance(NativeEditorView surface)
    {
        surface.FontFamily = _coordinator.FontFamily; surface.FontSize = _coordinator.FontSize;
        surface.TextColor = _coordinator.TextColor; surface.IsReadOnly = _coordinator.IsReadOnly;
        surface.IsSpellCheckEnabled = _coordinator.IsSpellCheckEnabled;
        surface.IsTextPredictionEnabled = _coordinator.IsTextPredictionEnabled;
        surface.Keyboard = _coordinator.Keyboard; surface.AcceptsTab = _coordinator.AcceptsTab;
        surface.MaxLength = _coordinator.MaxLength;
    }
    private RichNativeTableCell? TableCellContext(RichNodeId root)
    {
        var snapshot = _owner.Document.CurrentSnapshot;
        for (var node = snapshot.Get<RichNode>(root); ; node = snapshot.Get<RichNode>(node.Parent!.Value))
        {
            if (node is RichTableCell cell)
            {
                var row = snapshot.Get<RichTableRow>(cell.Parent!.Value);
                var table = snapshot.Get<RichTable>(row.Parent!.Value);
                return new(table.Id, cell.Id, table.Rows.IndexOf(row.Id), cell.RowSpan, cell.Column, cell.ColumnSpan);
            }
            if (node.Parent is null) return null;
        }
    }
    private void Release(NativeEditorView surface)
    {
        surface.StructuredInputBoundsProvider = null;
        surface.Handler = null;
        surface.Document = new RichTextDocument();
    }
    private void PaintCellSelection()
    {
        var selected = new HashSet<RichNodeId>();
        if (_owner.Selection is RichCellSelection selection && _tables.ContainsKey(selection.Table))
        {
            var grid = _owner.Document.CurrentSnapshot.GetTableGrid(selection.Table);
            for (var row = selection.Row; row < selection.Row + selection.RowCount; row++)
                for (var column = selection.Column; column < selection.Column + selection.ColumnCount; column++) selected.Add(grid[row, column]);
        }
        var nativeText = _surfaces.Values.Select(surface => (surface.Handler as RichEditorHandler)?.PlatformView).FirstOrDefault(view => view is not null);
        var foreground = new Android.Graphics.Color(nativeText?.CurrentTextColor ?? Android.Graphics.Color.Black.ToArgb());
        var colors = (Selection: nativeText?.HighlightColor ?? Android.Graphics.Color.Transparent.ToArgb(),
            Border: Android.Graphics.Color.Argb(112, foreground.R, foreground.G, foreground.B).ToArgb());
        var snapshot = _owner.Document.CurrentSnapshot;
        if (_selectedCells?.SetEquals(selected) == true && _cellColors == colors &&
            _cells.Keys.All(id => snapshot.Nodes.GetValueOrDefault(id) is RichTableCell cell && _paintedCellFormats.GetValueOrDefault(id) == cell.Format)) return;
        _selectedCells = selected;
        _cellColors = colors;
        foreach (var pair in _cellBackgrounds)
        {
            if (snapshot.Nodes.GetValueOrDefault(pair.Key) is not RichTableCell cell) continue;
            var format = cell.Format;
            var frame = _cells[pair.Key];
            if (_paintedCellFormats.GetValueOrDefault(pair.Key) != format)
            {
                frame.SetPadding(Pixels(format.Padding.Left.Points), Pixels(format.Padding.Top.Points), Pixels(format.Padding.Right.Points), Pixels(format.Padding.Bottom.Points));
                if (frame.GetChildAt(0)?.LayoutParameters is Android.Widget.FrameLayout.LayoutParams parameters)
                {
                    parameters.Gravity = format.VerticalAlignment switch
                    { RichCellVerticalAlignment.Center => GravityFlags.CenterVertical, RichCellVerticalAlignment.Bottom => GravityFlags.Bottom, _ => GravityFlags.Top };
                    frame.GetChildAt(0)!.LayoutParameters = parameters;
                }
            }
            ((RichCellDrawable)pair.Value).Configure(format, selected.Contains(pair.Key) ? colors.Selection : null, foreground.ToArgb(), colors.Border);
            _paintedCellFormats[pair.Key] = format;
        }
    }
    private void ClearCellBackgrounds()
    {
        foreach (var frame in _cells.Values) frame.Background = null;
        foreach (var background in _cellBackgrounds.Values) background.Dispose();
        _cells.Clear(); _cellBackgrounds.Clear(); _paintedCellFormats.Clear();
    }
    protected override void OnConfigurationChanged(Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _cellColors = null;
        Post(() => { if (_connected) PaintCellSelection(); });
    }
    private int Pixels(double value) => checked((int)Math.Ceiling(value * (Resources?.DisplayMetrics?.Density ?? 1)));
    private Rect SurfaceBounds(NativeEditorView surface)
    {
        if (surface.Handler?.PlatformView is not NativeView native) return Rect.Zero;
        var origin = new int[2]; var location = new int[2];
        GetLocationOnScreen(origin); native.GetLocationOnScreen(location);
        var density = Resources?.DisplayMetrics?.Density ?? 1;
        return new((location[0] - origin[0]) / density, (location[1] - origin[1]) / density, native.Width / density, native.Height / density);
    }
    protected override void OnScrollChanged(int left, int top, int oldLeft, int oldTop)
    {
        base.OnScrollChanged(left, top, oldLeft, oldTop);
        _coordinator.TextLayout.Invalidate();
    }
    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        base.OnLayout(changed, left, top, right, bottom);
        if (changed) _coordinator.TextLayout.Invalidate();
        var origin = new int[2]; GetLocationOnScreen(origin);
        foreach (var surface in _surfaces.Values)
            if (surface.Handler?.PlatformView is NativeView native)
            {
                var location = new int[2]; native.GetLocationOnScreen(location);
                var density = Resources?.DisplayMetrics?.Density ?? 1;
                surface.StructuredInputOffset = new((location[0] - origin[0]) / density, (location[1] - origin[1]) / density);
            }
    }


}

internal sealed class RichTableScrollView(Context context, NativeEditorView coordinator) : HorizontalScrollView(context)
{
    private readonly WeakReference<NativeEditorView> _coordinator = new(coordinator);
    protected override void OnScrollChanged(int left, int top, int oldLeft, int oldTop)
    {
        base.OnScrollChanged(left, top, oldLeft, oldTop);
        if (_coordinator.TryGetTarget(out var view)) view.TextLayout.Invalidate();
    }
}

internal sealed record RichNativeTableCell(RichNodeId Table, RichNodeId Cell, int Row, int RowSpan, int Column, int ColumnSpan);

internal sealed class RichNativeTableGrid(Context context) : NativeGrid(context)
{
    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            using var builder = new AccessibilityNodeInfo.CollectionInfo.Builder();
            builder.SetRowCount(RowCount); builder.SetColumnCount(ColumnCount); builder.SetHierarchical(false);
            builder.SetSelectionMode((int)Android.Views.Accessibility.SelectionMode.Multiple);
            info.SetCollectionInfo(builder.Build());
        }
        else
        {
            // CollectionInfo.Builder is available from API 35. The public obtain
            // factory is required on earlier supported Android releases.
#pragma warning disable CA1422
            info.SetCollectionInfo(AccessibilityNodeInfo.CollectionInfo.Obtain(RowCount, ColumnCount, false, Android.Views.Accessibility.SelectionMode.Multiple));
#pragma warning restore CA1422
        }
    }
}
