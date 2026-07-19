using System.Globalization;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class EditRecurringTransactionHelper
{
    public static State CreateState(
        Input input,
        IReadOnlyList<AccountVM> accounts,
        IReadOnlyList<TagVM> tags,
        IReadOnlyList<SavingGoalVM> goals) => new(
            input.EditingRecurringTransactionId,
            input.Type == RecurringTransactionType.Expense,
            input.Type == RecurringTransactionType.GoalUpdate,
            input.Name,
            input.Amount,
            input.Category ?? ExpenseCategory.Needs,
            input.RecurringPeriod,
            input.RecurringPeriod == RecurringPeriod.None ? string.Empty : input.RecurringTime.ToString(CultureInfo.InvariantCulture),
            accounts.FirstOrDefault(account => account.Id == input.AccountId) ?? accounts.FirstOrDefault(),
            input.TagId is > 0 ? tags.FirstOrDefault(tag => tag.Id == input.TagId) : tags.FirstOrDefault(),
            input.GoalId is > 0 ? goals.FirstOrDefault(goal => goal.Id == input.GoalId) : goals.FirstOrDefault(),
            input.IsExcludedFromBudget);

    public readonly record struct Input(
        int? EditingRecurringTransactionId,
        RecurringTransactionType Type,
        string Name,
        decimal Amount,
        RecurringPeriod RecurringPeriod,
        int RecurringTime,
        int AccountId,
        ExpenseCategory? Category,
        int? TagId,
        int? GoalId,
        bool IsExcludedFromBudget);

    public sealed record State(
        int? EditingRecurringTransactionId,
        bool IsExpense,
        bool IsGoal,
        string Name,
        decimal Amount,
        ExpenseCategory Category,
        RecurringPeriod RecurringPeriod,
        string RecurringTimeText,
        AccountVM? SelectedAccount,
        TagVM? SelectedTag,
        SavingGoalVM? SelectedGoal,
        bool IsExcludedFromBudget);
}
