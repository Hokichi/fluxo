using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.MainWindow;

internal static class TransactionDetailTargetResolver
{
    public static async Task<TransactionVM?> ResolveAsync(
        int transactionId,
        IAppDataService appData,
        CancellationToken cancellationToken = default)
    {
        var transaction = await appData.GetTransactionByIdAsync(transactionId, cancellationToken);
        if (transaction is null)
            return null;

        if (transaction.ParentTransactionId is { } parentId)
            transaction = await appData.GetTransactionByIdAsync(parentId, cancellationToken) ?? transaction;

        return ToViewModel(transaction);
    }

    public static async Task<TransactionVM> ResolveAsync(
        TransactionVM expenseLog,
        IAppDataService appData,
        CancellationToken cancellationToken = default)
    {
        if (expenseLog.ParentTransactionId is not { } parentLogId)
            return expenseLog;

        var parentLog = await appData.GetTransactionByIdAsync(parentLogId, cancellationToken);
        return parentLog is null ? expenseLog : ToViewModel(parentLog);
    }

    private static TransactionVM ToViewModel(Fluxo.Core.Entities.Transaction transaction)
    {
        return new TransactionVM
        {
            Id = transaction.Id,
            Type = transaction.Type,
            SourceAccountId = transaction.SourceAccountId,
            GoalId = transaction.GoalId,
            RepaymentAccountId = transaction.RepaymentAccountId,
            Name = transaction.Name,
            Amount = transaction.Amount,
            OccurredOn = transaction.OccurredOn,
            LoggedOn = transaction.LoggedOn,
            Notes = transaction.Notes,
            IsForDeletion = transaction.IsForDeletion,
            IsPinned = transaction.IsPinned,
            IsIoU = transaction.IsIoU,
            ShouldAffectBalance = transaction.ShouldAffectBalance,
            IsExcludedFromBudget = transaction.IsExcludedFromBudget,
            ExpenseCategory = transaction.ExpenseCategory,
            ParentTransactionId = transaction.ParentTransactionId,
            Account = new AccountVM
            {
                Id = transaction.Account?.Id ?? transaction.SourceAccountId,
                Name = transaction.Account?.Name ?? string.Empty,
                AccountType = transaction.Account?.AccountType ?? default,
                AccountLimit = transaction.Account?.AccountLimit ?? 0m,
                MaximumSpending = transaction.Account?.MaximumSpending ?? 0m,
                MinimumPayment = transaction.Account?.MinimumPayment,
                SpentAmount = transaction.Account?.SpentAmount ?? 0m,
                Balance = transaction.Account?.Balance ?? 0m,
                MonthlyDueDate = transaction.Account?.MonthlyDueDate,
                DeductSource = transaction.Account?.DeductSource,
                InterestRate = transaction.Account?.InterestRate,
                PinnedOnUI = transaction.Account?.PinnedOnUI ?? false,
                IsEnabled = transaction.Account?.IsEnabled ?? false
            },
            Tag = transaction.Tag is null ? null : new TagVM
            {
                Id = transaction.Tag.Id,
                Name = transaction.Tag.Name,
                HexCode = transaction.Tag.HexCode,
                IsSystemTag = transaction.Tag.IsSystemTag,
                SpendingLimit = transaction.Tag.SpendingLimit
            }
        };
    }
}
