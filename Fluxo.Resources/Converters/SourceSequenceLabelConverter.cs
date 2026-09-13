using System.Globalization;
using System.Windows.Data;

namespace Fluxo.Resources.Converters;

public sealed class SourceSequenceLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var index = value switch
        {
            int intValue => intValue,
            _ => 0
        };

        return $"Source {index + 1}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
