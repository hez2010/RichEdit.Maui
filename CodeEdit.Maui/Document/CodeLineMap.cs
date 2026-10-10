namespace CodeEdit.Maui;

internal sealed class CodeLineMap
{
    internal TextSnapshot Buffer { get; }
    internal int Count => Buffer.LineCount;
    internal CodeLineMap(TextSnapshot text) => Buffer = text;
    internal int GetLineIndex(int position) => Buffer.GetPosition(position).Line;
    internal TextSpan GetRange(int index) => Buffer.GetLine(index).Span;
    internal TextPosition GetPosition(int offset) => Buffer.GetPosition(offset);

    internal (int First, int Last) GetSelectedLines(TextSpan selection)
    {
        var last = GetLineIndex(selection.End);
        if (!selection.IsEmpty && last > 0 && selection.End == Buffer.GetLine(last).Span.Start)
            last--;

        return (GetLineIndex(selection.Start), last);
    }
}
