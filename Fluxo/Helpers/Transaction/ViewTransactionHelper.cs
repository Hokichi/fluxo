using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class ViewTransactionHelper
{
    public static State CreateState(
        TransactionVM transaction,
        IReadOnlyList<AccountVM> accounts,
        IReadOnlyList<SavingGoalVM> goals,
        IReadOnlyList<AccountVM> repaymentAccounts)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var loaded = TransactionMappingHelper.CreateLoaded(transaction);
        return new State(
            loaded,
            TransactionMappingHelper.CreatePending(loaded),
            loaded.Type == TransactionType.Expense,
            loaded.GoalId is not null,
            loaded.RepaymentAccountId is not null,
            accounts.FirstOrDefault(account => account.Id == loaded.SourceAccountId) ?? loaded.Account,
            loaded.Tag,
            goals.FirstOrDefault(goal => goal.Id == loaded.GoalId),
            repaymentAccounts.FirstOrDefault(account => account.Id == loaded.RepaymentAccountId));
    }

    public sealed record State(
        TransactionVM LoadedTransaction,
        TransactionVM PendingTransaction,
        bool IsExpense,
        bool IsGoal,
        bool IsRepayment,
        AccountVM? SelectedAccount,
        TagVM? SelectedTag,
        SavingGoalVM? SelectedGoal,
        AccountVM? SelectedRepaymentAccount);
}
