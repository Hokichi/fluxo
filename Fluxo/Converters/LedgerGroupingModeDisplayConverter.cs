using System.Globalization;
using System.Windows.Data;
using Fluxo.Core.Enums;

namespace Fluxo.Converters;

public sealed class LedgerGroupingModeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            LedgerGroupingMode.Accounts => "Accounts",
            LedgerGroupingMode groupingMode => groupingMode.ToString(),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
