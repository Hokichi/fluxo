using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Shell.QuickSetupWizard;

public sealed record QuickSetupWizardRecurringTransactionItemVM(
    int Id,
    string Name,
    decimal Amount,
    string CategoryLabel,
    string AccountName,
    RecurringPeriod RecurringPeriod,
    int? DueTime)
{
    public string DueDateDisplay => DueTime.HasValue ? $"Due {RecurringPeriod} {DueTime.Value}" : "No due day";

    public QuickSetupWizardRecurringTransactionItemVM(QuickSetupWizardDraftRecurringTransaction expense, string accountName) : this(
        expense.Id,
        expense.Name,
        expense.Amount,
        expense.Category switch
        {
            ExpenseCategory.Needs => "Needs",
            ExpenseCategory.Wants => "Wants",
            _ => "Invest"
        },
        accountName,
        expense.RecurringPeriod,
        expense.RecurringTime)
    {
    }
}
