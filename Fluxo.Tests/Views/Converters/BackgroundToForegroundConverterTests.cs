using Fluxo.Resources.Converters;
using System.Globalization;
using System.Windows.Media;
using Xunit;

namespace Fluxo.Tests.Views.Converters;

public sealed class BackgroundToForegroundConverterTests
{
    [Fact]
    public void BackgroundToForegroundConverter_Convert_ReturnsHarmonicDarkBrush_WhenBackgroundIsBright()
    {
        var converter = new BackgroundToForegroundConverter();
        var background = Color.FromRgb(230, 234, 240);

        var result = converter.Convert(
            background,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(ContrastRatio(background, brush.Color) >= converter.TargetContrast);
        Assert.True(RelativeLuminance(brush.Color) < RelativeLuminance(background));
        Assert.NotEqual(Colors.Black, brush.Color);
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_ReturnsHarmonicLightBrush_WhenBackgroundIsDark()
    {
        var converter = new BackgroundToForegroundConverter();
        var background = Color.FromRgb(18, 20, 23);

        var result = converter.Convert(
            new SolidColorBrush(background),
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(ContrastRatio(background, brush.Color) >= converter.TargetContrast);
        Assert.True(RelativeLuminance(brush.Color) > RelativeLuminance(background));
        Assert.NotEqual(Colors.White, brush.Color);
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_UsesStringBackground_WhenHexIsProvided()
    {
        var converter = new BackgroundToForegroundConverter();
        var background = Color.FromRgb(255, 176, 32);

        var result = converter.Convert(
            "#FFB020",
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(ContrastRatio(background, brush.Color) >= converter.TargetContrast);
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_UsesBlackFallback_WhenInputIsUnsupported()
    {
        var converter = new BackgroundToForegroundConverter();

        var result = converter.Convert(
            "not-a-color",
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(ContrastRatio(Colors.Black, brush.Color) >= converter.TargetContrast);
        Assert.True(RelativeLuminance(brush.Color) > RelativeLuminance(Colors.Black));
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_ReturnsWhiteBrush_WhenBackgroundIsTransparent()
    {
        var converter = new BackgroundToForegroundConverter();

        var result = converter.Convert(
            Colors.Transparent,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.White, brush.Color);
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_RespectsCustomTargetContrast()
    {
        var converter = new BackgroundToForegroundConverter { TargetContrast = 7.0 };
        var background = Color.FromRgb(18, 20, 23);

        var result = converter.Convert(
            background,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(ContrastRatio(background, brush.Color) >= 7.0);
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_DecreasesLuminanceSlightly_WhenForegroundIsDark()
    {
        var adjustedConverter = new BackgroundToForegroundConverter();
        var baselineConverter = new BackgroundToForegroundConverter { LuminanceAdjustment = 0 };
        var background = Color.FromRgb(230, 234, 240);

        var adjustedResult = adjustedConverter.Convert(
            background,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);
        var baselineResult = baselineConverter.Convert(
            background,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var adjustedBrush = Assert.IsType<SolidColorBrush>(adjustedResult);
        var baselineBrush = Assert.IsType<SolidColorBrush>(baselineResult);
        Assert.True(RelativeLuminance(adjustedBrush.Color) < RelativeLuminance(baselineBrush.Color));
        Assert.True(ContrastRatio(background, adjustedBrush.Color) >= adjustedConverter.TargetContrast);
    }

    [Fact]
    public void BackgroundToForegroundConverter_Convert_IncreasesLuminanceSlightly_WhenForegroundIsLight()
    {
        var adjustedConverter = new BackgroundToForegroundConverter();
        var baselineConverter = new BackgroundToForegroundConverter { LuminanceAdjustment = 0 };
        var background = Color.FromRgb(18, 20, 23);

        var adjustedResult = adjustedConverter.Convert(
            background,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);
        var baselineResult = baselineConverter.Convert(
            background,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var adjustedBrush = Assert.IsType<SolidColorBrush>(adjustedResult);
        var baselineBrush = Assert.IsType<SolidColorBrush>(baselineResult);
        Assert.True(RelativeLuminance(adjustedBrush.Color) > RelativeLuminance(baselineBrush.Color));
        Assert.True(ContrastRatio(background, adjustedBrush.Color) >= adjustedConverter.TargetContrast);
    }

    [Fact]
    public void BackgroundToForegroundConverter_ConvertMultiValue_UsesBackgroundValue()
    {
        var converter = new BackgroundToForegroundConverter();
        var background = Color.FromRgb(230, 234, 240);

        var result = converter.Convert(
            new object[] { background, Brushes.White },
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(ContrastRatio(background, brush.Color) >= converter.TargetContrast);
        Assert.True(RelativeLuminance(brush.Color) < RelativeLuminance(background));
    }

    [Fact]
    public void BackgroundToForegroundConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new BackgroundToForegroundConverter();

        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(
            Brushes.White,
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BackgroundToForegroundConverter_ConvertBackMultiValue_ThrowsNotSupportedException()
    {
        var converter = new BackgroundToForegroundConverter();

        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(
            Brushes.White,
            new[] { typeof(Brush), typeof(Brush) },
            null!,
            CultureInfo.InvariantCulture));
    }

    private static double ContrastRatio(Color first, Color second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);

        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        double r = Linearize(color.R / 255.0);
        double g = Linearize(color.G / 255.0);
        double b = Linearize(color.B / 255.0);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
