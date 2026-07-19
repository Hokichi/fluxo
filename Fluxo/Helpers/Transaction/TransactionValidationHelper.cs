using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class TransactionValidationHelper
{
    private const int MaxNameLength = 256;

    public static Result ValidateName(string? value, bool isGoal)
    {
        if (isGoal)
            return Result.Success();

        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result.Failure("Please enter a name.");
        if (name.Length > MaxNameLength)
            return Result.Failure($"Name cannot exceed {MaxNameLength} characters.");
        return name.Any(char.IsControl)
            ? Result.Failure("Name cannot contain control characters.")
            : Result.Success();
    }

    public static Result ValidateAmount(
        decimal amount,
        bool isRepaymentAmountInvalid,
        bool isExpense,
        bool isGoal,
        AccountVM? source,
        bool ignoreMaximumSpending = false)
    {
        if (isRepaymentAmountInvalid)
            return Result.Failure("Invalid Repayment");
        if (amount <= 0m)
            return Result.Failure("Please enter a valid amount greater than zero.");
        return source is null
            ? Result.Success()
            : ValidateSpendingAmount(isExpense, isGoal, amount, source, ignoreMaximumSpending);
    }

    public static Result ValidateSpendingAmount(bool isExpense, bool isGoal, decimal amount, AccountVM source,
        bool ignoreMaximumSpending = false) =>
        ValidateSpendingAmount(isExpense, isGoal, amount, source.MaximumSpending, source.AccountType,
            source.Balance, source.AccountLimit, source.SpentAmount, source.MoneyOut, ignoreMaximumSpending);

    public static Result ValidateSpendingAmount(bool isExpense, bool isGoal, decimal amount, Account source,
        bool ignoreMaximumSpending = false) =>
        ValidateSpendingAmount(isExpense, isGoal, amount, source.MaximumSpending, source.AccountType,
            source.Balance, source.AccountLimit, source.SpentAmount, GetPersistedMoneyOut(source), ignoreMaximumSpending);

    public static Result ValidateAccount(AccountVM? account) => account is null
        ? Result.Failure("Please choose a account.")
        : Result.Success();

    public static Result ValidateTag(TagVM? tag, bool isExpense) => isExpense && tag is null
        ? Result.Failure("Please choose a tag.")
        : Result.Success();

    public static Result ValidateGoal(SavingGoalVM? goal, bool isGoal) => isGoal && goal is null
        ? Result.Failure("Please choose a goal.")
        : Result.Success();

    public static Result ValidateTagSpending(
        bool isExpense,
        bool isRecurring,
        bool isExcludedFromBudget,
        TagVM? tag,
        decimal currentSpending,
        decimal amount)
    {
        if (!isExpense || isRecurring || isExcludedFromBudget || tag is not { SpendingLimit: > 0m })
            return Result.Success();

        return currentSpending + amount <= tag.SpendingLimit.Value
            ? Result.Success()
            : Result.Failure($"{tag.Name} spending limit exceeded.");
    }

    public static Result ValidateCategoryBudget(
        OverspendPolicy policy,
        BudgetAllocationCategoryState categoryState,
        ExpenseCategory category,
        decimal amount) => policy == OverspendPolicy.HardStop && BudgetAllocationCalculator.WouldHardStop(categoryState, amount)
            ? Result.Failure($"{TransactionCalculationHelper.GetExpenseCategoryLabel(category)} budget is exhausted for this allocation period.")
            : Result.Success();

    private static Result ValidateSpendingAmount(
        bool isExpense,
        bool isGoal,
        decimal amount,
        decimal maximumSpending,
        AccountType accountType,
        decimal balance,
        decimal accountLimit,
        decimal spentAmount,
        decimal moneyOut,
        bool ignoreMaximumSpending)
    {
        if (amount <= 0m)
            return Result.Failure("Please enter a valid amount greater than zero.");
        if (!isExpense && !isGoal)
            return Result.Success();

        var projectedSpending = accountType == AccountType.Credit ? spentAmount + amount : moneyOut + amount;
        if (!ignoreMaximumSpending && maximumSpending > 0m && projectedSpending > maximumSpending)
            return Result.Failure("Amount exceeds this source's maximum spending limit.");

        if (accountType == AccountType.Credit)
            return spentAmount + amount <= accountLimit
                ? Result.Success()
                : Result.Failure("Amount exceeds this source's account limit.");

        return amount <= balance
            ? Result.Success()
            : Result.Failure("Amount exceeds this source's available balance.");
    }

    private static decimal GetPersistedMoneyOut(Account source)
    {
        var rawValue = source.GetType().GetProperty("MoneyOut")?.GetValue(source);
        return rawValue is decimal moneyOut ? moneyOut : source.SpentAmount;
    }

    public readonly record struct Result(bool IsValid, string? ErrorMessage)
    {
        public static Result Success() => new(true, null);
        public static Result Failure(string errorMessage) => new(false, errorMessage);
    }
}
