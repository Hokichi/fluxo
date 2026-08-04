using Fluxo.Core.Enums;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class AppStartupVisibilityTests
{
    [Theory]
    [InlineData("--startup-tray", AppCloseBehavior.Exit, false, false)]
    [InlineData("--startup-tray", AppCloseBehavior.MinimizeToTray, false, true)]
    [InlineData("--startup--tray", AppCloseBehavior.MinimizeToTray, false, false)]
    [InlineData(null, AppCloseBehavior.MinimizeToTray, false, false)]
    [InlineData("--startup-tray", AppCloseBehavior.MinimizeToTray, true, false)]
    public void ShouldHideMainWindowAtStartup_UsesLaunchCloseBehaviorAndActivation(
        string? argument,
        AppCloseBehavior closeBehavior,
        bool isPrimaryActivationPending,
        bool expected)
    {
        var args = argument is null ? [] : new[] { argument };

        Assert.Equal(expected, App.ShouldHideMainWindowAtStartup(args, closeBehavior, isPrimaryActivationPending));
    }
}
