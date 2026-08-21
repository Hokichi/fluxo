using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class DeleteSavingGoalMemoryAction(SavingGoalMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Delete saving goal";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => "Saving goal deleted";
    public string Details => $"{LogMemoryDisplay.Amount(snapshot.CurrentAmount)} of {LogMemoryDisplay.Amount(snapshot.TargetAmount)}";

    public async Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        if (await appData.GetSavingGoalByIdAsync(snapshot.SavingGoalId, cancellationToken) is not null)
            return;

        var savingGoal = new SavingGoal
        {
            Id = snapshot.SavingGoalId,
            Name = snapshot.Name,
            TargetAmount = snapshot.TargetAmount,
            CurrentAmount = snapshot.CurrentAmount,
            SavingEndDate = snapshot.SavingEndDate,
            CreatedOn = snapshot.CreatedOn
        };

        await appData.AddSavingGoalAsync(savingGoal, cancellationToken);
    }

    public async Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        var savingGoal = await appData.GetSavingGoalByIdAsync(snapshot.SavingGoalId, cancellationToken);
        if (savingGoal is null)
            return;

        appData.RemoveSavingGoal(savingGoal);
    }
}
