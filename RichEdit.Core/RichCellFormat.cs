namespace RichEdit.Core;

/// <summary>Vertical placement of a table cell's content.</summary>
public enum RichCellVerticalAlignment
{
    /// <summary>Align to the top.</summary>
    Top,
    /// <summary>Center vertically.</summary>
    Center,
    /// <summary>Align to the bottom.</summary>
    Bottom,
}

/// <summary>A single authored border stroke, using standard RTF pen widths.</summary>
public sealed record RichBorderLine(RichBorderStyle Style, RichLength Width, RichColor? Color = null)
{
    internal void Validate() => new RichBorder(RichBorderSides.All, Style, Width, Color).Validate();
}

/// <summary>Independently authored cell border strokes. Null omits that edge.</summary>
public sealed record RichCellBorders(RichBorderLine? Left = null, RichBorderLine? Top = null, RichBorderLine? Right = null, RichBorderLine? Bottom = null)
{
    /// <summary>Applies one stroke to all four edges.</summary>
    public static RichCellBorders Uniform(RichBorderLine line) { ArgumentNullException.ThrowIfNull(line); return new(line, line, line, line); }
    internal void Validate() { Left?.Validate(); Top?.Validate(); Right?.Validate(); Bottom?.Validate(); }
}

/// <summary>Physical cell padding in twips.</summary>
public readonly record struct RichCellPadding(RichLength Left, RichLength Top, RichLength Right, RichLength Bottom)
{
    /// <summary>Applies one physical padding to every side.</summary>
    public static RichCellPadding Uniform(RichLength length) => new(length, length, length, length);
    internal void Validate()
    {
        if (Left.Twips < 0 || Top.Twips < 0 || Right.Twips < 0 || Bottom.Twips < 0)
            throw new ArgumentException("Cell padding cannot be negative.");
    }
}

/// <summary>Authored table-cell appearance, retained independently of selection and view colors.</summary>
public sealed record RichCellFormat
{
    /// <summary>Gets vertical content alignment.</summary>
    public RichCellVerticalAlignment VerticalAlignment { get; init; }
    /// <summary>Gets physical content padding.</summary>
    public RichCellPadding Padding { get; init; }
    /// <summary>Gets independent edge strokes. Null strokes omit authored borders.</summary>
    public RichCellBorders Borders { get; init; } = new();
    /// <summary>Gets optional background and foreground shading.</summary>
    public RichShading? Shading { get; init; }

    internal void Validate()
    {
        if (!Enum.IsDefined(VerticalAlignment)) throw new ArgumentException("Invalid cell vertical alignment.");
        ArgumentNullException.ThrowIfNull(Borders);
        Padding.Validate(); Borders.Validate();
    }
}
