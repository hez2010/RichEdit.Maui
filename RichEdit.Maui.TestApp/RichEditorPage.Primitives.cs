using System.Windows.Input;

namespace RichEdit.Maui.TestApp;

// Each demonstration uses the same public surface available to an application.
public partial class RichEditorPage
{
    private sealed record CapturedEdit(RichTextRevision Revision, RichTextRange Range, string Text);
    private CapturedEdit? _capturedEdit;
    private RichTextTrackedRange? _trackedRange;
    private RichTextTrackedPosition? _bookmark;
    private RichTextDecorationLayer? _capturedHighlight;
    private RichTextDecorationLayer? _trackingHighlight;
    private RichTextLayoutSnapshot? _capturedLayout;
    private int _capturedCaret;
    private int _widgetNumber;
    private string _pointer = "Move or tap inside the editor to inspect a source position.";
    private readonly GeometryDrawing _drawing = new();

    private void InitializePrimitiveDemos()
    {
        DemoPicker.ItemsSource = new[] { "Formatting", "Checked edits", "Tracking", "Widgets", "Layout & input" };
        PlacementPicker.ItemsSource = Enum.GetValues<RichTextAdornmentPlacement>();
        PlacementPicker.SelectedItem = RichTextAdornmentPlacement.Inline;
        GeometryOverlay.Drawable = _drawing;
        DemoPicker.SelectedIndex = 0;
        UpdateState();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ResultLabel.Text = "Choose an action above, then edit the document below.";
        _capturedHighlight = Editor.Decorations.CreateLayer();
        _trackingHighlight = Editor.Decorations.CreateLayer();
        Editor.TextChanged += OnTextChanged;
        Editor.SelectionChanged += OnSelectionChanged;
        Editor.DocumentChanged += OnDocumentChanged;
        Editor.CompositionChanged += OnCompositionChanged;
        Editor.PointerMoved += OnPointer;
        Editor.PointerPressed += OnPointer;
        Editor.TextLayout.Changed += OnLayoutChanged;
        Application.Current!.RequestedThemeChanged += OnDemoThemeChanged;
        UpdateState();
        UpdateGeometry();
    }

    protected override void OnDisappearing()
    {
        Editor.TextChanged -= OnTextChanged;
        Editor.SelectionChanged -= OnSelectionChanged;
        Editor.DocumentChanged -= OnDocumentChanged;
        Editor.CompositionChanged -= OnCompositionChanged;
        Editor.PointerMoved -= OnPointer;
        Editor.PointerPressed -= OnPointer;
        Editor.TextLayout.Changed -= OnLayoutChanged;
        Application.Current!.RequestedThemeChanged -= OnDemoThemeChanged;
        ClearTracking();
        Editor.Adornments.Clear();
        Editor.Adornments.MarginWidth = 0;
        _capturedHighlight?.Dispose();
        _trackingHighlight?.Dispose();
        _capturedHighlight = _trackingHighlight = null;
        _capturedEdit = null;
        _capturedLayout = null;
        base.OnDisappearing();
    }

    private void OnDemoChanged(object? sender, EventArgs args)
    {
        FormattingToolbar.IsVisible = ParagraphToolbar.IsVisible = FormattingStatus.IsVisible = DemoPicker.SelectedIndex == 0;
        PrimitiveTools.IsVisible = DemoReadout.IsVisible = DemoPicker.SelectedIndex > 0;
        RevisionTools.IsVisible = DemoPicker.SelectedIndex == 1;
        TrackingTools.IsVisible = DemoPicker.SelectedIndex == 2;
        AdornmentTools.IsVisible = DemoPicker.SelectedIndex == 3;
        LayoutTools.IsVisible = ObservationLabel.IsVisible = DemoPicker.SelectedIndex == 4;
        InstructionsLabel.Text = DemoPicker.SelectedIndex switch
        {
            0 => "Select a passage to format it, or choose another tool to explore edits, tracking, widgets, and geometry.",
            1 => "Capture an edit. Change the document before applying it to see a stale revision rejected.",
            2 => "Track a selection or bookmark the caret, then type or undo. The highlight follows the text.",
            3 => "Add a widget at the caret. Its anchor follows edits without changing the source text.",
            _ => "Capture layout, then edit or scroll to invalidate it. Show bounds to inspect native geometry."
        };
        ResultLabel.Text = "Choose an action above, then edit the document below.";
        UpdateState();
        UpdateGeometry();
    }

