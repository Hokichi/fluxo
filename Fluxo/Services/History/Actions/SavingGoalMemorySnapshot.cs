using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed record SavingGoalMemorySnapshot(
    int SavingGoalId,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateTime? SavingEndDate,
    DateTime CreatedOn)
{
    public static SavingGoalMemorySnapshot Create(SavingGoal savingGoal)
    {
        ArgumentNullException.ThrowIfNull(savingGoal);

        return new SavingGoalMemorySnapshot(
            savingGoal.Id,
            savingGoal.Name,
            savingGoal.TargetAmount,
            savingGoal.CurrentAmount,
            savingGoal.SavingEndDate,
            savingGoal.CreatedOn);
    }
}
