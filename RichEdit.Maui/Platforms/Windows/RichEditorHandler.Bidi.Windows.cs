using System.Runtime.InteropServices;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private TextSnapshot? _bidiSnapshot;
    private readonly Dictionary<(int Start, bool Rtl), ScriptItem[]> _bidiItems = [];
    partial void AddRectanglePartitions(TextSpan source, List<int> boundaries)
    {
        foreach (var run in NativeDirectionalRanges(NativeProjection.ToDisplay(source)))
        {
            boundaries.Add(Math.Clamp(NativeProjection.ToSource(run.Start), source.Start, source.End));
            boundaries.Add(Math.Clamp(NativeProjection.ToSource(run.End), source.Start, source.End));
        }
    }

    // TOM GetRect spans caret endpoints when a range crosses bidi runs; that
    // rectangle can omit visually enclosed text. Query native directional items
    // separately. Itemization uses whole paragraphs so embeddings retain context.
    // https://learn.microsoft.com/windows/win32/api/usp10/nf-usp10-scriptitemize
    private IReadOnlyList<TextSpan> NativeDirectionalRanges(TextSpan range)
    {
        var buffer = DisplaySourceSnapshot.TextBuffer;
        if (!ReferenceEquals(buffer, _bidiSnapshot)) { _bidiSnapshot = buffer; _bidiItems.Clear(); }
        var result = new List<TextSpan>();
        var lineIndex = buffer.GetPosition(range.Start).Line;
        while (lineIndex < buffer.LineCount)
        {
            var line = buffer.GetLine(lineIndex++).Span;
            if (line.Start >= range.End) break;
            if (line.Length == 0) continue;
            var rightToLeft = PlatformView.Document.GetRange(NativePositionFromDisplay(line.Start), NativePositionFromDisplay(line.Start))
                .ParagraphFormat.RightToLeft == Microsoft.UI.Text.FormatEffect.On;
            if (!_bidiItems.TryGetValue((line.Start, rightToLeft), out var items))
            {
                if (_bidiItems.Count >= 32) _bidiItems.Clear();
                _bidiItems[(line.Start, rightToLeft)] = items = Itemize(buffer.GetText(line), rightToLeft);
            }
            for (var i = 0; i + 1 < items.Length; i++)
            {
                var start = Math.Max(range.Start, line.Start + items[i].Position);
                var end = Math.Min(range.End, line.Start + items[i + 1].Position);
                if (end > start) result.Add(new(start, end - start));
            }
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScriptItem
    {
        internal int Position;
        internal ushort Analysis;
        internal ushort State;
    }

    private static unsafe ScriptItem[] Itemize(string text, bool rightToLeft)
    {
        var capacity = Math.Min(text.Length + 1, 32);
        while (true)
        {
            var items = new ScriptItem[capacity + 1];
            uint control = 0;
            ushort state = rightToLeft ? (ushort)1 : (ushort)0;
            int count;
            int status;
            fixed (char* chars = text)
            fixed (ScriptItem* output = items)
                status = ScriptItemize(chars, text.Length, capacity, &control, &state, output, &count);
            if (status == 0) return items[..(count + 1)];
            if (status != unchecked((int)0x8007000E) || capacity >= text.Length + 1)
                Marshal.ThrowExceptionForHR(status);
            capacity = Math.Min(text.Length + 1, checked(capacity * 2));
        }
    }

    [LibraryImport("usp10.dll")]
    private static unsafe partial int ScriptItemize(char* text, int length, int capacity,
        uint* control, ushort* state, ScriptItem* items, int* count);
}