    private void OnCaptureEdit(object? sender, EventArgs args)
    {
        _capturedEdit = new(Editor.Document.Revision, Editor.SelectedRange, ReplacementEntry.Text ?? "");
        ResultLabel.Text = $"Captured revision {_capturedEdit.Revision.Version}, range {RangeText(_capturedEdit.Range)}, replacement \"{_capturedEdit.Text}\".";
        UpdateState();
    }

    private void OnApplyEdit(object? sender, EventArgs args)
    {
        if (_capturedEdit is not { } captured) return;
        var accepted = Editor.TryApplyEdits(captured.Revision, (RichTextEdit[])[new(captured.Range, captured.Text)]);
        ResultLabel.Text = accepted ? "Accepted as one undoable edit. Try Undo, then apply the same capture again." :
            "Rejected: the captured revision is stale, or the editor is read only/composing. Source and history are unchanged.";
        UpdateState();
    }

    private void OnHighlightCapture(object? sender, EventArgs args)
    {
        if (_capturedEdit is not { Range.IsEmpty: false } captured || _capturedHighlight is null) return;
        var accepted = _capturedHighlight.TrySet(captured.Revision,
            (RichTextDecoration[])[new(captured.Range, new() { ForegroundColor = Colors.Black, BackgroundColor = Colors.LightGoldenrodYellow })]);
        ResultLabel.Text = accepted ? "Highlight accepted. The document revision and undo history did not change." : "Highlight rejected: the captured revision is stale.";
        UpdateState();
    }

    private void OnClearHighlight(object? sender, EventArgs args)
    {
        _capturedHighlight?.Clear();
        ResultLabel.Text = "The capture's highlight layer is clear; other presentation layers are independent.";
        UpdateState();
    }

    private void OnTrackSelection(object? sender, EventArgs args)
    {
        _trackedRange?.Dispose();
        _trackedRange = Editor.Document.Tracking.TrackRange(Editor.SelectedRange, RichTextTrackingAffinity.BeforeInsertion,
            RichTextTrackingAffinity.AfterInsertion, RichTextTrackingDeletionBehavior.Preserve);
        PaintTracking();
        ResultLabel.Text = "The blue range follows edits, including a replacement of its selected text.";
        UpdateState();
    }

    private void OnSelectTracked(object? sender, EventArgs args)
    {
        if (_trackedRange?.Range is not { } range) return;
        Editor.SelectedRange = range;
        Editor.ScrollIntoView(range);
        Editor.Focus();
        ResultLabel.Text = $"Selected the current tracked range {RangeText(range)}.";
    }

    private void OnBookmark(object? sender, EventArgs args)
    {
        _bookmark?.Dispose();
        _bookmark = Editor.Document.Tracking.TrackPosition(Editor.SelectionState.Active, RichTextTrackingAffinity.AfterInsertion);
        ResultLabel.Text = "Bookmarked the caret. Insertions at the bookmark move it after the inserted text.";
        UpdateState();
    }

    private void OnGoToBookmark(object? sender, EventArgs args)
    {
        if (_bookmark?.Position is not { } position) return;
        Editor.SelectedRange = new(position, 0);
        Editor.ScrollIntoView(Editor.SelectedRange);
        Editor.Focus();
        ResultLabel.Text = $"Moved to bookmark {position}.";
    }

    private void OnClearTracking(object? sender, EventArgs args)
    {
        ClearTracking();
        ResultLabel.Text = "Tracking handles disposed and their highlight removed.";
        UpdateState();
    }

    private void ClearTracking()
    {
        _trackedRange?.Dispose();
        _bookmark?.Dispose();
        _trackedRange = null;
        _bookmark = null;
        _trackingHighlight?.Clear();
    }

