using System.Windows;
using Fluxo.Helpers.MainWindow;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public class WindowRestoreBoundsResolverTests
{
    [Fact]
    public void WindowRestoreBoundsResolver_ResolveCenteredRestoreBounds_CentersOnSecondaryMonitorWorkArea()
    {
        var workArea = new Rect(1920, 0, 1920, 1040);

        var bounds = WindowRestoreBoundsResolver.ResolveCenteredRestoreBounds(workArea);

        Assert.Equal(2080, bounds.Left);
        Assert.Equal(70, bounds.Top);
        Assert.Equal(1600, bounds.Width);
        Assert.Equal(900, bounds.Height);
    }

    [Fact]
    public void WindowRestoreBoundsResolver_ResolveCenteredRestoreBounds_UsesWorkAreaOriginForExactFit()
    {
        var workArea = new Rect(2560, 1440, 1600, 900);

        var bounds = WindowRestoreBoundsResolver.ResolveCenteredRestoreBounds(workArea);

        Assert.Equal(2560, bounds.Left);
        Assert.Equal(1440, bounds.Top);
        Assert.Equal(1600, bounds.Width);
        Assert.Equal(900, bounds.Height);
    }
}
