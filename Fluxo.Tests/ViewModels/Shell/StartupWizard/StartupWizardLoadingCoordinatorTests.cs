using Fluxo.Helpers.Settings;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.StartupWizard;

public sealed class QuickSetupWizardLoadingCoordinatorTests
{
    [Fact]
    public async Task QuickSetupWizardLoadingCoordinator_RunAsync_SuccessOnFirstAttempt_ReturnsSuccess()
    {
        var attempts = 0;
        var prompted = 0;
        TimeSpan? observedDelay = null;

        var outcome = await QuickSetupWizardLoadingCoordinator.RunAsync(
            tryStageAsync: () =>
            {
                attempts++;
                return Task.FromResult(true);
            },
            confirmRetryCycleAsync: () =>
            {
                prompted++;
                return Task.FromResult(false);
            },
            delayAsync: duration =>
            {
                observedDelay = duration;
                return Task.CompletedTask;
            });

        Assert.Equal(1, attempts);
        Assert.Equal(0, prompted);
        Assert.Equal(TimeSpan.FromSeconds(5), observedDelay);
        Assert.Equal(QuickSetupWizardLoadingOutcome.Success, outcome);
    }

    [Fact]
    public async Task QuickSetupWizardLoadingCoordinator_RunAsync_SixthAttemptAfterUserYes_ReturnsSuccess()
    {
        var attempts = 0;
        var prompted = 0;

        var outcome = await QuickSetupWizardLoadingCoordinator.RunAsync(
            tryStageAsync: () =>
            {
                attempts++;
                return Task.FromResult(attempts == 6);
            },
            confirmRetryCycleAsync: () =>
            {
                prompted++;
                return Task.FromResult(true);
            },
            delayAsync: _ => Task.CompletedTask);

        Assert.Equal(6, attempts);
        Assert.Equal(1, prompted);
        Assert.Equal(QuickSetupWizardLoadingOutcome.Success, outcome);
    }

    [Fact]
    public async Task QuickSetupWizardLoadingCoordinator_RunAsync_FiveFailuresAndUserNo_ReturnsAbandoned()
    {
        var attempts = 0;
        var prompted = 0;

        var outcome = await QuickSetupWizardLoadingCoordinator.RunAsync(
            tryStageAsync: () =>
            {
                attempts++;
                return Task.FromResult(false);
            },
            confirmRetryCycleAsync: () =>
            {
                prompted++;
                return Task.FromResult(false);
            },
            delayAsync: _ => Task.CompletedTask);

        Assert.Equal(5, attempts);
        Assert.Equal(1, prompted);
        Assert.Equal(QuickSetupWizardLoadingOutcome.Abandoned, outcome);
    }
}
