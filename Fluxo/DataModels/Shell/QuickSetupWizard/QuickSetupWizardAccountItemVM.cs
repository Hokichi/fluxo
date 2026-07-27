using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Shell.QuickSetupWizard;

public sealed record QuickSetupWizardAccountItemVM(
    int Id,
    string Name,
    string TypeLabel,
    decimal PrimaryAmount,
    string PrimaryAmountLabel,
    decimal MaximumSpending,
    decimal? MinimumPayment)
{
    public QuickSetupWizardAccountItemVM(Account account) : this(
        account.Id,
        account.Name,
        account.AccountType switch
        {
            AccountType.Credit => "Credit",
            AccountType.Checking => "Checking",
            AccountType.Cash => "Cash",
            AccountType.Saving => "Savings",
            _ => "Account"
        },
        account.AccountType == AccountType.Credit
            ? account.SpentAmount
            : account.Balance,
        account.AccountType == AccountType.Credit
            ? "Spent"
            : "Balance",
        account.MaximumSpending,
        account.MinimumPayment)
    {
    }

    public QuickSetupWizardAccountItemVM(QuickSetupWizardDraftAccount account) : this(
        account.Id,
        account.Name,
        account.AccountType switch
        {
            AccountType.Credit => "Credit",
            AccountType.Checking => "Checking",
            AccountType.Cash => "Cash",
            AccountType.Saving => "Savings",
            _ => "Account"
        },
        account.AccountType == AccountType.Credit
            ? account.SpentAmount
            : account.Balance,
        account.AccountType == AccountType.Credit
            ? "Spent"
            : "Balance",
        account.MaximumSpending,
        account.MinimumPayment)
    {
    }
}
