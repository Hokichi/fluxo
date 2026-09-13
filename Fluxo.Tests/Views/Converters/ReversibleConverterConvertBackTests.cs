using System.Globalization;
using System.Windows.Data;
using Fluxo.Resources.Converters;
using Xunit;

namespace Fluxo.Tests.Views.Converters;

public sealed class ReversibleConverterConvertBackTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BoolNegation_ConvertBack_NegatesBoolean(bool value, bool expected)
    {
        var converter = new BoolNegationConverter();

        var result = converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BoolNegation_ConvertBack_ReturnsDoNothing_ForInvalidValue()
    {
        var converter = new BoolNegationConverter();

        var result = converter.ConvertBack("invalid", typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Same(Binding.DoNothing, result);
    }

    [Fact]
    public void MoneyAmount_ConvertBack_ReturnsDecimal_ForValidText()
    {
        var converter = new MoneyAmountToCanonicalConverter();

        var result = converter.ConvertBack("1,234.5", typeof(decimal), null!, CultureInfo.InvariantCulture);

        Assert.Equal(1234.5m, result);
    }

    [Fact]
    public void MoneyAmount_ConvertBack_ReturnsDoNothing_ForInvalidText()
    {
        var converter = new MoneyAmountToCanonicalConverter();

        var result = converter.ConvertBack("invalid", typeof(decimal), null!, CultureInfo.InvariantCulture);

        Assert.Same(Binding.DoNothing, result);
    }
}
