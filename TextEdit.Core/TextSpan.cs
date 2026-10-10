namespace TextEdit.Core;

/// <summary>A half-open range of UTF-16 code units.</summary>
public readonly record struct TextSpan
{
    /// <summary>Creates a nonnegative, non-overflowing range.</summary>
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _ = checked(start + length);
        Start = start;
        Length = length;
    }

    /// <summary>Gets the inclusive start.</summary>
    public int Start { get; }

    /// <summary>Gets the UTF-16 length.</summary>
    public int Length { get; }

    /// <summary>Gets the exclusive end.</summary>
    public int End => Start + Length;

    /// <summary>Gets whether the range is a caret position.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Rejects a range outside the specified text length.</summary>
    public void Validate(int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);
        if (End > textLength)
            throw new ArgumentOutOfRangeException(nameof(textLength), "The range extends beyond the text.");
    }
}
