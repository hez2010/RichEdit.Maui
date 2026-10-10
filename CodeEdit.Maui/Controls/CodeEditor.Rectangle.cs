using RichEdit.Maui;

namespace CodeEdit.Maui;

public sealed partial class CodeEditor
{
    private Point? _rectangleAnchor;
    private DocumentRevision _rectangleRevision;

    private bool ObserveSelectionPointer(RichTextPointerEventArgs input, bool pressed)
    {
        if (input.DeviceKind == RichTextPointerDeviceKind.Touch) return false;
        if ((input.Buttons & RichTextPointerButtons.Primary) == 0)
        {
            var handled = _rectangleAnchor is not null;
            _rectangleAnchor = null;
            return handled;
        }
        var point = TranslatePointer(input).Point;
        if (!pressed)
        {
            if (_rectangleRevision != Document.Revision) _rectangleAnchor = null;
            return _rectangleAnchor is { } anchor && SelectRectangle(anchor, point, AllowVirtualSpace);
        }
        _rectangleAnchor = null;
        if (input.Modifiers == EditorKeyModifiers.None && !Composition.IsActive &&
            (Selections.Count > 1 || Selections.Primary.AnchorVirtualSpaces != 0 || Selections.Primary.ActiveVirtualSpaces != 0))
            Selections = new((TextSelection[])[Selections.Primary with { AnchorVirtualSpaces = 0, ActiveVirtualSpaces = 0 }]);
        if (input.Modifiers != EditorKeyModifiers.Alt || Composition.IsActive || TextLayout.Capture() is not { } layout ||
            TextLayout.HitTest(layout, point) is not { } hit) return false;
        _rectangleAnchor = point;
        _rectangleRevision = Document.Revision;
        var selections = Selections.ToList();
        var existing = selections.FindIndex(item => item.Range.IsEmpty && item.Active == hit.Position && item.ActiveVirtualSpaces == 0);
        if (existing >= 0 && selections.Count > 1)
        {
            var primaryId = Selections.Primary.Id;
            selections.RemoveAt(existing);
            var primary = selections.FindIndex(item => item.Id == primaryId);
            Selections = new(selections, primary < 0 ? Math.Min(existing, selections.Count - 1) : primary);
        }
        else AddCaret(hit.Position);
        return true;
    }

    /// <summary>Identifies <see cref="AllowVirtualSpace"/>.</summary>
    public static readonly BindableProperty AllowVirtualSpaceProperty = BindableProperty.Create(
        nameof(AllowVirtualSpace), typeof(bool), typeof(CodeEditor), false,
        propertyChanged: static (view, _, value) => ((CodeEditor)view).TextView.SourceVirtualSpaceEnabled = (bool)value);

    /// <summary>Gets or sets whether caret movement and rectangular gestures can extend beyond logical line ends.</summary>
    public bool AllowVirtualSpace
    {
        get => (bool)GetValue(AllowVirtualSpaceProperty);
        set => SetValue(AllowVirtualSpaceProperty, value);
    }

    /// <summary>Selects between two native hit-test boundaries on each visible visual row intersecting the rectangle.</summary>
    /// <remarks>Points use this control's coordinates. Wrapping, tabs, variable-width text and bidi endpoints use native geometry.
    /// Virtual space is available only beyond a logical line end, never beyond a soft wrap. The active row becomes primary.</remarks>
    public bool SelectRectangle(Point anchor, Point active, bool virtualSpace = false)
    {
        VerifyAccess();
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || !double.IsFinite(active.X) || !double.IsFinite(active.Y))
            throw new ArgumentOutOfRangeException(nameof(active));
        if (Composition.IsActive || TextView.Handler is not RichEditorHandler handler || TextLayout.Capture() is not { } layout)
            return false;
        var top = Math.Min(anchor.Y, active.Y);
        var bottom = Math.Max(anchor.Y, active.Y);
        var selections = new List<TextSelection>();
        var primary = 0;
        var distance = double.MaxValue;
        foreach (var line in layout.Lines)
        {
            if (line.SourceRanges.Count == 0 || line.Bounds.Bottom <= top || line.Bounds.Top > bottom) continue;
            var y = Math.Clamp(line.Bounds.Center.Y, layout.TextViewport.Top + 0.01, layout.TextViewport.Bottom - 0.01);
            var first = Endpoint(anchor.X, y, line);
            var last = Endpoint(active.X, y, line);
            if (first is null || last is null) continue;
            selections.Add(new(first.Value.Position, last.Value.Position)
            {
                AnchorVirtualSpaces = first.Value.Spaces, ActiveVirtualSpaces = last.Value.Spaces,
                Affinity = last.Value.Affinity, PreferredVisualX = active.X - layout.Origin.X,
            });
            if (Math.Abs(y - active.Y) < distance) { distance = Math.Abs(y - active.Y); primary = selections.Count - 1; }
        }
        if (selections.Count == 0) return false;
        Selections = new(selections, primary);
        return true;

        (int Position, int Spaces, CaretAffinity Affinity)? Endpoint(double x, double y, RichTextVisualLine line)
        {
            var hit = TextLayout.HitTest(layout, new(Math.Clamp(x, layout.TextViewport.Left + 0.01, layout.TextViewport.Right - 0.01), y));
            if (hit is null) return null;
            var source = Document.CurrentSnapshot.TextBuffer;
            var start = line.SourceRanges[0].Start;
            var endPosition = line.SourceRanges[^1].End;
            while (endPosition > start && source[endPosition - 1] is '\r' or '\n') endPosition--;
            var position = Math.Clamp(hit.Position, start, endPosition);
            var spaces = 0;
            var logical = source.GetLine(source.GetPosition(position).Line);
            if ((virtualSpace || AllowVirtualSpace) && position == logical.Span.End &&
                TextLayout.GetCaretBounds(layout, position, RichTextCaretAffinity.Upstream) is { } end && Math.Abs(end.Center.Y - y) < end.Height)
                spaces = Math.Max(0, checked((int)Math.Round((x - end.X) / handler.VirtualSpaceAdvance(position))));
            return (position, spaces, (CaretAffinity)hit.Affinity);
        }
    }
}