    private void PaintTracking()
    {
        if (_trackingHighlight is null) return;
        _trackingHighlight.TrySet(Editor.Document.Revision, _trackedRange?.Range is { IsEmpty: false } range ?
            (RichTextDecoration[])[new(range, new() { ForegroundColor = StudioTheme.Get("Ink"), BackgroundColor = StudioTheme.Get("Selection"), Underline = RichTextUnderlineStyle.Single })] : []);
    }

    private void OnAddAdornment(object? sender, EventArgs args)
    {
        if (PlacementPicker.SelectedItem is not RichTextAdornmentPlacement placement) return;
        var position = Editor.SelectionState.Active;
        var number = ++_widgetNumber;
        var button = (Button)((DataTemplate)Application.Current!.Resources["DemoWidgetTemplate"]).CreateContent();
        button.AutomationId = $"DemoAdornment{number}";
        if (placement == RichTextAdornmentPlacement.LeftMargin) Editor.Adornments.MarginWidth = 96;
        var adornment = Editor.Adornments.Add(position, button, new() { Placement = placement });
        button.BindingContext = new DemoWidget($"{placement} · {number}", placement,
            new Command(() => ResultLabel.Text = $"Clicked {placement} {number}; its current source anchor is {adornment.Position}."));
        ResultLabel.Text = $"Added {placement} at {position}. Source revision {Editor.Document.Revision.Version} is unchanged.";
        UpdateState();
    }

    private void OnClearAdornments(object? sender, EventArgs args)
    {
        Editor.Adornments.Clear();
        Editor.Adornments.MarginWidth = 0;
        ResultLabel.Text = "All widgets removed. Source text and undo history are unchanged.";
        UpdateState();
    }

    private void OnCaptureLayout(object? sender, EventArgs args)
    {
        _capturedLayout = Editor.TextLayout.Capture();
        _capturedCaret = Editor.SelectionState.Active;
        ResultLabel.Text = _capturedLayout is { } layout ?
            $"Captured layout {layout.LayoutVersion}: {layout.Lines.Count} visible fragments. Caret: {(Editor.TextLayout.GetCaretBounds(layout, _capturedCaret) is { } caret ? RectText(caret) : "offscreen")}. Edit or scroll, then query this capture." :
            "Native layout is not ready yet.";
        UpdateState();
    }

    private void OnInspectLayout(object? sender, EventArgs args)
    {
        if (_capturedLayout is not { } layout) return;
        ResultLabel.Text = Editor.TextLayout.GetCaretBounds(layout, _capturedCaret) is { } caret ?
            $"Captured caret {_capturedCaret}: {RectText(caret)}. Querying did not move the selection." :
            "No geometry: this capture is stale or its caret is outside the visible viewport. Capture the current layout to query again.";
    }

    private void OnInspectSelection(object? sender, EventArgs args)
    {
        var layout = Editor.TextLayout.Capture();
        if (layout is null) { ResultLabel.Text = "Native layout is not ready yet."; return; }
        var bounds = Editor.TextLayout.GetRangeBounds(layout, Editor.SelectedRange);
        ResultLabel.Text = $"Selection {RangeText(Editor.SelectedRange)} occupies {bounds.Count} visible rectangles. " +
            (bounds.Count > 0 ? $"First: {RectText(bounds[0])}." : "Scroll the selection into view to inspect it.");
    }

    private void OnShowGeometry(object? sender, ToggledEventArgs args) => UpdateGeometry();
    private void OnDemoThemeChanged(object? sender, AppThemeChangedEventArgs args) { PaintTracking(); UpdateGeometry(); }
    private void OnLayoutChanged(object? sender, EventArgs args) => UpdateGeometry();
    private void UpdateGeometry()
    {
        GeometryOverlay.IsVisible = DemoPicker.SelectedIndex == 4 && ShowGeometrySwitch.IsToggled;
        if (!GeometryOverlay.IsVisible) return;
        var layout = Editor.TextLayout.Capture();
        _drawing.Lines = layout?.Lines ?? [];
        _drawing.Selection = layout is null ? [] : Editor.TextLayout.GetRangeBounds(layout, Editor.SelectedRange);
        _drawing.Caret = layout is null ? null : Editor.TextLayout.GetCaretBounds(layout, Editor.SelectionState.Active);
        GeometryOverlay.Invalidate();
    }

