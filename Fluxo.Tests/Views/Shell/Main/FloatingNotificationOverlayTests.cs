using Fluxo.Views.Shell.Main;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class FloatingNotificationOverlayTests
{
    [Theory]
    [InlineData(42u, 42, true)]
    [InlineData(7u, 42, false)]
    [InlineData(0u, 42, false)]
    public void IsForegroundProcess_MatchesCurrentProcess(
        uint foregroundProcessId, int currentProcessId, bool expected)
    {
        Assert.Equal(expected,
            FloatingNotificationOverlayWindow.IsForegroundProcess(foregroundProcessId, currentProcessId));
    }

}
