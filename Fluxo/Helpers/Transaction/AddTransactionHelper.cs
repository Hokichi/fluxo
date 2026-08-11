using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class AddTransactionHelper
{
    public static State CreateState(
        Input input,
        IReadOnlyList<AccountVM> accounts,
        IReadOnlyList<TagVM> tags,
        IReadOnlyList<SavingGoalVM> goals) => new(
            input,
            Resolve(accounts, input.AccountId),
            Resolve(tags, input.TagId),
            Resolve(goals, input.GoalId));

    private static T? Resolve<T>(IReadOnlyList<T> values, int? id) where T : class =>
        id is null ? values.FirstOrDefault() : values.FirstOrDefault(value => GetId(value) == id) ?? values.FirstOrDefault();

    private static int GetId<T>(T value) => value switch
    {
        AccountVM account => account.Id,
        TagVM tag => tag.Id,
        SavingGoalVM goal => goal.Id,
        _ => 0
    };

    public readonly record struct Input(
        bool IsExpense,
        bool IsGoal,
        string Name,
        decimal Amount,
        int? AccountId,
        DateTime Date,
        string Note,
        ExpenseCategory? Category,
        int? TagId,
        int? GoalId,
        bool IsIoU,
        bool ShouldAffectBalance,
        bool LockTransactionType);

    public sealed record State(
        Input Input,
        AccountVM? SelectedAccount,
        TagVM? SelectedTag,
        SavingGoalVM? SelectedGoal)
    {
        public string Name => Input.Name;
        public decimal Amount => Input.Amount;
    }
}
