using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

using Fluxo.DataModels.Popups.AddNewTransactionHistoryBuilder;
namespace Fluxo.ViewModels.Popups;

public static class AddNewTransactionHistoryBuilder
{
    public static IReadOnlyList<AddNewTransactionHistoryItemVM> BuildPinnedExpenses(
        IEnumerable<Transaction> expenseLogs,
        int pageSize = int.MaxValue)
    {
        return ProjectExpenses(expenseLogs, isPinned: true)
            .GroupBy(item => new ExpenseKey(
                NormalizeName(item.Name),
                item.Amount,
                item.AccountId,
                item.Note,
                item.Category ?? ExpenseCategory.Needs,
                item.TagId ?? 0))
            .Select(SelectNewest)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .Take(pageSize)
            .ToList();
    }

    public static IReadOnlyList<AddNewTransactionHistoryItemVM> BuildExpenseHistory(
        IEnumerable<Transaction> expenseLogs,
        int pageSize = int.MaxValue)
    {
        return ProjectExpenses(expenseLogs, isPinned: false)
            .GroupBy(item => new RepeatingExpenseKey(
                NormalizeName(item.Name),
                item.Amount,
                item.AccountId,
                item.TagId ?? 0))
            .Select(SelectNewest)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .Take(pageSize)
            .ToList();
    }

    public static IReadOnlyList<AddNewTransactionHistoryItemVM> BuildPinnedIncomes(
        IEnumerable<Transaction> incomeLogs,
        int pageSize = int.MaxValue)
    {
        return ProjectIncomes(incomeLogs, isPinned: true)
            .GroupBy(item => new IncomeKey(
                NormalizeName(item.Name),
                item.Amount,
                item.AccountId,
                item.Note))
            .Select(SelectNewest)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .Take(pageSize)
            .ToList();
    }

    public static IReadOnlyList<AddNewTransactionHistoryItemVM> BuildIncomeHistory(
        IEnumerable<Transaction> incomeLogs,
        int pageSize = int.MaxValue)
    {
        return ProjectIncomes(incomeLogs, isPinned: false)
            .GroupBy(item => new RepeatingIncomeKey(
                NormalizeName(item.Name),
                item.Amount,
                item.AccountId))
            .Select(SelectNewest)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .Take(pageSize)
            .ToList();
    }

    public static IReadOnlyList<AddNewTransactionHistoryItemVM> BuildGoalUpdateHistory(
        IEnumerable<Transaction> expenseLogs,
        string goalName,
        int pageSize = int.MaxValue)
    {
        var expectedName = BuildGoalUpdateName(goalName);
        return expenseLogs
            .Where(log => !log.IsForDeletion)
            .Where(log => IsGoalUpdateLog(log))
            .Where(log => string.Equals(log.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ThenByDescending(log => log.Id)
            .Take(pageSize)
            .Select(log => new AddNewTransactionHistoryItemVM
            {
                Id = log.Id,
                IsExpense = false,
                IsGoalUpdate = true,
                Name = log.Name,
                Amount = log.Amount,
                AccountId = log.SourceAccountId,
                AccountName = log.Account?.Name ?? string.Empty,
                Note = log.Notes ?? string.Empty,
                Date = log.OccurredOn,
                Category = ExpenseCategory.Savings,
                TagId = log.TagId,
                TagHexCode = log.Tag?.HexCode,
                IsPinned = false
            })
            .ToList();
    }

    private static IEnumerable<AddNewTransactionHistoryItemVM> ProjectExpenses(
        IEnumerable<Transaction> expenseLogs,
        bool isPinned)
    {
        return expenseLogs
            .Where(log => !log.IsForDeletion)
            .Where(log => log.IsPinned == isPinned)
            .Where(log => log.Type == TransactionType.Expense)
            .Where(log => log.Tag?.IsSystemTag != true)
            .Where(log => !string.IsNullOrWhiteSpace(log.Name))
            .Select(log => new AddNewTransactionHistoryItemVM
            {
                Id = log.Id,
                IsExpense = true,
                Name = log.Name,
                Amount = log.Amount,
                AccountId = log.SourceAccountId,
                AccountName = log.Account?.Name ?? string.Empty,
                Note = log.Notes ?? string.Empty,
                Date = log.OccurredOn,
                Category = log.ExpenseCategory,
                TagId = log.TagId,
                TagHexCode = log.Tag?.HexCode,
                IsPinned = log.IsPinned
            });
    }

    private static IEnumerable<AddNewTransactionHistoryItemVM> ProjectIncomes(
        IEnumerable<Transaction> incomeLogs,
        bool isPinned)
    {
        return incomeLogs
            .Where(log => log.Type == TransactionType.Income && !log.IsForDeletion)
            .Where(log => log.IsPinned == isPinned)
            .Where(log => !string.IsNullOrWhiteSpace(log.Name))
            .Select(log => new AddNewTransactionHistoryItemVM
            {
                Id = log.Id,
                IsExpense = false,
                Name = log.Name,
                Amount = log.Amount,
                AccountId = log.SourceAccountId,
                AccountName = log.Account?.Name ?? string.Empty,
                Note = log.Notes ?? string.Empty,
                Date = log.OccurredOn,
                IsPinned = log.IsPinned
            });
    }

    private static AddNewTransactionHistoryItemVM SelectNewest(IEnumerable<AddNewTransactionHistoryItemVM> items)
    {
        return items
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .First();
    }

    private static string NormalizeName(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static bool IsGoalUpdateLog(Transaction log)
    {
        var tagName = log.Tag?.Name;
        var expenseName = log.Name;
        return string.Equals(tagName, GoalUpdateTransactionSupport.GoalUpdateTagName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(expenseName, GoalUpdateTransactionSupport.GoalUpdateTagName, StringComparison.OrdinalIgnoreCase) ||
               expenseName?.StartsWith($"{GoalUpdateTransactionSupport.GoalUpdateTagName}:", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string BuildGoalUpdateName(string goalName)
    {
        var trimmedGoalName = goalName.Trim();
        return string.IsNullOrWhiteSpace(trimmedGoalName)
            ? GoalUpdateTransactionSupport.GoalUpdateTagName
            : $"{GoalUpdateTransactionSupport.GoalUpdateTagName}: {trimmedGoalName}";
    }




}
