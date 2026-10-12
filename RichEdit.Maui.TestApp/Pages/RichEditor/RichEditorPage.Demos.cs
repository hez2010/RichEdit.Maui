using RichEdit.Core;
using CoreTextSelection = RichEdit.Core.RichTextSelection;

namespace RichEdit.Maui.TestApp;

public partial class RichEditorPage
{
    private ToolTarget? _capturedEdit;
    private CoreTextSelection? _trackedRange;
    private RichTextPoint? _bookmark;
    private RichEditorDecorationLayer? _capturedHighlight, _trackingHighlight;
    private RichEditorWidgetSource? _widgetSource;
    private readonly List<RichEditorWidget> _widgets = [];
    private RichEditorLayoutSnapshot? _capturedLayout;
    private RichTextPoint? _capturedCaret;
    private int _widgetNumber;
    private bool _showGeometry;
    private (Rect Rectangle, Color Color)[] _geometryRectangles = [];

    private void CaptureEdit()
    {
        _capturedEdit = CaptureTarget();
        ResultLabel.Text = "Captured the current selection and revision. Edit elsewhere before Apply edit to see stale work rejected.";
    }
    private void ApplyCapturedEdit()
    {
        if (_capturedEdit is not { } capture || !ReferenceEquals(capture.Document, Editor.Document)) { ResultLabel.Text = "Capture an edit first."; return; }
        var result = Editor.Edit(edit => edit.SetSelection(edit.ReplaceSelection(capture.Selection, _replacementEntry?.Text ?? "updated")),
            new("Checked edit"), capture.Revision, capture.Generation);
        ResultLabel.Text = result.Accepted ? "Applied as one undoable edit." : $"Edit rejected: {result.Rejection}.";
    }
    private void HighlightCapture()
    {
        if (_capturedEdit is not { } capture || !IsCurrent(capture)) return;
        PublishHighlight(ref _capturedHighlight, capture.Selection, Colors.Gold);
    }
    private void HighlightSelection() => PublishHighlight(ref _capturedHighlight, Editor.Selection, Colors.Gold);
    private void PublishHighlight(ref RichEditorDecorationLayer? layer, RichSelection selection, Color color)
    {
        var ranges = Editor.Document.CurrentSnapshot.GetSelectedText(selection).Where(range => range.Length > 0).ToArray();
        if (ranges.Length == 0) { ResultLabel.Text = "Select text to highlight."; return; }
        layer ??= Editor.Presentation.CreateDecorationLayer();
        var request = layer.BeginUpdate();
        var accepted = layer.TryPublish(request, ranges.Select(range => new RichEditorDecoration(new(range.Start, range.Start with { Offset = range.Start.Offset + range.Length }), new() { BackgroundColor = color })));
        ResultLabel.Text = accepted ? "Presentation highlight added; document and undo history are unchanged." : "The presentation request is stale.";
    }
    private CoreTextSelection? PrimaryTextSelection() => Editor.Selection switch { CoreTextSelection text => text, RichTextSelectionSet set => set.Primary.Selection, _ => null };
    private void TrackSelection()
    {
        _trackedRange = PrimaryTextSelection();
        if (_trackedRange is null) { ResultLabel.Text = "Select text first."; return; }
        if (Editor.Document.CurrentSnapshot.GetSelectedText(_trackedRange).Any(range => range.Length > 0)) PublishHighlight(ref _trackingHighlight, _trackedRange, Colors.LightSkyBlue);
        ResultLabel.Text = "Tracking the selection through edits and undo.";
    }
    private void SelectTracked()
    {
        if (_trackedRange is null) { ResultLabel.Text = "There is no surviving tracked selection."; return; }
        Editor.Selection = _trackedRange;
        Editor.TextLayout.ScrollIntoView(_trackedRange.Active);
        Editor.Focus();
    }
    private void BookmarkCaret() { _bookmark = ActivePoint(); ResultLabel.Text = _bookmark is null ? "Place a text caret first." : "A transient bookmark now follows this caret address."; }
    private void GoToBookmark()
    {
        if (_bookmark is not { } point) { ResultLabel.Text = "There is no surviving caret bookmark."; return; }
        Editor.Selection = new CoreTextSelection(point, point);
        Editor.TextLayout.ScrollIntoView(point);
        Editor.Focus();
    }
    private void ClearTracking() { _trackedRange = null; _bookmark = null; _trackingHighlight?.Clear(); ResultLabel.Text = "Tracking cleared."; }
    private void OnDemoContentChanged(object? sender, RichDocumentChangedEventArgs args)
    {
        if (_trackedRange is { } range) _trackedRange = args.Map.Map(range.Anchor) is { } anchor && args.Map.Map(range.Active) is { } active ? new(anchor, active) : null;
        if (_bookmark is { } bookmark) _bookmark = args.Map.Map(bookmark);
        for (var index = _widgets.Count - 1; index >= 0; index--)
            if (args.Map.Map(_widgets[index].Anchor) is { } mapped) _widgets[index] = _widgets[index] with { Anchor = mapped };
            else _widgets.RemoveAt(index);
        UpdateFormattingTools();
    }
    private void AddWidget(RichTextAdornmentPlacement placement)
    {
        if (ActivePoint() is not { } point) { ResultLabel.Text = "Place a text caret first."; return; }
        _widgetSource ??= Editor.Presentation.CreateWidgetSource(item =>
        {
            var button = new Button { Text = (string?)item.Data ?? item.Id, FontSize = 12, Padding = new Thickness(8, 2), HeightRequest = 30 };
            button.Clicked += (_, _) => ResultLabel.Text = $"Invoked widget {item.Id}. Its content is not in the RTF document.";
            return button;
        });
        var name = "Widget " + ++_widgetNumber;
        _widgets.Add(new(name, point, new(90, 30), name, new() { Placement = placement }, name));
        if (placement == RichTextAdornmentPlacement.LeftMargin) Editor.Presentation.MarginWidth = 100;
        var request = _widgetSource.BeginUpdate();
        ResultLabel.Text = _widgetSource.TryPublish(request, _widgets) ? $"{_widgets.Count} transient widgets; views are created when visible." : "The widget request is stale.";
    }
    private void ClearWidgets() { _widgets.Clear(); _widgetSource?.Clear(); Editor.Presentation.MarginWidth = 0; ResultLabel.Text = "Widgets cleared."; }
    private void CaptureLayout()
    {
        _capturedLayout = Editor.TextLayout.Capture(); _capturedCaret = ActivePoint();
        ResultLabel.Text = _capturedLayout is { } layout ? $"Captured layout {layout.LayoutVersion}, {layout.Lines.Count} visible lines, viewport {layout.Viewport}." : "Layout is unavailable while composing or before display.";
    }
    private void InspectLayout() => ResultLabel.Text = _capturedLayout is { } layout && _capturedCaret is { } point && Editor.TextLayout.GetCaretBounds(layout, point) is { } bounds
        ? $"Captured caret bounds: {bounds}." : "Capture is stale or the caret is outside the viewport. Capture layout again.";
    private void InspectSelection()
    {
        var layout = Editor.TextLayout.Capture();
        ResultLabel.Text = layout is null ? "No committed layout is available." : $"{Editor.TextLayout.GetSelectionBounds(layout, Editor.Selection).Count} visible selection rectangles; primary address {ActivePoint()}.";
    }
    private void OnDemoPointer(object? sender, RichTextPointerEventArgs args)
    {
        if (DemoPicker.SelectedItem as string != "Layout & input") return;
        var capture = Editor.TextLayout.CaptureInput();
        var hit = capture is null ? null : Editor.TextLayout.HitTest(capture, args.Point);
        ResultLabel.Text = $"Pointer {args.Point}; text address {hit?.Point.ToString() ?? "none"}; composing: {Editor.Composition is not null}.";
    }
    private void UpdateInputReadout()
    {
        if (DemoPicker.SelectedItem as string == "Layout & input") ResultLabel.Text = Editor.Composition is null ? "Input is committed." : "Native marked text is provisional; shared content remains committed.";
    }
    private void UpdateGeometry()
    {
        if (!_showGeometry || Editor.TextLayout.CaptureInput() is not { } layout)
        {
            if (_geometryRectangles.Length != 0) { _geometryRectangles = []; GeometryOverlay.Children.Clear(); }
            return;
        }
        var selection = Editor.Composition?.Selection ?? Editor.Selection;
        var rectangles = Editor.TextLayout.GetSelectionBounds(layout, selection).Select(rect => (rect, Colors.SkyBlue.WithAlpha(0.35f))).ToList();
        var active = selection switch { CoreTextSelection text => (RichTextPoint?)text.Active, RichTextSelectionSet set => set.Primary.Selection.Active, _ => null };
        if (active is { } caret && Editor.TextLayout.GetCaretBounds(layout, caret) is { } bounds) rectangles.Add((new(bounds.X, bounds.Y, Math.Max(2, bounds.Width), bounds.Height), Colors.Coral));
        if (_geometryRectangles.SequenceEqual(rectangles)) return;
        _geometryRectangles = rectangles.ToArray();
        GeometryOverlay.Children.Clear();
        foreach (var (rectangle, color) in rectangles)
        {
            var box = new BoxView { Color = color, InputTransparent = true };
            AbsoluteLayout.SetLayoutBounds(box, rectangle);
            GeometryOverlay.Add(box);
        }
    }
    private void ResetDemos()
    {
        _capturedEdit = null; _capturedLayout = null; _capturedCaret = null; _trackedRange = null; _bookmark = null;
        _capturedHighlight?.Clear(); _trackingHighlight?.Clear(); ClearWidgets(); _geometryRectangles = []; GeometryOverlay.Children.Clear();
    }
}
