using System.Windows;

namespace Fluxo.Helpers.MainWindow;

public static class WindowBoundsInterpolator
{
    public static Rect Interpolate(Rect from, Rect to, double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 1);

        var left = Lerp(from.Left, to.Left, clampedProgress);
        var width = Lerp(from.Width, to.Width, clampedProgress);
        var height = Lerp(from.Height, to.Height, clampedProgress);

        // Interpolate using bottom edge + height so the bottom edge animates continuously.
        var fromBottom = from.Top + from.Height;
        var toBottom = to.Top + to.Height;
        var bottom = Lerp(fromBottom, toBottom, clampedProgress);
        var top = bottom - height;

        return new Rect(left, top, width, height);
    }

    private static double Lerp(double from, double to, double progress)
    {
        return from + (to - from) * progress;
    }
}
