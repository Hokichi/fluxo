
namespace Fluxo.DataModels.Shell.QuickSetupWizard;

public sealed record QuickSetupWizardDraftSavingGoal(
    int Id,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateTime? SavingEndDate);
