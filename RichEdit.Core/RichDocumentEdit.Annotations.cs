namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Adds a persistent named anchor. Deleting its complete text range or an endpoint's paragraph retains an orphaned bookmark.</summary>
    public RichAnnotationId AddBookmark(string name, RichTextSelection range)
    {
        VerifyOpen();
        RichDocumentSnapshot.ValidateBookmarkName(name);
        Candidate(validate: false).NormalizeSelection(range);
        if (_bookmarks.Values.Any(bookmark => string.Equals(bookmark.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The bookmark name is already used.", nameof(name));
        var id = new RichAnnotationId(Allocate().Value);
        _bookmarks.Add(id, new(id, name, range));
        return id;
    }

    /// <summary>Adds a persistent comment, normalizing its optional timestamp to a UTC minute.</summary>
    public RichAnnotationId AddComment(RichTextSelection range, string text, string author = "", DateTimeOffset? created = null)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(author);
        Candidate(validate: false).NormalizeSelection(range);
        if (created is { } date)
        {
            var utc = date.UtcDateTime;
            if (utc.Year is < 1900 or > 2411) throw new ArgumentOutOfRangeException(nameof(created));
            created = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        }
        var id = new RichAnnotationId(Allocate().Value);
        _comments.Add(id, new(id, range, text, author, created));
        return id;
    }

    /// <summary>Changes comment text without changing its identity, author, timestamp, or anchor.</summary>
    public void SetCommentText(RichAnnotationId id, string text)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(text);
        if (!_comments.TryGetValue(id, out var comment)) throw new ArgumentException("The comment does not exist.", nameof(id));
        if (comment.Text != text) _comments[id] = comment with { Text = text };
    }

    /// <summary>Reattaches a bookmark or comment to a current rich range.</summary>
    public void SetAnnotationRange(RichAnnotationId id, RichTextSelection range)
    {
        VerifyOpen();
        Candidate(validate: false).NormalizeSelection(range);
        if (_comments.TryGetValue(id, out var comment)) _comments[id] = comment with { Range = range };
        else if (_bookmarks.TryGetValue(id, out var bookmark)) _bookmarks[id] = bookmark with { Range = range };
        else throw new ArgumentException("The annotation does not exist.", nameof(id));
    }

    /// <summary>Removes one persistent bookmark or comment in this transaction.</summary>
    public void RemoveAnnotation(RichAnnotationId id)
    {
        VerifyOpen();
        if (!_comments.Remove(id) && !_bookmarks.Remove(id)) throw new ArgumentException("The annotation does not exist.", nameof(id));
    }

    private void Record(RichAddressMap.Step step)
    {
        _maps.Add(step);
        foreach (var id in _comments.Keys.ToArray())
        {
            var comment = _comments[id];
            var range = Map(comment.Range);
            if (range != comment.Range) _comments[id] = comment with { Range = range };
        }
        foreach (var id in _bookmarks.Keys.ToArray())
        {
            var bookmark = _bookmarks[id];
            var range = Map(bookmark.Range);
            if (range != bookmark.Range) _bookmarks[id] = bookmark with { Range = range };
        }

        RichTextSelection? Map(RichTextSelection? range)
        {
            if (range is null) return null;
            if (step is RichAddressMap.Text text && range.Anchor.Paragraph == text.Paragraph && range.Active.Paragraph == text.Paragraph &&
                range.Anchor.Offset != range.Active.Offset && text.Start <= Math.Min(range.Anchor.Offset, range.Active.Offset) &&
                text.Start + text.Removed >= Math.Max(range.Anchor.Offset, range.Active.Offset)) return null;
            var anchor = step.Map(range.Anchor);
            var active = step.Map(range.Active);
            return anchor is { } first && active is { } second ? new(first, second) : null;
        }
    }
}
