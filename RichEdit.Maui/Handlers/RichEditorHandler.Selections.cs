namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private GraphicsView? _secondarySelectionView;
    private SecondarySelectionDrawable? _secondarySelectionDrawable;

    internal IReadOnlyList<Rect> SecondaryCaretBounds => _secondarySelectionDrawable?.Carets ?? [];

    private void UpdateSecondarySelections(Rect viewport)
    {
        if (VirtualView.Selections.Count < 2)
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
                if (selection.Id == VirtualView.Selections.Primary.Id)
                    continue;
                if (!selection.Range.IsEmpty)
                    ranges.AddRange(VirtualView.TextLayout.GetRangeBounds(layout, new(selection.Range.Start, selection.Range.Length)));
                if (!layout.Lines.SelectMany(static line => line.SourceRanges).Any(range =>
                        selection.Active >= range.Start && selection.Active <= range.End))
                    continue;
                if (VirtualView.TextLayout.GetCaretBounds(layout, selection.Active, (RichTextCaretAffinity)selection.Affinity) is { } caret)
                    carets.Add(new(caret.X, caret.Y, Math.Max(1, caret.Width), caret.Height));
            }

        var colors = GetSecondarySelectionColors();
        _secondarySelectionDrawable!.Carets = carets;
        _secondarySelectionDrawable.Ranges = ranges;
        _secondarySelectionDrawable.Clip = captured?.TextViewport ?? viewport;
        _secondarySelectionDrawable.CaretColor = colors.Caret;
        _secondarySelectionDrawable.SelectionColor = colors.Selection;
        _secondarySelectionDrawable.DrawCarets = VirtualView.IsFocused && !VirtualView.IsReadOnly && !VirtualView.Composition.IsActive;
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
