using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class TransactionCalculationHelper
{
    public static decimal CalculateInstallmentAmount(decimal totalAmount, int count) =>
        decimal.Round(totalAmount / count, 2, MidpointRounding.AwayFromZero);

    public static decimal GetAccountCurrent(AccountVM? account) => account is null
        ? 0m
        : account.IsCredit ? account.SpentAmount : account.Balance;

    public static decimal CalculateAccountToBe(AccountVM? account, bool isIncome, decimal amount)
    {
        var current = GetAccountCurrent(account);
        if (account is null)
            return 0m;
        return account.IsCredit
            ? current + (isIncome ? -amount : amount)
            : current + (isIncome ? amount : -amount);
    }

    public static decimal CalculateCategoryToBe(decimal current, decimal amount, bool isRepayment) =>
        isRepayment ? Math.Max(0m, current - amount) : current + amount;

    public static IReadOnlyDictionary<ExpenseCategory, decimal> CalculateSpentByCategory(
        IEnumerable<Fluxo.Core.Entities.Transaction> expenseLogs,
        BudgetAllocationPeriod period) => expenseLogs
            .Where(log => !log.IsForDeletion)
            .Where(log => !log.IsExcludedFromBudget)
            .Where(log => log.OccurredOn.Date >= period.Start && log.OccurredOn.Date <= period.End)
            .Where(log => log.ExpenseCategory.HasValue)
            .GroupBy(log => log.ExpenseCategory!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));

    public static BudgetAllocationCategoryState GetCategoryState(
        BudgetAllocationSnapshot snapshot,
        ExpenseCategory category) => category switch
        {
            ExpenseCategory.Wants => snapshot.Wants,
            ExpenseCategory.Savings => snapshot.Invest,
            _ => snapshot.Needs
        };

    public static string GetExpenseCategoryLabel(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Wants => "Wants",
        ExpenseCategory.Savings => "Invest",
        _ => "Needs"
    };

    public static void AddDebtDelta(BudgetAllocation allocation, ExpenseCategory category, decimal debtDelta)
    {
        if (category == ExpenseCategory.Wants)
            allocation.WantsDebt += debtDelta;
        else if (category == ExpenseCategory.Savings)
            allocation.InvestDebt += debtDelta;
        else
            allocation.NeedsDebt += debtDelta;
    }
}
