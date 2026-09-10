using Microsoft.UI.Text;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    partial void UpdateDisplayInputLimit() => PlatformView.MaxLength = DisplayInputLimit < 0 ? 0 : DisplayInputLimit;
    private readonly List<(RichTextDisplayMarker Marker, ITextRange Range)> _displayReservationRanges = [];

    private partial void WriteDisplayReservationMetadata()
    {
        if (NativeProjection.IsEmpty) { _displayReservationRanges.Clear(); return; }
        var map = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        var previous = _displayReservationRanges.GroupBy(static item => item.Marker.Adornment)
            .ToDictionary(static group => group.Key, static group => new Queue<ITextRange>(group.Select(static item => item.Range)));
        _displayReservationRanges.Clear();
        foreach (var (position, marker) in NativeProjection.Markers)
        {
            var start = map?.ToNativePosition(position) ?? position;
            var end = map?.ToNativePosition(position + 1) ?? position + 1;
            var range = previous.TryGetValue(marker.Adornment, out var ranges) && ranges.TryDequeue(out var retained) ? retained : PlatformView.Document.GetRange(start, end);
            if (range.StartPosition != start || range.EndPosition != end) range.SetRange(start, end);
            _displayReservationRanges.Add((marker, range));
        }
    }

    private partial RichTextDisplayProjection ReadDisplayReservationMetadata(string text)
    {
        if (_displayReservationRanges.Count == 0) return RichTextDisplayProjection.Empty;
        var map = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        return RichTextDisplayProjection.FromNative(_displayReservationRanges.Select(item =>
            (item.Marker, map?.ToLogicalPosition(item.Range.StartPosition) ?? item.Range.StartPosition,
                map is null ? item.Range.EndPosition - item.Range.StartPosition :
                    map.ToLogicalPosition(item.Range.EndPosition) - map.ToLogicalPosition(item.Range.StartPosition))), text);
    }

    private partial Point? GetDisplayReservationBaseline(int position)
    {
        var range = PlatformView.Document.GetRange(NativePositionFromDisplay(position), NativePositionFromDisplay(position + 1));
        NativeGeometryQueryCount += 2;
        range.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        range.GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Baseline,
            PointOptions.ClientCoordinates | PointOptions.AllowOffClient | PointOptions.Start, out var baseline);
        var origin = TextLayoutOrigin();
        return new(rect.X + origin.X, baseline.Y + origin.Y);
    }
}
