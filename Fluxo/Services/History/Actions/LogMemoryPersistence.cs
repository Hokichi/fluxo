using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

internal static class LogMemoryPersistence
{
    internal static async Task AdjustGoalAsync(
        IAppDataService appData,
        int? goalId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (goalId is not { } id)
            return;

        var goal = await appData.GetSavingGoalByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Saving goal {id} was not found.");
        goal.CurrentAmount += amount;
        appData.UpdateSavingGoal(goal);
    }

    internal static void ApplyTransactionToAccount(Account account, TransactionType type, decimal amount)
    {
        if (type == TransactionType.Expense)
            ApplyExpenseToAccount(account, amount);
        else
            ApplyIncomeToAccount(account, amount);
    }

    internal static void RevertTransactionFromAccount(Account account, TransactionType type, decimal amount)
    {
        if (type == TransactionType.Expense)
            RevertExpenseFromAccount(account, amount);
        else
            RevertIncomeFromAccount(account, amount);
    }

    internal static void ApplyExpenseToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount += amount;
            return;
        }

        account.Balance -= amount;
    }

    internal static void RevertExpenseFromAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
            return;
        }

        account.Balance += amount;
    }

    internal static void ApplyIncomeToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
            return;
        }

        account.Balance += amount;
    }

    internal static void RevertIncomeFromAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount += amount;
            return;
        }

        account.Balance -= amount;
    }

    internal static async Task<Account> GetRequiredAccountAsync(IAppDataService appData,
        int accountId, CancellationToken cancellationToken)
    {
        var account = await appData.GetAccountByIdAsync(accountId, cancellationToken);
        return account ??
               throw new InvalidOperationException($"Unable to find account {accountId}.");
    }

    internal static async Task<Tag> GetRequiredTagAsync(IAppDataService appData, int tagId,
        CancellationToken cancellationToken)
    {
        var tag = await appData.GetTagByIdAsync(tagId, cancellationToken);
        return tag ?? throw new InvalidOperationException($"Unable to find expense tag {tagId}.");
    }
}
