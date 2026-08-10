using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class EditTransactionHelper
{
    public static Input CreateInput(TransactionVM transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.SourceAccountId <= 0)
            throw new InvalidOperationException("The transaction edit is incomplete.");

        return new Input(
            transaction.Name,
            transaction.Amount,
            transaction.IsPinned,
            transaction.Notes,
            transaction.OccurredOn,
            transaction.ExpenseCategory,
            transaction.SourceAccountId,
            transaction.Tag?.Id,
            transaction.IsIoU,
            transaction.ShouldAffectBalance,
            transaction.IsExcludedFromBudget);
    }

    public static Draft CreateDraft(TransactionVM transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return new Draft(
            transaction.Type == TransactionType.Expense,
            transaction.Name,
            transaction.Amount,
            transaction.SourceAccountId,
            transaction.OccurredOn,
            transaction.Notes,
            transaction.ExpenseCategory,
            transaction.Tag?.Id,
            transaction.GoalId is not null,
            transaction.GoalId,
            transaction.IsIoU,
            transaction.IsExcludedFromBudget,
            transaction.ShouldAffectBalance);
    }

    public readonly record struct Input(
        string Name,
        decimal Amount,
        bool IsPinned,
        string Note,
        DateTime Date,
        ExpenseCategory? Category,
        int AccountId,
        int? TagId,
        bool IsIoU,
        bool ShouldAffectBalance,
        bool IsExcludedFromBudget);

    public readonly record struct Draft(
        bool IsExpense,
        string Name,
        decimal Amount,
        int AccountId,
        DateTime Date,
        string Note,
        ExpenseCategory? Category,
        int? TagId,
        bool IsGoal,
        int? GoalId,
        bool IsIoU,
        bool IsExcludedFromBudget,
        bool ShouldAffectBalance);
}
