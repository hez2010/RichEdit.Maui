namespace RichEdit.Core;

/// <summary>How paragraph line height is calculated.</summary>
public enum RichLineSpacingKind
{
    /// <summary>Use native font metrics and inline object heights.</summary>
    Automatic,
    /// <summary>Use at least the specified height.</summary>
    AtLeast,
    /// <summary>Use the specified fixed height.</summary>
    Exactly,
    /// <summary>Scale the native line height.</summary>
    Multiple,
}

/// <summary>RTF-exact line spacing in twips or 1/240-line increments.</summary>
public readonly record struct RichLineSpacing
{
    private readonly int _value;
    private RichLineSpacing(RichLineSpacingKind kind, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Kind = kind; _value = value;
    }
    /// <summary>Gets the spacing rule.</summary>
    public RichLineSpacingKind Kind { get; }
    /// <summary>Gets the absolute height for fixed/minimum spacing, or null.</summary>
    public RichLength? Height => Kind is RichLineSpacingKind.AtLeast or RichLineSpacingKind.Exactly ? new(_value) : null;
    /// <summary>Gets the line-height factor for multiple spacing, or null.</summary>
    public double? Factor => Kind == RichLineSpacingKind.Multiple ? _value / 240d : null;
    /// <summary>Uses the text engine's automatic line height.</summary>
    public static RichLineSpacing Automatic => default;
    /// <summary>Uses a positive minimum height.</summary>
    public static RichLineSpacing AtLeast(RichLength height) => new(RichLineSpacingKind.AtLeast, height.Twips);
    /// <summary>Uses a positive fixed height.</summary>
    public static RichLineSpacing Exactly(RichLength height) => new(RichLineSpacingKind.Exactly, height.Twips);
    /// <summary>Uses a positive line factor, rounded to the nearest 1/240 line. Use 1, 1.5 or 2 for common spacing.</summary>
    public static RichLineSpacing Multiple(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        return new(RichLineSpacingKind.Multiple, checked((int)Math.Round(factor * 240)));
    }
    internal int RtfValue => Kind == RichLineSpacingKind.Exactly ? -_value : _value;
    internal static RichLineSpacing FromRtf(int value, bool multiple) => value == 0 ? default : multiple
        ? new(RichLineSpacingKind.Multiple, checked(Math.Abs(value))) : value < 0
            ? Exactly(new(checked(-value))) : AtLeast(new(value));
}
