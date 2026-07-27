using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Shell.QuickSetupWizard;

public sealed record QuickSetupWizardDraftAccount(
    int Id,
    string Name,
    AccountType AccountType,
    decimal Balance,
    decimal SpentAmount,
    decimal AccountLimit,
    decimal MaximumSpending,
    decimal? MinimumPayment,
    int? MonthlyDueDate,
    int? DeductSource,
    decimal? InterestRate,
    bool PinnedOnUi,
    bool IsEnabled,
    bool IsDefault);
