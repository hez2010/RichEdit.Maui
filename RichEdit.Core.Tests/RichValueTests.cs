using RichEdit.Core;
using Xunit;

namespace RichEdit.Core.Tests;

public class RichValueTests
{
    [Fact]
    public void ExplicitNeutralValuesDoNotInherit()
    {
        StyleValue<bool> disabled = false;
        Assert.False(disabled.Resolve(true, false));
        Assert.True(StyleValue<bool>.Inherit.Resolve(true, false));
        Assert.False(StyleValue<bool>.Initial.Resolve(true, false));
        Assert.NotEqual(StyleValue<bool>.Inherit, disabled);
        Assert.NotEqual(StyleValue<bool>.Initial, disabled);
        Assert.Throws<InvalidOperationException>(() => StyleValue<bool>.Inherit.Value);
        StyleValue<string?> clear = StyleValue<string?>.FromValue(null);
        Assert.Null(clear.Resolve("inherited", "initial"));
    }

    [Theory]
    [InlineData(12.025, 241)]
    [InlineData(-12.025, -241)]
    [InlineData(0.024, 0)]
    public void AuthoredLengthsAreCanonicalTwips(double points, int twips)
    {
        var length = RichLength.FromPoints(points);
        Assert.Equal(twips, length.Twips);
        Assert.Equal(length, RichLength.FromPoints(length.Points));
    }

    [Fact]
    public void FontSizesAreCanonicalHalfPoints()
    {
        Assert.Equal(12.5, RichFontSize.FromPoints(12.3).Points);
        Assert.Equal(0.5, RichFontSize.FromPoints(0.1).Points);
        Assert.Throws<ArgumentOutOfRangeException>(() => RichFontSize.FromPoints(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => RichFontSize.FromPoints(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RichLength.FromPoints(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => RichLength.FromPoints(double.MaxValue));
    }
}
