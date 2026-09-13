using System.Globalization;
using System.Windows.Data;

namespace Fluxo.Resources.Converters;

public class DateTimeToRelativeDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dateTime) return string.Empty;

        var localDate = dateTime.Kind == DateTimeKind.Utc
            ? dateTime.ToLocalTime()
            : dateTime;

        var today = DateTime.Today;
        var valueDate = localDate.Date;

        if (valueDate == today) return "Today";

        if (valueDate == today.AddDays(-1)) return "Yesterday";

        return localDate.ToString("MMM dd", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
