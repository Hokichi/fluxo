using System.Globalization;
using System.Windows.Data;
using Fluxo.Resources.Helpers;

namespace Fluxo.Resources.Converters;

public class NumberWithCommasConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return MoneyFormatUtility.ToCompactText(value, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
