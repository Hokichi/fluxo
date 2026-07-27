using System.Windows;

namespace Fluxo.Helpers.MainWindow;

public static class WindowRestoreBoundsResolver
{
    public const double RestoredWidth = 1600;
    public const double RestoredHeight = 900;

    public static Rect ResolveCenteredRestoreBounds(Rect workArea)
    {
        var left = workArea.Left + (workArea.Width - RestoredWidth) / 2;
        var top = workArea.Top + (workArea.Height - RestoredHeight) / 2;

        return new Rect(left, top, RestoredWidth, RestoredHeight);
    }
}
