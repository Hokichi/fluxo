using System.Globalization;
using System.Windows.Data;

namespace Fluxo.Converters;

public sealed class TransactionDateGroupConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime occurredOn ? occurredOn.Date : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
