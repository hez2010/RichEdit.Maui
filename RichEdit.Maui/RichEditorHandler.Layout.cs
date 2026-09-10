using System.Collections.Immutable;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    internal long NativeGeometryQueryCount { get; private set; }
    internal object LayoutAttachment { get; private set; } = new();
    internal bool HasTextLayout => _adornmentsConnected && !_applyingDocument && GetAdornmentViewport() is { Width: > 0, Height: > 0 };
    internal RichTextNativeLayout? CaptureTextLayout() => HasTextLayout ? CaptureTextLayoutCore() : null;
    internal Rect? GetTextCaretBounds(int position, RichTextCaretAffinity affinity) => HasTextLayout ?
        GetTextCaretBoundsCore(NativeProjection.ToDisplayCaret(position, affinity == RichTextCaretAffinity.Downstream), affinity) : null;
    internal IReadOnlyList<Rect> GetTextRangeBounds(RichTextRange range)
    {
        if (!HasTextLayout) return Array.Empty<Rect>();
        if (NativeProjection.IsEmpty) return GetTextRangeBoundsCore(range);
        var result = new List<Rect>();
        var start = range.Start;
        foreach (var item in NativeProjection.Reservations)
        {
            if (item.SourcePosition <= start) continue;
            if (item.SourcePosition >= range.End) break;
            result.AddRange(GetTextRangeBoundsCore(NativeProjection.ToDisplay(new RichTextRange(start, item.SourcePosition - start))));
            start = item.SourcePosition;
        }
        if (start < range.End) result.AddRange(GetTextRangeBoundsCore(NativeProjection.ToDisplay(new RichTextRange(start, range.End - start))));
        return result;
    }
    internal RichTextHit? HitTestText(Point point) => HasTextLayout ? HitTestTextCore(point) : null;
    private partial RichTextNativeLayout? CaptureTextLayoutCore();
    private partial Rect? GetTextCaretBoundsCore(int position, RichTextCaretAffinity affinity);
    private partial IReadOnlyList<Rect> GetTextRangeBoundsCore(RichTextRange range);
    private partial RichTextHit? HitTestTextCore(Point point);

    private ImmutableArray<RichTextRange> VisibleSourceRanges(int start, int end)
    {
        if (start < end && NativeProjection.ToSource(start) == NativeProjection.ToSource(end)) return [];
        start = NativeProjection.ToSource(start);
        end = NativeProjection.ToSource(end);
        start = Math.Clamp(start, 0, VirtualView.Document.Length);
        end = Math.Clamp(end, start, VirtualView.Document.Length);
        var result = new List<RichTextRange>();
        if (start == end && VirtualView.Folding.FindRange(start) is null) result.Add(new(start, 0));
        foreach (var hidden in VirtualView.Folding.EffectiveRanges)
        {
            if (hidden.End <= start) continue;
            if (hidden.Start >= end) break;
            if (hidden.Start > start) result.Add(new(start, hidden.Start - start));
            start = Math.Min(end, hidden.End);
        }
        if (start < end) result.Add(new(start, end - start));
        return [.. result];
    }

    private RichTextHit SourceHit(int position, RichTextCaretAffinity affinity, bool isText)
    {
        isText &= !NativeProjection.ContainsDisplayCharacter(position);
        position = NativeProjection.ToSource(position);
        position = Math.Clamp(position, 0, VirtualView.Document.Length);
        if (VirtualView.Folding.FindRange(position) is { } hidden) position = hidden.End;
        return new(position, affinity, isText ? RichTextHitKind.Text : RichTextHitKind.NearestPosition);
    }

    private static void AddFragment(List<Rect> result, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        for (var i = result.Count - 1; i >= 0; i--)
        {
            var previous = result[i];
            if (Math.Abs(previous.Y - rect.Y) < 0.5 && Math.Abs(previous.Height - rect.Height) < 0.5 &&
                previous.Right >= rect.Left - 0.5 && rect.Right >= previous.Left - 0.5)
            {
                result[i] = previous.Union(rect);
                return;
            }
        }
        result.Add(rect);
    }
}
