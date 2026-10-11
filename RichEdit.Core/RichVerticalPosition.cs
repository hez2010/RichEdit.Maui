namespace RichEdit.Core;

/// <summary>Vertical character placement.</summary>
public enum RichVerticalPositionKind
{
    /// <summary>Normal baseline and size.</summary>
    Normal,
    /// <summary>Native superscript placement and size.</summary>
    Superscript,
    /// <summary>Native subscript placement and size.</summary>
    Subscript,
    /// <summary>A manual shift without script sizing.</summary>
    Offset,
}

/// <summary>One RTF vertical-placement mode, including signed manual offsets in half-points.</summary>
public readonly record struct RichVerticalPosition
{
    private RichVerticalPosition(RichVerticalPositionKind kind, int halfPoints) { Kind = kind; HalfPoints = halfPoints; }
    /// <summary>Gets the placement mode.</summary>
    public RichVerticalPositionKind Kind { get; }
    /// <summary>Gets the signed manual baseline shift in half-points, or zero for a script mode.</summary>
    public int HalfPoints { get; }
    /// <summary>Gets the manual shift in points.</summary>
    public double Points => HalfPoints / 2d;
    /// <summary>Gets normal placement.</summary>
    public static RichVerticalPosition Normal => default;
    /// <summary>Gets native superscript placement.</summary>
    public static RichVerticalPosition Superscript => new(RichVerticalPositionKind.Superscript, 0);
    /// <summary>Gets native subscript placement.</summary>
    public static RichVerticalPosition Subscript => new(RichVerticalPositionKind.Subscript, 0);
    /// <summary>Creates a manual signed offset. Zero normalizes to normal placement.</summary>
    public static RichVerticalPosition Offset(int halfPoints)
    {
        if (halfPoints == int.MinValue) throw new ArgumentOutOfRangeException(nameof(halfPoints));
        return halfPoints == 0 ? Normal : new(RichVerticalPositionKind.Offset, halfPoints);
    }
    internal static RichVerticalPosition FromScript(RichScript script) => script switch
    {
        RichScript.Normal => Normal, RichScript.Superscript => Superscript, RichScript.Subscript => Subscript,
        _ => throw new ArgumentOutOfRangeException(nameof(script)),
    };
    internal RichScript Script => Kind switch { RichVerticalPositionKind.Superscript => RichScript.Superscript,
        RichVerticalPositionKind.Subscript => RichScript.Subscript, _ => RichScript.Normal };
}
