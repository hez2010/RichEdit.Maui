namespace RichEdit.Core;

/// <summary>Edges receiving a uniform paragraph border.</summary>
[Flags]
public enum RichBorderSides
{
    /// <summary>No edges.</summary>
    None = 0,
    /// <summary>Left edge.</summary>
    Left = 1,
    /// <summary>Top edge.</summary>
    Top = 2,
    /// <summary>Right edge.</summary>
    Right = 4,
    /// <summary>Bottom edge.</summary>
    Bottom = 8,
    /// <summary>All four edges.</summary>
    All = Left | Top | Right | Bottom,
}

/// <summary>A paragraph border's line pattern.</summary>
public enum RichBorderStyle
{
    /// <summary>One solid line.</summary>
    Single,
    /// <summary>Two solid lines.</summary>
    Double,
    /// <summary>A dotted line.</summary>
    Dotted,
    /// <summary>A dashed line.</summary>
    Dashed,
}

/// <summary>A uniform border on selected paragraph edges. Use null to disable a border.</summary>
/// <param name="Sides">One or more edges.</param>
/// <param name="Style">The line pattern.</param>
/// <param name="Width">The RTF pen width, from zero (native hairline) through 255 twips.</param>
/// <param name="Color">The border color, or null for automatic.</param>
public sealed record RichBorder(RichBorderSides Sides, RichBorderStyle Style, RichLength Width, RichColor? Color = null)
{
    internal void Validate()
    {
        if (Sides == RichBorderSides.None || (Sides & ~RichBorderSides.All) != 0 || !Enum.IsDefined(Style) || Width.Twips is < 0 or > 255)
            throw new ArgumentException("Borders require selected edges, a valid line style and a width from zero through 255 twips.");
    }
}
