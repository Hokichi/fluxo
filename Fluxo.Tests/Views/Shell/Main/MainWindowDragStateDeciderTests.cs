using Fluxo.Helpers.MainWindow;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public class MainWindowDragStateDeciderTests
{
    [Fact]
    public void DecideRestoreMode_MaximizedEligibleAndNotTransitioning_ReturnsInstantRestoreAndDrag()
    {
        var result = MainWindowDragStateDecider.DecideRestoreMode(
            isMaximized: true,
            isStateChangeTransitionActive: false,
            isEligibleHeaderDrag: true);

        Assert.Equal(MainWindowRestoreMode.InstantRestoreAndDrag, result);
    }

    [Fact]
    public void DecideRestoreMode_NotMaximizedEligibleAndNotTransitioning_ReturnsDragOnly()
    {
        var result = MainWindowDragStateDecider.DecideRestoreMode(
            isMaximized: false,
            isStateChangeTransitionActive: false,
            isEligibleHeaderDrag: true);

        Assert.Equal(MainWindowRestoreMode.DragOnly, result);
    }

    [Fact]
    public void DecideRestoreMode_TransitioningAndEligible_ReturnsNoop()
    {
        var result = MainWindowDragStateDecider.DecideRestoreMode(
            isMaximized: true,
            isStateChangeTransitionActive: true,
            isEligibleHeaderDrag: true);

        Assert.Equal(MainWindowRestoreMode.Noop, result);
    }

    [Fact]
    public void DecideRestoreMode_NotEligibleAndNotTransitioning_ReturnsNoop()
    {
        var result = MainWindowDragStateDecider.DecideRestoreMode(
            isMaximized: true,
            isStateChangeTransitionActive: false,
            isEligibleHeaderDrag: false);

        Assert.Equal(MainWindowRestoreMode.Noop, result);
    }
}
