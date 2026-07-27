using System.Windows;
using Fluxo.Helpers.MainWindow;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public class WindowBoundsInterpolatorTests
{
    [Fact]
    public void Interpolate_Halfway_AnimatesWidthAndHeightTogether()
    {
        var from = new Rect(0, 0, 200, 100);
        var to = new Rect(100, 50, 300, 200);

        var halfway = WindowBoundsInterpolator.Interpolate(from, to, 0.5);

        Assert.Equal(50, halfway.Left);
        Assert.Equal(25, halfway.Top);
        Assert.Equal(250, halfway.Width);
        Assert.Equal(150, halfway.Height);
    }

    [Fact]
    public void Interpolate_Halfway_AnimatesBottomEdgeSmoothly()
    {
        var from = new Rect(0, 0, 200, 100);
        var to = new Rect(100, 50, 300, 200);

        var halfway = WindowBoundsInterpolator.Interpolate(from, to, 0.5);
        var halfwayBottom = halfway.Top + halfway.Height;

        Assert.Equal(175, halfwayBottom);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void Interpolate_ClampsProgressToSupportedRange(double inputProgress, double expectedProgress)
    {
        var from = new Rect(0, 10, 200, 100);
        var to = new Rect(100, 50, 300, 200);

        var result = WindowBoundsInterpolator.Interpolate(from, to, inputProgress);
        var expected = WindowBoundsInterpolator.Interpolate(from, to, expectedProgress);

        Assert.Equal(expected.Left, result.Left);
        Assert.Equal(expected.Top, result.Top);
        Assert.Equal(expected.Width, result.Width);
        Assert.Equal(expected.Height, result.Height);
    }
}
