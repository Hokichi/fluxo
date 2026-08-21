using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class AddAccountMemoryAction(AccountMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Add account";
    public string Title => $"{snapshot.Name} Added";
    public string Summary => "Account added";
    public string Details => $"{snapshot.AccountType} · Balance {LogMemoryDisplay.Amount(snapshot.Balance)}";

    public async Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        var account =
            await appData.GetAccountByIdAsync(snapshot.AccountId, cancellationToken);
        if (account is null)
            return;

        appData.RemoveAccount(account);
    }

    public async Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        if (await appData.GetAccountByIdAsync(snapshot.AccountId, cancellationToken) is not null)
            return;

        var account = new Account
        {
            Id = snapshot.AccountId,
            Name = snapshot.Name,
            AccountType = snapshot.AccountType,
            AccountLimit = snapshot.AccountLimit,
            MaximumSpending = snapshot.MaximumSpending,
            MinimumPayment = snapshot.MinimumPayment,
            SpentAmount = snapshot.SpentAmount,
            Balance = snapshot.Balance,
            MonthlyDueDate = snapshot.MonthlyDueDate,
            DeductSource = snapshot.DeductSource,
            InterestRate = snapshot.InterestRate,
            PinnedOnUI = snapshot.PinnedOnUI,
            IsEnabled = snapshot.IsEnabled,
            IsDefault = snapshot.IsDefault
        };

        await appData.AddAccountAsync(account, cancellationToken);
    }
}
