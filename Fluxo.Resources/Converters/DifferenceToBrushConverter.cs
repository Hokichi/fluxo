using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Fluxo.Resources.Theming;

namespace Fluxo.Resources.Converters;

public sealed class DifferenceToBrushConverter : IValueConverter
{
    private readonly Dictionary<string, SolidColorBrush> _brushes = new();

    public DifferenceToBrushConverter()
    {
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var difference = value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            int intValue => intValue,
            long longValue => longValue,
            _ => 0m
        };

        var brushKey = difference switch
        {
            < 0m => "Brush.Danger",
            > 0m => "Brush.Mint",
            _ => "Brush.Text.Primary"
        };

        return GetBrush(brushKey);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private SolidColorBrush GetBrush(string brushKey)
    {
        if (!_brushes.TryGetValue(brushKey, out var brush))
        {
            brush = new SolidColorBrush(Colors.White);
            _brushes.Add(brushKey, brush);
        }

        RefreshBrush(brushKey, brush);
        return brush;
    }

    private void OnThemeChanged(object? sender, EventArgs args)
    {
        foreach (var (brushKey, brush) in _brushes)
            RefreshBrush(brushKey, brush);
    }

    private static void RefreshBrush(string brushKey, SolidColorBrush brush)
    {
        if (Application.Current?.TryFindResource(brushKey) is SolidColorBrush themeBrush)
            brush.Color = themeBrush.Color;
    }
}
