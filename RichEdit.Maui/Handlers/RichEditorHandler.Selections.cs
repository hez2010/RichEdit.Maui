namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private GraphicsView? _secondarySelectionView;
    private SecondarySelectionDrawable? _secondarySelectionDrawable;

    internal IReadOnlyList<Rect> SecondaryCaretBounds => _secondarySelectionDrawable?.Carets ?? [];
    internal void RefreshSelectionPresentation() => QueueAdornmentLayout();

    private void UpdateSecondarySelections(Rect viewport)
    {
        var virtualPrimary = VirtualView.Selections.Primary.ActiveVirtualSpaces != 0 && !VirtualView.Composition.IsActive;
        var primaryVirtualSelection = VirtualView.Selections.Primary.AnchorVirtualSpaces != 0 || VirtualView.Selections.Primary.ActiveVirtualSpaces != 0;
        UpdateVirtualPrimaryCaret(virtualPrimary);
        if (VirtualView.Selections.Count < 2 && !VirtualView.DrawScopedPrimaryAsSecondary && !primaryVirtualSelection)
        {
            VirtualView.Adornments.Overlay.SetSelectionOverlay(null);
            _secondarySelectionView?.Handler?.DisconnectHandler();
            _secondarySelectionView = null;
            _secondarySelectionDrawable = null;
            return;
        }

        if (_secondarySelectionView is null)
        {
            _secondarySelectionDrawable = new();
            _secondarySelectionView = new GraphicsView
            {
                Drawable = _secondarySelectionDrawable,
                InputTransparent = true,
            };
            VirtualView.Adornments.Overlay.SetSelectionOverlay(_secondarySelectionView);
        }

        var carets = new List<Rect>();
        var ranges = new List<Rect>();
        var captured = VirtualView.TextLayout.Capture();
        if (captured is { } layout)
            foreach (var selection in VirtualView.Selections)
            {
                var primary = selection.Id == VirtualView.Selections.Primary.Id;
                if (primary && !VirtualView.DrawScopedPrimaryAsSecondary && !primaryVirtualSelection)
                    continue;
                if (!selection.Range.IsEmpty && !primary)
                    ranges.AddRange(VirtualView.TextLayout.GetRangeBounds(layout, new(selection.Range.Start, selection.Range.Length)));
                if (selection.AnchorVirtualSpaces != 0 || selection.ActiveVirtualSpaces != 0)
                {
                    var position = selection.Range.End;
                    if (VirtualView.TextLayout.GetCaretBounds(layout, position, RichTextCaretAffinity.Upstream) is { } end)
                    {
                        var startSpaces = selection.Range.IsEmpty ? Math.Min(selection.AnchorVirtualSpaces, selection.ActiveVirtualSpaces) : 0;
                        var endSpaces = selection.Anchor == position ? selection.AnchorVirtualSpaces : selection.ActiveVirtualSpaces;
                        if (selection.Range.IsEmpty) endSpaces = Math.Max(selection.AnchorVirtualSpaces, selection.ActiveVirtualSpaces);
                        var advance = VirtualSpaceAdvance(position);
                        ranges.Add(new(end.X + Math.Min(startSpaces * advance, endSpaces * advance), end.Y, Math.Abs((endSpaces - startSpaces) * advance), end.Height));
                    }
                }
                if (!layout.Lines.SelectMany(static line => line.SourceRanges).Any(range =>
                        selection.Active >= range.Start && selection.Active <= range.End))
                    continue;
                if (primary && !VirtualView.DrawScopedPrimaryAsSecondary && !virtualPrimary) continue;
                if (VirtualView.TextLayout.GetCaretBounds(layout, selection) is { } caret)
                    carets.Add(new(caret.X, caret.Y, Math.Max(1, caret.Width), caret.Height));
            }

        var colors = GetSecondarySelectionColors();
        _secondarySelectionDrawable!.Carets = carets;
        _secondarySelectionDrawable.Ranges = ranges;
        _secondarySelectionDrawable.Clip = captured?.TextViewport ?? viewport;
        _secondarySelectionDrawable.CaretColor = colors.Caret;
        _secondarySelectionDrawable.SelectionColor = colors.Selection;
        _secondarySelectionDrawable.DrawCarets = (VirtualView.IsFocused || VirtualView.StructuredInputCoordinator?.HasFocusedStructuredSurface == true) &&
            !VirtualView.IsReadOnly && !VirtualView.Composition.IsActive && VirtualView.StructuredSession?.Composition is null;
        _secondarySelectionView.Invalidate();
    }

    private partial (Color Caret, Color Selection) GetSecondarySelectionColors();

    private void DisconnectSecondarySelections()
    {
        VirtualView.Adornments.Overlay.SetSelectionOverlay(null);
        _secondarySelectionView?.Handler?.DisconnectHandler();
        _secondarySelectionView = null;
        _secondarySelectionDrawable = null;
    }

    private sealed class SecondarySelectionDrawable : IDrawable
    {
        internal IReadOnlyList<Rect> Carets { get; set; } = [];
        internal IReadOnlyList<Rect> Ranges { get; set; } = [];
        internal Rect Clip { get; set; }
        internal Color CaretColor { get; set; } = Colors.Black;
        internal Color SelectionColor { get; set; } = Colors.Transparent;
        internal bool DrawCarets { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();
            canvas.ClipRectangle((float)Clip.X, (float)Clip.Y, (float)Clip.Width, (float)Clip.Height);
            canvas.FillColor = SelectionColor;
            foreach (var rect in Ranges)
                canvas.FillRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            if (DrawCarets)
            {
                canvas.FillColor = CaretColor;
                foreach (var rect in Carets)
                    canvas.FillRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            }
            canvas.RestoreState();
        }
    }
}
