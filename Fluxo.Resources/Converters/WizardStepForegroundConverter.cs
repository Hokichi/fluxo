using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Fluxo.Resources.Converters;

public sealed class WizardStepForegroundConverter : IValueConverter
{
    public Brush CurrentBrush { get; set; } = Brushes.Transparent;

    public Brush CompletedBrush { get; set; } = Brushes.Transparent;

    public Brush FutureBrush { get; set; } = Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int currentStep
            && int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelStep)
            ? labelStep == currentStep ? CurrentBrush : labelStep < currentStep ? CompletedBrush : FutureBrush
            : FutureBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
