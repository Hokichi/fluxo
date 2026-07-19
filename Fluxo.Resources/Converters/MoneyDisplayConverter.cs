using System.Globalization;
using System.Windows.Data;
using Fluxo.Resources.Helpers;

namespace Fluxo.Resources.Converters;

public class MoneyDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        var text = value as string ?? System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var canonical = MoneyFormatUtility.BuildCanonicalNumber(text, culture);
        return canonical.Length == 0 ? string.Empty : MoneyFormatUtility.ToCompactTextFromCanonical(canonical, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
