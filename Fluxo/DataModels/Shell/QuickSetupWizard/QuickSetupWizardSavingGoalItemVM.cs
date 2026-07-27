using Fluxo.Core.Entities;

namespace Fluxo.DataModels.Shell.QuickSetupWizard;

public sealed record QuickSetupWizardSavingGoalItemVM(
    int Id,
    string Name,
    decimal CurrentAmount,
    decimal TargetAmount,
    DateTime? SavingEndDate)
{
    public QuickSetupWizardSavingGoalItemVM(SavingGoal goal) : this(
        goal.Id,
        goal.Name,
        goal.CurrentAmount,
        goal.TargetAmount,
        goal.SavingEndDate)
    {
    }

    public QuickSetupWizardSavingGoalItemVM(QuickSetupWizardDraftSavingGoal goal) : this(
        goal.Id,
        goal.Name,
        goal.CurrentAmount,
        goal.TargetAmount,
        goal.SavingEndDate)
    {
    }
}
