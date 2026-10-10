namespace RichEdit.Core;

/// <summary>An opaque document-local structural node identity. Zero is invalid.</summary>
public readonly record struct RichNodeId(long Value)
{
    /// <summary>Gets whether the identity is valid.</summary>
    public bool IsValid => Value > 0;
}

/// <summary>An opaque document-local reusable style identity. Zero is invalid.</summary>
public readonly record struct RichStyleId(int Value)
{
    /// <summary>Gets whether the identity is valid.</summary>
    public bool IsValid => Value > 0;
}

/// <summary>An opaque, RGB authored color. Transparency belongs to view presentation.</summary>
public readonly record struct RichColor(byte Red, byte Green, byte Blue);

/// <summary>A physical document length with RTF twip precision: twenty twips per point.</summary>
public readonly record struct RichLength(int Twips)
{
    /// <summary>Gets the length in typographic points.</summary>
    public double Points => Twips / 20d;

    /// <summary>Quantizes points to the nearest twip, with midpoints rounded away from zero.</summary>
    public static RichLength FromPoints(double points)
    {
        if (!double.IsFinite(points)) throw new ArgumentOutOfRangeException(nameof(points));
        var twips = Math.Round(points * 20, MidpointRounding.AwayFromZero);
        if (twips < int.MinValue || twips > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(points));
        return new((int)twips);
    }
}

/// <summary>A positive font size represented in RTF half-points.</summary>
public readonly record struct RichFontSize
{
    /// <summary>Creates a positive font size in half-points.</summary>
    public RichFontSize(int halfPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfPoints);
        HalfPoints = halfPoints;
    }
    /// <summary>Gets the exact RTF half-point value.</summary>
    public int HalfPoints { get; }
    /// <summary>Gets the size in typographic points.</summary>
    public double Points => HalfPoints / 2d;
    /// <summary>Quantizes positive points to the nearest positive half-point.</summary>
    public static RichFontSize FromPoints(double points)
    {
        if (!double.IsFinite(points) || points <= 0 || points * 2 > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(points));
        return new(Math.Max(1, (int)Math.Round(points * 2, MidpointRounding.AwayFromZero)));
    }
}

/// <summary>Distinguishes inheritance, an explicit value, and the schema's initial value.</summary>
public enum StyleValueKind
{
    /// <summary>Use the preceding value in the cascade.</summary>
    Inherit,
    /// <summary>Use the explicit value, including an explicit neutral value.</summary>
    Value,
    /// <summary>Use the schema's initial value, bypassing inherited formatting.</summary>
    Initial,
}

/// <summary>A typed cascading style value. The default value means inherit.</summary>
public readonly record struct StyleValue<T>
{
    private readonly T? _value;
    private StyleValue(StyleValueKind kind, T? value) { Kind = kind; _value = value; }
    /// <summary>Gets the cascade operation.</summary>
    public StyleValueKind Kind { get; }
    /// <summary>Gets the explicit value, throwing when this operation is inherit or initial.</summary>
    public T Value => Kind == StyleValueKind.Value ? _value! : throw new InvalidOperationException("This style operation has no explicit value.");
    /// <summary>Gets an operation that inherits preceding formatting.</summary>
    public static StyleValue<T> Inherit => default;
    /// <summary>Gets an operation that resets this property to the schema initial value.</summary>
    public static StyleValue<T> Initial => new(StyleValueKind.Initial, default);
    /// <summary>Creates an explicit value, preserving false, zero, none, and null distinctly from inheritance.</summary>
    public static StyleValue<T> FromValue(T value) => new(StyleValueKind.Value, value);
    /// <summary>Resolves this operation against inherited and initial values.</summary>
    public T Resolve(T inherited, T initial) => Kind switch
    {
        StyleValueKind.Inherit => inherited,
        StyleValueKind.Value => _value!,
        StyleValueKind.Initial => initial,
        _ => throw new InvalidOperationException("Invalid style operation."),
    };
    /// <summary>Converts a typed value to an explicit cascade value.</summary>
    public static implicit operator StyleValue<T>(T value) => FromValue(value);
}
