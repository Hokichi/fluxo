using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Fluxo.Resources.Converters;

public sealed class GoalProgressToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ratio = Math.Clamp(GetRatio(value), 0d, 1d);
        var start = GetColor("Color.Danger", Colors.IndianRed);
        var end = GetColor("Color.Mint", Colors.MediumSeaGreen);

        var color = Color.FromRgb(
            (byte)Math.Round(start.R + (end.R - start.R) * ratio),
            (byte)Math.Round(start.G + (end.G - start.G) * ratio),
            (byte)Math.Round(start.B + (end.B - start.B) * ratio));

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static double GetRatio(object value)
    {
        return value switch
        {
            decimal decimalValue => decimalValue > 1m ? (double)(decimalValue / 100m) : (double)decimalValue,
            double doubleValue => doubleValue > 1d ? doubleValue / 100d : doubleValue,
            float floatValue => floatValue > 1f ? floatValue / 100f : floatValue,
            int intValue => intValue / 100d,
            _ => 0d
        };
    }

    private static Color GetColor(string resourceKey, Color fallback)
    {
        return Application.Current.TryFindResource(resourceKey) switch
        {
            Color color => color,
            SolidColorBrush brush => brush.Color,
            _ => fallback
        };
    }
}
