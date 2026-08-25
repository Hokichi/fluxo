using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Fluxo.Resources.Converters;

public sealed class WizardStepForegroundConverter : IValueConverter
{
    private readonly Func<string, Brush?> _findThemeBrush;

    public WizardStepForegroundConverter()
        : this(FindApplicationBrush)
    {
    }

    internal WizardStepForegroundConverter(Func<string, Brush?> findThemeBrush)
    {
        _findThemeBrush = findThemeBrush;
    }

    public Brush CurrentBrush { get; set; } = Brushes.Transparent;

    public string CurrentBrushKey { get; set; } = string.Empty;

    public Brush CompletedBrush { get; set; } = Brushes.Transparent;

    public string CompletedBrushKey { get; set; } = string.Empty;

    public Brush FutureBrush { get; set; } = Brushes.Transparent;

    public string FutureBrushKey { get; set; } = string.Empty;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int currentStep
            && int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelStep)
            ? labelStep == currentStep
                ? ResolveBrush(CurrentBrushKey, CurrentBrush)
                : labelStep < currentStep
                    ? ResolveBrush(CompletedBrushKey, CompletedBrush)
                    : ResolveBrush(FutureBrushKey, FutureBrush)
            : ResolveBrush(FutureBrushKey, FutureBrush);
    }

    private Brush ResolveBrush(string resourceKey, Brush fallback) =>
        string.IsNullOrWhiteSpace(resourceKey) ? fallback : _findThemeBrush(resourceKey) ?? fallback;

    private static Brush? FindApplicationBrush(string resourceKey) =>
        Application.Current?.TryFindResource(resourceKey) as Brush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
