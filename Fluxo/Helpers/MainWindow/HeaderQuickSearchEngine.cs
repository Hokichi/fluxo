using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.MainWindow;

public sealed record HeaderQuickSearchResult(TransactionVM Transaction)
{
    public int Id => Transaction.Id;
    public string Name => Transaction.Name.Trim();
    public decimal Amount => Transaction.Amount;
    public DateTime Date => Transaction.OccurredOn;
    public string AccountName => Transaction.Account?.Name?.Trim() ?? string.Empty;
    public string? TagName => Transaction.Tag?.Name;
    public string? TagBrush => Transaction.Tag?.HexCode;
    public bool IsExpense => Transaction.Type == TransactionType.Expense;
    public bool IsIncome => Transaction.Type == TransactionType.Income;
    public string IconResourceKey => IsIncome ? "BanknoteArrowUp" : "BanknoteArrowDown";
    public string IconBrushKey => IsIncome ? "Brush.Success" : "Brush.Danger";
}

public static class HeaderQuickSearchEngine
{
    public static IEnumerable<HeaderQuickSearchResult> Search(
        IEnumerable<TransactionVM> transactions,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery) || normalizedQuery.Length <= 3)
            return [];

        return transactions
            .Where(transaction => transaction.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.LoggedOn)
            .Take(5)
            .Select(transaction => new HeaderQuickSearchResult(transaction));
    }
}
