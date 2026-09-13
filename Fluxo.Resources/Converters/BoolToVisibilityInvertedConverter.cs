using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fluxo.Resources.Converters;

public class BoolToVisibilityInvertedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
