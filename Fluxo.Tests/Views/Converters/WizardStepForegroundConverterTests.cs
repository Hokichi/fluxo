using System.Globalization;
using System.Windows.Media;
using Fluxo.Resources.Converters;
using Xunit;

namespace Fluxo.Tests.Views.Converters;

public sealed class WizardStepForegroundConverterTests
{
    [Fact]
    public void WizardStepForegroundConverter_Convert_ReturnsCurrentBrush_ForCurrentLabel()
    {
        var converter = CreateConverter();

        var result = converter.Convert(3, typeof(Brush), "3", CultureInfo.InvariantCulture);

        Assert.Same(converter.CurrentBrush, result);
    }

    [Fact]
    public void WizardStepForegroundConverter_Convert_ReturnsCompletedBrush_ForPriorLabel()
    {
        var converter = CreateConverter();

        var result = converter.Convert(4, typeof(Brush), "2", CultureInfo.InvariantCulture);

        Assert.Same(converter.CompletedBrush, result);
    }

    [Fact]
    public void WizardStepForegroundConverter_Convert_ReturnsFutureBrush_ForLaterLabel()
    {
        var converter = CreateConverter();

        var result = converter.Convert(4, typeof(Brush), "6", CultureInfo.InvariantCulture);

        Assert.Same(converter.FutureBrush, result);
    }

    [Fact]
    public void WizardStepForegroundConverter_Convert_ReturnsFutureBrush_ForInvalidInput()
    {
        var converter = CreateConverter();

        var result = converter.Convert(0, typeof(Brush), "invalid", CultureInfo.InvariantCulture);

        Assert.Same(converter.FutureBrush, result);
    }

    [Fact]
    public void WizardStepForegroundConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = CreateConverter();

        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(
            Brushes.White,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture));
    }

    private static WizardStepForegroundConverter CreateConverter() => new()
    {
        CurrentBrush = Brushes.Red,
        CompletedBrush = Brushes.Green,
        FutureBrush = Brushes.Blue
    };
}
