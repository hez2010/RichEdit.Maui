using TextEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    // RichEdit exposes the current story, but TextChanging has no changed range.
    // Compare its one returned string directly with source chunks. Only the
    // replacement needs line-ending conversion or a new managed string.
    internal static TextReplacement FindNativeSourceReplacement(TextSnapshot before, string raw, TextSpan hint)
    {
        hint.Validate(before.Length);
        var length = raw.EndsWith('\r') ? raw.Length - 1 : raw.Length;
        var native = raw.AsSpan(0, length);
        var insertedLength = length - before.Length + hint.Length;
        if (insertedLength >= 0 &&
            CommonNativeEdge(before, new(0, hint.Start), native[..hint.Start], false) == hint.Start &&
            CommonNativeEdge(before, new(hint.End, before.Length - hint.End), native[(hint.Start + insertedLength)..], false) == before.Length - hint.End)
        {
            var inserted = native.Slice(hint.Start, insertedLength);
            if (hint.Length == insertedLength && CommonNativeEdge(before, hint, inserted, false) == hint.Length)
                return new(new(hint.Start, 0), "");
            return new(hint, NormalizeNativeSource(inserted));
        }

        var prefix = CommonNativeEdge(before, new(0, before.Length), native, false);
        var suffix = CommonNativeEdge(before, new(prefix, before.Length - prefix), native[prefix..], true);
        return new(new(prefix, before.Length - prefix - suffix), NormalizeNativeSource(native.Slice(prefix, length - prefix - suffix)));
    }

    private static string NormalizeNativeSource(ReadOnlySpan<char> text) =>
        text.ToString().Replace('\r', '\n').Replace('\v', RichTextDocument.SoftLineBreakCharacter);

    private static int CommonNativeEdge(TextSnapshot before, TextSpan range, ReadOnlySpan<char> native, bool reverse)
    {
        var limit = Math.Min(range.Length, native.Length);
        range = new(reverse ? range.End - limit : range.Start, limit);
        Span<char> normalized = stackalloc char[2048];
        var compared = 0;
        foreach (var chunk in reverse ? before.GetChunksReverse(range) : before.GetChunks(range))
        {
            for (var used = 0; used < chunk.Length;)
            {
                var count = Math.Min(normalized.Length, chunk.Length - used);
                var source = chunk.Span.Slice(reverse ? chunk.Length - used - count : used, count);
                scoped ReadOnlySpan<char> current = native.Slice(reverse ? native.Length - compared - count : compared, count);
                if (current.ContainsAny('\r', '\v'))
                {
                    current.CopyTo(normalized);
                    var converted = normalized[..count];
                    converted.Replace('\r', '\n');
                    converted.Replace('\v', RichTextDocument.SoftLineBreakCharacter);
                    current = converted;
                }
                var equal = 0;
                if (!reverse) equal = source.CommonPrefixLength(current);
                else if (source.SequenceEqual(current)) equal = count;
                else while (equal < count && source[^(equal + 1)] == current[^(equal + 1)]) equal++;
                compared += equal;
                if (equal != count) return compared;
                used += count;
            }
        }
        return compared;
    }
}
