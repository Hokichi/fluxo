using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class EditAccountMemoryAction(
    AccountMemorySnapshot before,
    AccountMemorySnapshot after) : ILogMemoryAction
{
    public string Description => "Edit account";
    public string Title => $"{after.Name} Updated";
    public string Summary => "Account information updated";
    public string Details => LogMemoryDisplay.Changes(
        ("Name", before.Name, after.Name),
        ("Type", before.AccountType.ToString(), after.AccountType.ToString()),
        ("Balance", LogMemoryDisplay.Amount(before.Balance), LogMemoryDisplay.Amount(after.Balance)),
        ("Account limit", LogMemoryDisplay.Amount(before.AccountLimit), LogMemoryDisplay.Amount(after.AccountLimit)),
        ("Maximum spending", LogMemoryDisplay.Amount(before.MaximumSpending), LogMemoryDisplay.Amount(after.MaximumSpending)),
        ("Minimum payment", LogMemoryDisplay.OptionalAmount(before.MinimumPayment), LogMemoryDisplay.OptionalAmount(after.MinimumPayment)),
        ("Pinned", LogMemoryDisplay.YesNo(before.PinnedOnUI), LogMemoryDisplay.YesNo(after.PinnedOnUI)),
        ("Enabled", LogMemoryDisplay.YesNo(before.IsEnabled), LogMemoryDisplay.YesNo(after.IsEnabled)),
        ("Default", LogMemoryDisplay.YesNo(before.IsDefault), LogMemoryDisplay.YesNo(after.IsDefault)));

    public Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(appData, before, cancellationToken);
    }

    public Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(appData, after, cancellationToken);
    }

    private static async Task ApplySnapshotAsync(IAppDataService appData, AccountMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var account =
            await LogMemoryPersistence.GetRequiredAccountAsync(appData, snapshot.AccountId,
                cancellationToken);

        account.Name = snapshot.Name;
        account.AccountType = snapshot.AccountType;
        account.AccountLimit = snapshot.AccountLimit;
        account.MaximumSpending = snapshot.MaximumSpending;
        account.MinimumPayment = snapshot.MinimumPayment;
        account.SpentAmount = snapshot.SpentAmount;
        account.Balance = snapshot.Balance;
        account.MonthlyDueDate = snapshot.MonthlyDueDate;
        account.DeductSource = snapshot.DeductSource;
        account.InterestRate = snapshot.InterestRate;
        account.PinnedOnUI = snapshot.PinnedOnUI;
        account.IsEnabled = snapshot.IsEnabled;
        account.IsDefault = snapshot.IsDefault;

        appData.UpdateAccount(account);
    }
}