    private void OnPointer(object? sender, RichTextPointerEventArgs args)
    {
        if (DemoPicker.SelectedIndex != 4) return;
        var layout = Editor.TextLayout.Capture();
        var hit = layout is null ? null : Editor.TextLayout.HitTest(layout, args.Point);
        _pointer = $"{args.DeviceKind} ({args.Point.X:F0}, {args.Point.Y:F0}) → " + (hit is null ? "no current hit" : $"source {hit.Position}, {hit.Kind}");
        UpdateObservation();
    }

    private void OnCompositionChanged(object? sender, RichTextCompositionChangedEventArgs args) => UpdateObservation();
    private void OnSelectionChanged(object? sender, RichTextSelectionChangedEventArgs args) { UpdateState(); UpdateGeometry(); }
    private void OnTextChanged(object? sender, RichTextTextChangedEventArgs args) { PaintTracking(); UpdateState(); }
    private void OnDocumentChanged(object? sender, RichTextDocumentReplacedEventArgs args) { ClearTracking(); _capturedLayout = null; UpdateState(); }

    private void UpdateState()
    {
        var captured = _capturedEdit is { } edit ? $" · capture v{edit.Revision.Version} ({(edit.Revision == Editor.Document.Revision ? "current" : "stale")})" : "";
        var tracking = _trackedRange is null ? "" : $" · tracked {(_trackedRange.Range is { } range ? RangeText(range) : "invalid")}";
        var bookmark = _bookmark is null ? "" : $" · bookmark {_bookmark.Position?.ToString() ?? "invalid"}";
        StateLabel.Text = $"Revision {Editor.Document.Revision.Version} · selection {RangeText(Editor.SelectedRange)} · widgets {Editor.Adornments.Count}{captured}{tracking}{bookmark}";
        ApplyEditButton.IsEnabled = _capturedEdit is not null;
        HighlightButton.IsEnabled = _capturedEdit is { Range.IsEmpty: false };
        SelectTrackedButton.IsEnabled = _trackedRange?.Range is not null;
        GoToBookmarkButton.IsEnabled = _bookmark?.Position is not null;
        InspectLayoutButton.IsEnabled = _capturedLayout is not null;
        UpdateObservation();
    }

    private void UpdateObservation()
    {
        var composition = Editor.Composition;
        ObservationLabel.Text = composition.IsActive ? $"IME composing {(composition.Range is { } range ? RangeText(range) : "(range unavailable)")}" : "IME idle";
        if (DemoPicker.SelectedIndex == 4) ObservationLabel.Text += $" · {_pointer}";
    }

    private static string RangeText(RichTextRange range) => $"[{range.Start}, {range.End})";
    private static string RectText(Rect rect) => $"({rect.X:F0}, {rect.Y:F0}, {rect.Width:F0} × {rect.Height:F0})";

    private sealed class GeometryDrawing : IDrawable
    {
        internal IReadOnlyList<RichTextVisualLine> Lines { get; set; } = [];
        internal IReadOnlyList<Rect> Selection { get; set; } = [];
        internal Rect? Caret { get; set; }
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = Colors.DodgerBlue;
            canvas.StrokeSize = 1;
            foreach (var line in Lines) canvas.DrawRectangle((float)line.Bounds.X, (float)line.Bounds.Y, (float)line.Bounds.Width, (float)line.Bounds.Height);
            canvas.FillColor = Colors.Orange.WithAlpha(0.25f);
            foreach (var rect in Selection) canvas.FillRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            canvas.FillColor = Colors.Crimson;
            if (Caret is { } caret) canvas.FillRectangle((float)caret.X, (float)caret.Y, 2, (float)caret.Height);
        }
    }
}

internal sealed record DemoWidget(string Title, RichTextAdornmentPlacement Placement, ICommand Invoke);
