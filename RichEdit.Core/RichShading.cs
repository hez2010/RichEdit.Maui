namespace RichEdit.Core;

/// <summary>An RTF shading fill with coverage from zero through ten thousand.</summary>
public readonly record struct RichShading
{
    /// <summary>Creates shading. Null colors use the text engine's automatic foreground/background.</summary>
    public RichShading(int coverage, RichColor? foreground = null, RichColor? background = null)
    {
        if (coverage is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(coverage));
        Coverage = coverage; Foreground = foreground; Background = background;
    }
    /// <summary>Gets foreground coverage in hundredths of one percent.</summary>
    public int Coverage { get; }
    /// <summary>Gets the foreground color, or null for automatic.</summary>
    public RichColor? Foreground { get; }
    /// <summary>Gets the background color, or null for automatic.</summary>
    public RichColor? Background { get; }
    internal RichColor? SolidColor => Coverage == 10000 ? Foreground : Coverage == 0 ? Background : null;
}
