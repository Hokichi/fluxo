using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class NumericUpDownTests
{
    [Theory]
    [InlineData(-3, 0, 10, 0)]
    [InlineData(13, 0, 10, 10)]
    [InlineData(4.25, 0, 10, 4.25)]
    [InlineData(4.25, 10, 0, 10)]
    public void NumericUpDown_CoerceValueWithinLimits_ClampsToNormalizedRange(
        decimal value,
        decimal lowerLimit,
        decimal upperLimit,
        decimal expected)
    {
        var actual = NumericUpDown.CoerceValueWithinLimits(value, lowerLimit, upperLimit);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("4.25", 0, 10, 4.25)]
    [InlineData("13", 0, 10, 10)]
    [InlineData("-3", 0, 10, 0)]
    [InlineData(" 7.5 ", 0, 10, 7.5)]
    public void NumericUpDown_TryParseValueText_ParsesDecimalTextAndClampsToLimits(
        string text,
        decimal lowerLimit,
        decimal upperLimit,
        decimal expected)
    {
        var parsed = NumericUpDown.TryParseValueText(text, lowerLimit, upperLimit, out var actual);

        Assert.True(parsed);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("4.2.1")]
    public void NumericUpDown_TryParseValueText_ReturnsFalseForInvalidText(string text)
    {
        var parsed = NumericUpDown.TryParseValueText(text, 0, 10, out _);

        Assert.False(parsed);
    }

}
