using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed record AccountMemorySnapshot(
    int AccountId,
    string Name,
    AccountType AccountType,
    decimal AccountLimit,
    decimal MaximumSpending,
    decimal? MinimumPayment,
    decimal SpentAmount,
    decimal Balance,
    int? MonthlyDueDate,
    int? DeductSource,
    decimal? InterestRate,
    bool PinnedOnUI,
    bool IsEnabled,
    bool IsDefault = false)
{
    public static AccountMemorySnapshot Create(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountMemorySnapshot(
            account.Id,
            account.Name,
            account.AccountType,
            account.AccountLimit,
            account.MaximumSpending,
            account.MinimumPayment,
            account.SpentAmount,
            account.Balance,
            account.MonthlyDueDate,
            account.DeductSource,
            account.InterestRate,
            account.PinnedOnUI,
            account.IsEnabled,
            account.IsDefault);
    }
}
