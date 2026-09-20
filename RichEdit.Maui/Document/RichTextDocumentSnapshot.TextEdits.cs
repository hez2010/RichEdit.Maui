using System.Text;

namespace RichEdit.Maui;

internal readonly record struct RichTextReplacement(RichTextRange Range, string Text, RichTextCharacterFormat Format);

public sealed partial class RichTextDocumentSnapshot
{
    // Replacements are disjoint and ordered from right to left, in the source's
    // coordinates. This is the same order used by the checked editor batch API.
    internal RichTextDocumentSnapshot ReplaceTextBatch(IReadOnlyList<RichTextReplacement> replacements)
    {
        var ordered = replacements.Reverse().ToArray();
        var length = checked((int)(Length + ordered.Sum(static edit => (long)edit.Text.Length - edit.Range.Length)));
        var text = new StringBuilder(length);
        var runs = new List<RichTextRun>(_runs.Length + ordered.Length * 2);
        var newStarts = new int[ordered.Length];
        var newEnds = new int[ordered.Length];
        var shifts = new int[ordered.Length];
        var runIndex = 0;
        var oldPosition = 0;
        var shift = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var edit = ordered[index];
            text.Append(Text, oldPosition, edit.Range.Start - oldPosition);
            CopyRuns(oldPosition, edit.Range.Start, shift);
            newStarts[index] = edit.Range.Start + shift;
            text.Append(edit.Text);
            if (edit.Text.Length != 0)
                AddRun(runs, new(newStarts[index], edit.Text.Length, edit.Format));

            newEnds[index] = newStarts[index] + edit.Text.Length;
            shift += edit.Text.Length - edit.Range.Length;
            shifts[index] = shift;
            oldPosition = edit.Range.End;
        }

        text.Append(Text, oldPosition, Length - oldPosition);
        CopyRuns(oldPosition, Length, shift);
        var resultText = text.ToString();
        var paragraphs = new List<RichTextParagraph>();
        var editIndex = 0;
        shift = 0;
        foreach (var start in EnumerateParagraphStarts(resultText))
        {
            // Include the preceding edit's end: a replacement ending with a newline
            // owns that boundary unless it also removed an old paragraph terminator.
            while (editIndex < ordered.Length && newEnds[editIndex] < start)
                shift = shifts[editIndex++];

            RichTextParagraphFormat format;
            if (editIndex == ordered.Length || start < newStarts[editIndex])
                format = GetParagraphFormat(start - shift);
            else
            {
                var edit = ordered[editIndex];
                if (start == newStarts[editIndex])
                    format = GetParagraphFormat(edit.Range.Start);
                else if (start < newEnds[editIndex] || edit.Range.IsEmpty || edit.Range.End == 0 || Text[edit.Range.End - 1] != '\n')
                {
                    format = GetParagraphFormat(edit.Range.Start);
                    if (format.List is { } item)
                        format = format with { List = new RichTextListItemFormat(item.ListId, item.Level), NativeList = null };
                }
                else
                    format = GetParagraphFormat(edit.Range.End);
            }

            paragraphs.Add(new(start, format));
        }

        // Preserve the established boundary/deletion rules for semantic ranges.
        // Unlike rebuilding snapshots, these steps do not copy text or paragraph indexes.
        var links = _links;
        var fields = _fields;
        var images = _images;
        foreach (var edit in replacements)
        {
            if (!links.IsEmpty)
                links = [.. RemapRanges(links, edit.Range.Start, edit.Range.End, edit.Text.Length)];
            if (!fields.IsEmpty)
                fields = [.. RemapRanges(fields, edit.Range.Start, edit.Range.End, edit.Text.Length)];
            if (!images.IsEmpty)
                images = [.. images.Where(image => image.Position < edit.Range.Start || image.Position >= edit.Range.End)
                    .Select(image => image.Position >= edit.Range.End
                        ? image with { Position = image.Position + edit.Text.Length - edit.Range.Length } : image)];
        }

        return new(resultText, runs, paragraphs, links, fields, images, DefaultCharacterFormat,
            DefaultParagraphFormat, _metadata, _listPictures.Values, _lists);

        void CopyRuns(int start, int end, int delta)
        {
            while (runIndex < _runs.Length && _runs[runIndex].End <= start)
                runIndex++;
            while (runIndex < _runs.Length && _runs[runIndex].Start < end)
            {
                var run = _runs[runIndex];
                var first = Math.Max(start, run.Start);
                var last = Math.Min(end, run.End);
                if (first < last)
                    AddRun(runs, new(first + delta, last - first, run.Format));
                if (run.End > end)
                    break;

                runIndex++;
            }
        }
    }
}
