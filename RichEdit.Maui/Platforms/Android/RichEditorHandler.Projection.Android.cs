using Android.Text;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    partial void UpdateDisplayInputLimit() => PlatformView.SetFilters(DisplayInputLimit < 0 ? [] : [new InputFilterLengthFilter(DisplayInputLimit)]);
    private sealed class DisplayReservationSpan(RichTextDisplayMarker marker) : Java.Lang.Object
    {
        internal RichTextDisplayMarker Marker { get; } = marker;
    }

    private partial void WriteDisplayReservationMetadata()
    {
        if (PlatformView.EditableText is not { } text) return;
        var applying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            var pending = NativeProjection.Markers.ToDictionary(static item => item.Position, static item => item.Marker);
            // Android moves existing spans with text. Retain them so publishing a
            // batch creates only its new reservations, including after native edits.
            foreach (var span in GetSpans<DisplayReservationSpan>(text, 0, text.Length()))
            {
                var start = text.GetSpanStart(span);
                if (pending.TryGetValue(start, out var expected) && text.GetSpanEnd(span) == start + 1 &&
                    span.Marker == expected)
                    pending.Remove(start);
                else { text.RemoveSpan(span); span.Dispose(); }
            }
            foreach (var (start, marker) in pending)
                text.SetSpan(new DisplayReservationSpan(marker),
                    start, start + 1, SpanTypes.ExclusiveExclusive);
        }
        finally { _applyingDocument = applying; }
    }

    private partial RichTextDisplayProjection ReadDisplayReservationMetadata(string text)
    {
        if (PlatformView.EditableText is not { } editable) return RichTextDisplayProjection.Empty;
        var spans = editable.GetSpans(0, editable.Length(), Java.Lang.Class.FromType(typeof(DisplayReservationSpan)))?.OfType<DisplayReservationSpan>() ?? [];
        return RichTextDisplayProjection.FromNative(spans.Select(span => (span.Marker, editable.GetSpanStart(span), editable.GetSpanEnd(span) - editable.GetSpanStart(span))), text);
    }

    private partial Point? GetDisplayReservationBaseline(int position)
    {
        if (PlatformView.Layout is not { } layout) return null;
        NativeGeometryQueryCount += 3;
        var line = layout.GetLineForOffset(position);
        var origin = TextLayoutOrigin();
        var x = (layout.GetPrimaryHorizontal(position) + origin.X) / LayoutDensity;
        var y = (layout.GetLineBaseline(line) + origin.Y) / LayoutDensity;
        return new(x, y);
    }
}
