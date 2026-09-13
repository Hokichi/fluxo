using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Fluxo.Resources.Converters;

/// <summary>
/// Derives a foreground brush from the background by keeping the same hue,
/// reducing saturation, then binary-searching for the least-extreme lightness
/// that clears the target WCAG contrast ratio.
/// </summary>
public class BackgroundToForegroundConverter : IValueConverter
{
    /// <summary>WCAG 2.1 relative luminance (0 = black, 1 = white).</summary>
    private static double RelativeLuminance(Color color)
    {
        double r = Linearize(color.R / 255.0);
        double g = Linearize(color.G / 255.0);
        double b = Linearize(color.B / 255.0);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>WCAG 2.1 contrast ratio between two colors (range: 1-21).</summary>
    private static double ContrastRatio(Color first, Color second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);

        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    /// <summary>RGB to HSL. H in [0,360), S in [0,1], L in [0,1].</summary>
    private static (double H, double S, double L) RgbToHsl(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double lightness = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-10)
            return (0, 0, lightness);

        double delta = max - min;
        double saturation = lightness > 0.5
            ? delta / (2.0 - max - min)
            : delta / (max + min);

        double hue;
        if (max == r)
            hue = ((g - b) / delta + (g < b ? 6 : 0)) / 6.0;
        else if (max == g)
            hue = ((b - r) / delta + 2) / 6.0;
        else
            hue = ((r - g) / delta + 4) / 6.0;

        return (hue * 360.0, saturation, lightness);
    }

    /// <summary>HSL to RGB. H in [0,360), S in [0,1], L in [0,1].</summary>
    private static Color HslToRgb(double hue, double saturation, double lightness)
    {
        if (saturation < 1e-10)
        {
            var value = ToByte(lightness);
            return Color.FromRgb(value, value, value);
        }

        double q = lightness < 0.5
            ? lightness * (1 + saturation)
            : lightness + saturation - lightness * saturation;
        double p = 2 * lightness - q;
        double normalizedHue = hue / 360.0;

        return Color.FromRgb(
            ToByte(HueChannel(p, q, normalizedHue + 1.0 / 3)),
            ToByte(HueChannel(p, q, normalizedHue)),
            ToByte(HueChannel(p, q, normalizedHue - 1.0 / 3)));
    }

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double HueChannel(double p, double q, double t)
    {
        if (t < 0)
            t += 1;
        if (t > 1)
            t -= 1;

        return t switch
        {
            < 1.0 / 6 => p + (q - p) * 6 * t,
            < 1.0 / 2 => q,
            < 2.0 / 3 => p + (q - p) * (2.0 / 3 - t) * 6,
            _ => p
        };
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(value * 255, 0, 255);
    /// <summary>4.5 = WCAG AA. Raise to 7.0 for AAA.</summary>
    public double TargetContrast { get; set; } = 4.5;

    /// <summary>
    /// How much background saturation to retain for the foreground.
    /// 0 = fully desaturated grey tones, 1 = same saturation.
    /// </summary>
    public double SaturationRetention { get; set; } = 0.55;

    /// <summary>
    /// Extra HSL lightness nudge after contrast search.
    /// Dark foregrounds move darker, light foregrounds move lighter.
    /// </summary>
    public double LuminanceAdjustment { get; set; } = 0.1;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return new SolidColorBrush(GetHarmonicForeground(GetBackgroundColor(value)));
    }

    private Color GetHarmonicForeground(Color background)
    {
        if (background.A == 0)
            return Colors.White;

        double backgroundLuminance = RelativeLuminance(background);
        bool backgroundIsDark = backgroundLuminance <= 0.179;

        var (hue, saturation, _) = RgbToHsl(background);
        double foregroundSaturation = Math.Min(saturation * SaturationRetention, 0.55);

        Color Candidate(double lightness) => HslToRgb(hue, foregroundSaturation, lightness);
        bool Passes(double lightness) => ContrastRatio(background, Candidate(lightness)) >= TargetContrast;

        return backgroundIsDark
            ? FindLightForeground(Candidate, Passes, LuminanceAdjustment)
            : FindDarkForeground(Candidate, Passes, LuminanceAdjustment);
    }

    private static Color GetBackgroundColor(object? value)
    {
        return value switch
        {
            SolidColorBrush brush => brush.Color,
            Color color => color,
            string hex => TryParseColor(hex, out var parsedColor) ? parsedColor : Colors.Black,
            _ => Colors.Black
        };
    }

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            if (converted is Color parsedColor)
            {
                color = parsedColor;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        color = Colors.Black;
        return false;
    }

    private static Color FindLightForeground(Func<double, Color> candidate, Func<double, bool> passes, double adjustment)
    {
        double low = 0;
        double high = 1;

        for (var i = 0; i < 20; i++)
        {
            double middle = (low + high) / 2;
            if (passes(middle))
                high = middle;
            else
                low = middle;
        }

        return candidate(Math.Clamp(high + adjustment, 0, 1));
    }

    private static Color FindDarkForeground(Func<double, Color> candidate, Func<double, bool> passes, double adjustment)
    {
        double low = 0;
        double high = 1;

        for (var i = 0; i < 20; i++)
        {
            double middle = (low + high) / 2;
            if (passes(middle))
                low = middle;
            else
                high = middle;
        }

        return candidate(Math.Clamp(low - adjustment, 0, 1));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

}
