using Xunit;
using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ColorValuesTests
{
    [Fact]
    public void ValuesAreLimitedToTheSliderRanges()
    {
        Assert.Equal(new ColorValues(80, 120, 0.3, 0, 359), new ColorValues(79, 121, 0.1, -5, 400).ClampedToSliders());
        Assert.Equal(new ColorValues(120, 80, 2.8, 100, 0), new ColorValues(130, 70, 3, 101, -1).ClampedToSliders());
        Assert.Equal(ColorValues.Defaults, ColorValues.Defaults.ClampedToSliders());
    }

    // (int)Math.Round rounds half to even, as in the original.
    [Theory]
    [InlineData(50.5, 50)]
    [InlineData(51.5, 52)]
    [InlineData(49.6, 50)]
    public void VibranceAndHueRoundHalfToEven(double value, int expected)
    {
        var values = new ColorValues(100, 100, 1, value, value);
        Assert.Equal(expected, values.RoundedVibrance);
        Assert.Equal(expected, values.RoundedHue);
    }
}
