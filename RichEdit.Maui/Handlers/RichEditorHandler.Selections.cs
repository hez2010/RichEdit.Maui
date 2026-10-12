namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private AbsoluteLayout? _secondarySelectionView;
    private SecondarySelectionPresentation? _secondarySelectionPresentation;

    internal IReadOnlyList<Rect> SecondaryCaretBounds => _secondarySelectionPresentation?.Carets ?? [];
    internal void RefreshSelectionPresentation() => QueueAdornmentLayout();

    private void UpdateSecondarySelections(Rect viewport)
    {
        var virtualPrimary = !HasNativeVirtualSelection && VirtualView.Selections.Primary.ActiveVirtualSpaces != 0 && !VirtualView.Composition.IsActive;
        var primaryVirtualSelection = !HasNativeVirtualSelection &&
            (VirtualView.Selections.Primary.AnchorVirtualSpaces != 0 || VirtualView.Selections.Primary.ActiveVirtualSpaces != 0);
        UpdateVirtualPrimaryCaret(virtualPrimary);
        if (VirtualView.Selections.Count < 2 && !VirtualView.DrawScopedPrimaryAsSecondary && !primaryVirtualSelection)
        {
            VirtualView.Adornments.Overlay.SetSelectionOverlay(null);
            _secondarySelectionView?.Handler?.DisconnectHandler();
            _secondarySelectionView = null;
            _secondarySelectionPresentation = null;
            return;
        }

        if (_secondarySelectionView is null)
        {
            _secondarySelectionPresentation = new();
            _secondarySelectionView = new AbsoluteLayout { InputTransparent = true, IsClippedToBounds = true };
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
        _secondarySelectionPresentation!.Carets = carets;
        _secondarySelectionPresentation.Ranges = ranges;
        _secondarySelectionPresentation.Clip = captured?.TextViewport ?? viewport;
        _secondarySelectionPresentation.CaretColor = colors.Caret;
        _secondarySelectionPresentation.SelectionColor = colors.Selection;
        _secondarySelectionPresentation.ShowCarets = (VirtualView.IsFocused || VirtualView.StructuredInputCoordinator?.HasFocusedStructuredSurface == true) &&
            !VirtualView.IsReadOnly && !VirtualView.Composition.IsActive && VirtualView.StructuredSession?.Composition is null;
        _secondarySelectionPresentation.Update(_secondarySelectionView);
    }

    private partial (Color Caret, Color Selection) GetSecondarySelectionColors();

    private void DisconnectSecondarySelections()
    {
        VirtualView.Adornments.Overlay.SetSelectionOverlay(null);
        _secondarySelectionView?.Handler?.DisconnectHandler();
        _secondarySelectionView = null;
        _secondarySelectionPresentation = null;
    }

    private sealed class SecondarySelectionPresentation
    {
        private readonly List<BoxView> _boxes = [];
        internal IReadOnlyList<Rect> Carets { get; set; } = [];
        internal IReadOnlyList<Rect> Ranges { get; set; } = [];
        internal Rect Clip { get; set; }
        internal Color CaretColor { get; set; } = Colors.Black;
        internal Color SelectionColor { get; set; } = Colors.Transparent;
        internal bool ShowCarets { get; set; }

        internal void Update(AbsoluteLayout layout)
        {
            var rectangles = Ranges.Select(rect => (Rect: rect.Intersect(Clip), Color: SelectionColor))
                .Concat(ShowCarets ? Carets.Select(rect => (Rect: rect.Intersect(Clip), Color: CaretColor)) : [])
                .Where(item => item.Rect.Width > 0 && item.Rect.Height > 0).ToArray();
            while (_boxes.Count < rectangles.Length)
            {
                var box = new BoxView { InputTransparent = true };
                _boxes.Add(box);
                layout.Add(box);
            }
            while (_boxes.Count > rectangles.Length)
            {
                var last = _boxes[^1];
                layout.Remove(last);
                last.Handler?.DisconnectHandler();
                _boxes.RemoveAt(_boxes.Count - 1);
            }
            for (var index = 0; index < rectangles.Length; index++)
            {
                _boxes[index].Color = rectangles[index].Color;
                AbsoluteLayout.SetLayoutBounds(_boxes[index], rectangles[index].Rect);
            }
        }
    }
}
