using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A one-based logical line and UTF-16 column.</summary>
/// <param name="Line">The line number, starting at one.</param>
/// <param name="Column">The UTF-16 column, starting at one. A tab occupies one column.</param>
public readonly record struct CodePosition(int Line, int Column);

internal sealed class CodeLineMap
{
    private readonly int[] _starts;
    internal string Text { get; }
    internal int Count => _starts.Length;

    internal CodeLineMap(string text)
    {
        Text = text;
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] is '\n' or '\u2028') starts.Add(i + 1);
        _starts = [.. starts];
    }

    internal int GetLineIndex(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Text.Length);
        var result = Array.BinarySearch(_starts, position);
        return result >= 0 ? result : ~result - 1;
    }

    internal RichTextRange GetRange(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        var end = index + 1 < Count ? _starts[index + 1] - 1 : Text.Length;
        return new RichTextRange(_starts[index], end - _starts[index]);
    }

    internal CodePosition GetPosition(int offset)
    {
        var index = GetLineIndex(offset);
        return new CodePosition(index + 1, offset - _starts[index] + 1);
    }

    internal (int First, int Last) GetSelectedLines(RichTextRange selection)
    {
        var last = GetLineIndex(selection.End);
        if (!selection.IsEmpty && last > 0 && selection.End == _starts[last]) last--;
        return (GetLineIndex(selection.Start), last);
    }
}
