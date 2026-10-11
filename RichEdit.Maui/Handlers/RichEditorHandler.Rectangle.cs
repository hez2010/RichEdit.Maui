using System.Globalization;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    // Native extent queries prune complete visual runs. Subdivide only at the
    // rectangle edges or bidi discontinuities, never hit-test every character.
    internal IReadOnlyList<TextSpan> GetRectangleRanges(TextSpan source, double left, double right, double y)
    {
        if (source.IsEmpty || !HasTextLayout) return Array.Empty<TextSpan>();
        var text = VirtualView.InputSnapshot.TextBuffer.GetText(source);
        var elements = StringInfo.ParseCombiningCharacters(text);
        var pending = new Stack<(int First, int Last)>();
        var result = new List<TextSpan>();
        var boundaries = new List<int> { source.Start, source.End };
        AddRectanglePartitions(source, boundaries);
        var cuts = boundaries.Order().Distinct().Select(position =>
        {
            var index = Array.BinarySearch(elements, position - source.Start);
            return index >= 0 ? index : ~index;
        }).Distinct().ToArray();
        for (var i = cuts.Length - 1; i > 0; i--) pending.Push((cuts[i - 1], cuts[i]));
        while (pending.TryPop(out var part))
        {
            var start = Offset(part.First);
            var end = Offset(part.Last);
            var bounds = GetTextRangeExtentCore(NativeProjection.ToDisplay(new TextSpan(start, end - start)));
            if (bounds is not { Height: > 0 } rect) continue;
            if (y < rect.Top || y >= rect.Bottom || rect.Right < left || rect.Left > right) continue;
            var leaf = part.Last - part.First == 1;
            if (rect.Left >= left && rect.Right <= right || leaf && rect.Center.X >= left && rect.Center.X <= right)
            {
                if (result.Count > 0 && result[^1].End == start)
                    result[^1] = new(result[^1].Start, end - result[^1].Start);
                else result.Add(new(start, end - start));
            }
            else if (!leaf)
            {
                var middle = part.First + (part.Last - part.First) / 2;
                pending.Push((middle, part.Last));
                pending.Push((part.First, middle));
            }
        }
        return result;
        int Offset(int element) => element < elements.Length ? source.Start + elements[element] : source.End;
    }

    private partial Rect? GetTextRangeExtentCore(TextSpan range);
    partial void AddRectanglePartitions(TextSpan source, List<int> boundaries);
}
