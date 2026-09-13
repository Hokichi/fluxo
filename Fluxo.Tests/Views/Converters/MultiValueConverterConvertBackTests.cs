using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Fluxo.Resources.Converters;
using Xunit;

namespace Fluxo.Tests.Views.Converters;

public sealed class MultiValueConverterConvertBackTests
{
    public static TheoryData<IMultiValueConverter> Cases => new()
    {
        new BorderCornerClipConverter(),
        new Fluxo.Resources.Converters.CornerRadiusConverter()
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConvertBack_ReturnsDoNothing_ForEveryTarget(IMultiValueConverter converter)
    {
        var targetTypes = new[] { typeof(CornerRadius), typeof(double), typeof(double) };

        var result = converter.ConvertBack(
            Geometry.Empty,
            targetTypes,
            null!,
            CultureInfo.InvariantCulture);

        Assert.Equal(targetTypes.Length, result.Length);
        Assert.All(result, item => Assert.Same(Binding.DoNothing, item));
    }
}
