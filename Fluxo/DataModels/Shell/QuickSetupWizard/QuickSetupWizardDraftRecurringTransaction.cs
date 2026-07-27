using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Shell.QuickSetupWizard;

public sealed record QuickSetupWizardDraftRecurringTransaction(
    int Id,
    RecurringTransactionType Type,
    string Name,
    decimal Amount,
    ExpenseCategory? Category,
    int AccountId,
    RecurringPeriod RecurringPeriod,
    int RecurringTime,
    int TagId,
    string TagName,
    bool IsActive,
    DateTime? EndDate = null);
