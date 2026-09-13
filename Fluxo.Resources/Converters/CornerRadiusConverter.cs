using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fluxo.Resources.Converters;

public class CornerRadiusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not double width || values[1] is not double height)
            return 0;

        return new CornerRadius(Math.Min(width, height) / 2);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return Array.ConvertAll(targetTypes, _ => Binding.DoNothing);
    }
}
