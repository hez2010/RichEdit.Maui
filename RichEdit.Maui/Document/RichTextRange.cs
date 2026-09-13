namespace RichEdit.Maui;

/// <summary>
/// Identifies a half-open UTF-16 range in a rich-text document.
/// </summary>
public readonly record struct RichTextRange
{
    /// <summary>
    /// Initializes a new document range.
    /// </summary>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The number of UTF-16 code units in the range.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start"/> or <paramref name="length"/> is negative, or their sum
    /// is greater than <see cref="int.MaxValue"/>.
    /// </exception>
    public RichTextRange(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _ = checked(start + length);
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets an empty range at the beginning of a document.
    /// </summary>
    public static RichTextRange Empty { get; } = new(0, 0);

    /// <summary>
    /// Gets the zero-based UTF-16 start offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of UTF-16 code units in the range.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the exclusive UTF-16 end offset.
    /// </summary>
    public int End => Start + Length;

    /// <summary>
    /// Gets a value indicating whether the range contains no text.
    /// </summary>
    public bool IsEmpty => Length == 0;

    internal Range ToRange() => Start..End;

    internal RichTextRange Clamp(int documentLength)
    {
        var start = Math.Clamp(Start, 0, documentLength);
        return new RichTextRange(start, Math.Clamp(Length, 0, documentLength - start));
    }

    internal void Validate(int documentLength, string parameterName)
    {
        if (End > documentLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The range [{Start}, {End}) exceeds the document length {documentLength}.");
        }
    }
}
