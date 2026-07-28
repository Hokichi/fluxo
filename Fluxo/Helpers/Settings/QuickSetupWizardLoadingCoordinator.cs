namespace Fluxo.Helpers.Settings;

public static class QuickSetupWizardLoadingCoordinator
{
    public const int AttemptsPerCycle = 5;

    public static readonly TimeSpan MinimumLoadingDuration = TimeSpan.FromSeconds(5);

    public static async Task<QuickSetupWizardLoadingOutcome> RunAsync(
        Func<Task<bool>> tryStageAsync,
        Func<Task<bool>> confirmRetryCycleAsync,
        Func<TimeSpan, Task> delayAsync)
    {
        ArgumentNullException.ThrowIfNull(tryStageAsync);
        ArgumentNullException.ThrowIfNull(confirmRetryCycleAsync);
        ArgumentNullException.ThrowIfNull(delayAsync);

        var minimumDelayTask = delayAsync(MinimumLoadingDuration);

        while (true)
        {
            for (var attempt = 0; attempt < AttemptsPerCycle; attempt++)
            {
                if (await tryStageAsync().ConfigureAwait(false))
                {
                    await minimumDelayTask.ConfigureAwait(false);
                    return QuickSetupWizardLoadingOutcome.Success;
                }
            }

            if (!await confirmRetryCycleAsync().ConfigureAwait(false))
            {
                await minimumDelayTask.ConfigureAwait(false);
                return QuickSetupWizardLoadingOutcome.Abandoned;
            }
        }
    }
}
