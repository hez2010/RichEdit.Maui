using Android.Text;
using Android.Text.Style;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void RefreshSourceTabStops(ISpannable text, RichTextDocumentSnapshot snapshot, TextSpan range)
    {
        if (VirtualView.SourceSession is null || snapshot.Length == 0) return;
        var buffer = snapshot.TextBuffer;
        var first = buffer.GetLine(buffer.GetPosition(range.Start).Line).Span.Start;
        var lastLine = buffer.GetLine(buffer.GetPosition(range.End).Line);
        var last = lastLine.Span.End + lastLine.LineBreakLength;
        // The common tab-free source needs no paragraph descriptors or JNI
        // substring calls. Clear existing stops once, then inspect bounded text.
        RemoveIntersectingParagraphSpans<TabStopSpanStandard>(text, first, last);
        var source = buffer.GetText(new(first, last - first));
        if (source.AsSpan().IndexOf('\t') < 0) return;
        for (var start = 0; start < source.Length;)
        {
            var end = GetParagraphEnd(source, start);
            if (source.AsSpan(start, end - start).IndexOf('\t') >= 0) ApplySourceTabStops(text, first + start, first + end);
            if (end <= start) break;
            start = end;
        }
    }

    private void ApplySourceTabStops(ISpannable text, int start, int end)
    {
        var value = Java.Lang.ICharSequenceExtensions.SubSequence(text, start, end) ?? "";
        if (!value.Contains('\t')) return;
        var interval = Math.Max(1, PlatformView.Paint!.MeasureText(" ") * VirtualView.SourceTabSize);
        if (VirtualView.SourceWordWrap)
        {
            // A wrapped visual line restarts its tab grid at the leading edge.
            // Only stops within the native viewport can affect wrapped text.
            var width = PlatformView.Width > 0 ? PlatformView.Width - PlatformView.CompoundPaddingLeft - PlatformView.CompoundPaddingRight :
                PlatformView.Resources!.DisplayMetrics!.WidthPixels;
            for (var stop = 1; stop * interval <= Math.Max(1, width) + interval; stop++) AddStop(stop * interval);
            return;
        }
        var stops = new HashSet<int>();
        for (var offset = value.IndexOf('\t'); offset >= 0; offset = value.IndexOf('\t', offset + 1))
        {
            // Native shaping measures the bounded prefix, including earlier tab
            // spans, styled text, bidi runs, and inline reservations. An unwrapped
            // line needs only the stops actually reached by its authored tabs.
            var width = Android.Text.Layout.GetDesiredWidth(text, start, start + offset, PlatformView.Paint!);
            var next = (Math.Floor((width + 0.001) / interval) + 1) * interval;
            var pixels = checked((int)Math.Ceiling(next));
            if (stops.Add(pixels)) AddStop(pixels);
        }
        void AddStop(double pixels) => text.SetSpan(new TabStopSpanStandard(checked((int)Math.Ceiling(pixels))), start, end, SpanTypes.Paragraph);
    }
}
