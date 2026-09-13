using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Fluxo.Resources.Converters;
using Xunit;

namespace Fluxo.Tests.Views.Converters;

public sealed class LossyValueConverterConvertBackTests
{
    public static TheoryData<IValueConverter, object> Cases => new()
    {
        { new BoolToVisibilityInvertedConverter(), Visibility.Visible },
        { new DateTimeToRelativeDateConverter(), "Today" },
        { new DifferenceToBrushConverter(), Brushes.Red },
        { new GoalProgressToBrushConverter(), Brushes.Green },
        { new MoneyDisplayConverter(), "1.2K" },
        { new MoneyFullDisplayConverter(), "1,234.50" },
        { new NumberWithCommasConverter(), "1.2K" },
        { new ProgressToArcGeometryConverter(), Geometry.Empty },
        { new SourceSequenceLabelConverter(), "Source 3" }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConvertBack_ReturnsDoNothing(IValueConverter converter, object value)
    {
        var result = converter.ConvertBack(
            value,
            typeof(object),
            null!,
            CultureInfo.InvariantCulture);

        Assert.Same(Binding.DoNothing, result);
    }
}
