namespace RichEdit.Core;

/// <summary>Typed cascading paragraph properties. Unspecified properties inherit preceding formatting.</summary>
public sealed record RichParagraphStyle
{
    /// <summary>Paragraph alignment.</summary>
    public StyleValue<RichAlignment> Alignment { get; init; }

    /// <summary>Paragraph writing direction.</summary>
    public StyleValue<RichDirection> Direction { get; init; }

    /// <summary>The leading indent.</summary>
    public StyleValue<RichLength> LeadingIndent { get; init; }

    /// <summary>The trailing indent.</summary>
    public StyleValue<RichLength> TrailingIndent { get; init; }

    /// <summary>The first-line indent relative to the leading indent.</summary>
    public StyleValue<RichLength> FirstLineIndent { get; init; }

    /// <summary>Space before the paragraph.</summary>
    public StyleValue<RichLength> SpaceBefore { get; init; }

    /// <summary>Space after the paragraph.</summary>
    public StyleValue<RichLength> SpaceAfter { get; init; }

    /// <summary>The semantic heading level, from one through nine, or null for body text.</summary>
    public StyleValue<int?> HeadingLevel { get; init; }

    /// <summary>Whether paragraph layout should keep this paragraph with its successor.</summary>
    public StyleValue<bool> KeepWithNext { get; init; }

    /// <summary>Whether paragraph layout should keep its lines together.</summary>
    public StyleValue<bool> KeepTogether { get; init; }

    /// <summary>Whether paragraph layout avoids isolated first and last lines.</summary>
    public StyleValue<bool> WidowControl { get; init; }

    /// <summary>The paragraph background, or null for no fill.</summary>
    public StyleValue<RichColor?> Background { get; init; }

    internal RichParagraphStyle Overlay(RichParagraphStyle child) => new()
    {
        Alignment = child.Alignment.Kind == StyleValueKind.Inherit ? Alignment : child.Alignment,
        Direction = child.Direction.Kind == StyleValueKind.Inherit ? Direction : child.Direction,
        LeadingIndent = child.LeadingIndent.Kind == StyleValueKind.Inherit ? LeadingIndent : child.LeadingIndent,
        TrailingIndent = child.TrailingIndent.Kind == StyleValueKind.Inherit ? TrailingIndent : child.TrailingIndent,
        FirstLineIndent = child.FirstLineIndent.Kind == StyleValueKind.Inherit ? FirstLineIndent : child.FirstLineIndent,
        SpaceBefore = child.SpaceBefore.Kind == StyleValueKind.Inherit ? SpaceBefore : child.SpaceBefore,
        SpaceAfter = child.SpaceAfter.Kind == StyleValueKind.Inherit ? SpaceAfter : child.SpaceAfter,
        HeadingLevel = child.HeadingLevel.Kind == StyleValueKind.Inherit ? HeadingLevel : child.HeadingLevel,
        KeepWithNext = child.KeepWithNext.Kind == StyleValueKind.Inherit ? KeepWithNext : child.KeepWithNext,
        KeepTogether = child.KeepTogether.Kind == StyleValueKind.Inherit ? KeepTogether : child.KeepTogether,
        WidowControl = child.WidowControl.Kind == StyleValueKind.Inherit ? WidowControl : child.WidowControl,
        Background = child.Background.Kind == StyleValueKind.Inherit ? Background : child.Background,
    };

    internal ResolvedParagraphStyle Apply(ResolvedParagraphStyle inherited) => new()
    {
        Alignment = Alignment.Resolve(inherited.Alignment, RichAlignment.Left),
        Direction = Direction.Resolve(inherited.Direction, RichDirection.Automatic),
        LeadingIndent = LeadingIndent.Resolve(inherited.LeadingIndent, default),
        TrailingIndent = TrailingIndent.Resolve(inherited.TrailingIndent, default),
        FirstLineIndent = FirstLineIndent.Resolve(inherited.FirstLineIndent, default),
        SpaceBefore = SpaceBefore.Resolve(inherited.SpaceBefore, default),
        SpaceAfter = SpaceAfter.Resolve(inherited.SpaceAfter, default),
        HeadingLevel = HeadingLevel.Resolve(inherited.HeadingLevel, null),
        KeepWithNext = KeepWithNext.Resolve(inherited.KeepWithNext, false),
        KeepTogether = KeepTogether.Resolve(inherited.KeepTogether, false),
        WidowControl = WidowControl.Resolve(inherited.WidowControl, true),
        Background = Background.Resolve(inherited.Background, null),
    };
}

/// <summary>Resolved paragraph properties after the authored style cascade.</summary>
public sealed record ResolvedParagraphStyle
{
    /// <summary>Paragraph alignment.</summary>
    public RichAlignment Alignment { get; init; } = RichAlignment.Left;

    /// <summary>Paragraph writing direction.</summary>
    public RichDirection Direction { get; init; } = RichDirection.Automatic;

    /// <summary>The leading indent.</summary>
    public RichLength LeadingIndent { get; init; } = default;

    /// <summary>The trailing indent.</summary>
    public RichLength TrailingIndent { get; init; } = default;

    /// <summary>The first-line indent relative to the leading indent.</summary>
    public RichLength FirstLineIndent { get; init; } = default;

    /// <summary>Space before the paragraph.</summary>
    public RichLength SpaceBefore { get; init; } = default;

    /// <summary>Space after the paragraph.</summary>
    public RichLength SpaceAfter { get; init; } = default;

    /// <summary>The semantic heading level, from one through nine, or null for body text.</summary>
    public int? HeadingLevel { get; init; } = null;

    /// <summary>Whether paragraph layout should keep this paragraph with its successor.</summary>
    public bool KeepWithNext { get; init; } = false;

    /// <summary>Whether paragraph layout should keep its lines together.</summary>
    public bool KeepTogether { get; init; } = false;

    /// <summary>Whether paragraph layout avoids isolated first and last lines.</summary>
    public bool WidowControl { get; init; } = true;

    /// <summary>The paragraph background, or null for no fill.</summary>
    public RichColor? Background { get; init; } = null;

}
