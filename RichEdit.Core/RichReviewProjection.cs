namespace RichEdit.Core;

/// <summary>The authored state shown by a read-only review projection.</summary>
public enum RichReviewMode
{
    /// <summary>Shows current content with all pending changes applied.</summary>
    Final,
    /// <summary>Shows content with all pending changes rejected.</summary>
    Original,
}

/// <summary>A frozen review state and address maps bound to its exact source snapshot.</summary>
public sealed class RichReviewProjection
{
    private readonly RichAddressMap _toReview;
    private readonly RichAddressMap _toSource;
    internal RichReviewProjection(RichDocumentSnapshot source, RichDocumentSnapshot snapshot, RichReviewMode mode, RichAddressMap map)
    { Source = source; Snapshot = snapshot; Mode = mode; _toReview = map; _toSource = map.Invert(); }
    /// <summary>Gets the canonical snapshot from which this projection was captured.</summary>
    public RichDocumentSnapshot Source { get; }
    /// <summary>Gets the immutable displayed content. It owns no undo history.</summary>
    public RichDocumentSnapshot Snapshot { get; }
    /// <summary>Gets the displayed review state.</summary>
    public RichReviewMode Mode { get; }
    /// <summary>Maps a canonical point into the review state. Removed paragraphs return null; text replacements use point affinity.</summary>
    public RichTextPoint? ToReview(RichTextPoint point)
    {
        Source.ValidatePoint(point);
        return Validate(_toReview.Map(point), Snapshot);
    }
    /// <summary>Maps a review point to canonical content. Retained-only paragraphs return null; text replacements use point affinity.</summary>
    public RichTextPoint? ToSource(RichTextPoint point)
    {
        Snapshot.ValidatePoint(point);
        return Validate(_toSource.Map(point), Source);
    }
    private static RichTextPoint? Validate(RichTextPoint? point, RichDocumentSnapshot snapshot) =>
        point is { } value && snapshot.Nodes.GetValueOrDefault(value.Paragraph) is RichParagraph &&
        value.Offset <= snapshot.GetParagraphLength(value.Paragraph) ? value : null;
}
