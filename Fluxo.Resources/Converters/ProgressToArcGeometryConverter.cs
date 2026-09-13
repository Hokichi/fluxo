using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Fluxo.Resources.Converters;

public class ProgressToArcGeometryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var progress = value switch
        {
            decimal decimalValue => (double)decimalValue,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue / 100d,
            _ => 0d
        };

        progress = Math.Clamp(progress, 0d, 1d);
        if (progress <= 0d)
            return Geometry.Empty;

        var size = 48d;
        if (parameter is string rawSize &&
            double.TryParse(rawSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize) &&
            parsedSize > 0d)
            size = parsedSize;

        if (progress >= 0.9999d)
        {
            var radius = size / 2d;
            return new EllipseGeometry(new Point(radius, radius), radius, radius);
        }

        var startAngle = -90d;
        var endAngle = startAngle + 360d * progress;
        var radiusValue = size / 2d;
        var center = new Point(radiusValue, radiusValue);

        var figure = new PathFigure
        {
            StartPoint = GetPoint(center, radiusValue, startAngle),
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = GetPoint(center, radiusValue, endAngle),
            Size = new Size(radiusValue, radiusValue),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = progress >= 0.5d
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static Point GetPoint(Point center, double radius, double angleInDegrees)
    {
        var angleInRadians = angleInDegrees * Math.PI / 180d;

        return new Point(
            center.X + radius * Math.Cos(angleInRadians),
            center.Y + radius * Math.Sin(angleInRadians));
    }
}
