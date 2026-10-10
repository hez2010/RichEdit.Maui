namespace RichEdit.Core;

/// <summary>A document-owned annotation identity.</summary>
public readonly record struct RichAnnotationId(long Value)
{
    /// <summary>Gets whether the identity is valid.</summary>
    public bool IsValid => Value > 0;
}

/// <summary>A persistent named anchor. A null range retains a bookmark whose anchored content was removed.</summary>
public sealed record RichBookmark(RichAnnotationId Id, string Name, RichTextSelection? Range)
{
    /// <summary>Gets whether the bookmark has no surviving anchor.</summary>
    public bool IsOrphaned => Range is null;
}

/// <summary>A persistent comment with an optional surviving text anchor.</summary>
public sealed record RichComment(RichAnnotationId Id, RichTextSelection? Range, string Text, string Author, DateTimeOffset? Created)
{
    /// <summary>Gets whether the comment's anchored content was removed.</summary>
    public bool IsOrphaned => Range is null;
}
