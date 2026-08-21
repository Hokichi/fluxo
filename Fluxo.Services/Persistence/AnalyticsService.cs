using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Persistence;

public sealed class AnalyticsService(IAppDataService appData) : IAnalyticsService
{
    public async Task<AnalyticsDto> GetAnalyticsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
            (from, to) = (to, from);

        var transactions = await appData.GetTransactionsAsync(cancellationToken).ConfigureAwait(false);
        var goals = await appData.GetSavingGoalsAsync(cancellationToken).ConfigureAwait(false);

            var fromDate = from.ToDateTime(TimeOnly.MinValue);
            var toDate = to.ToDateTime(TimeOnly.MaxValue);

            var expenseInPeriod = transactions
                .Where(transaction => transaction.Type == TransactionType.Expense &&
                              !transaction.IsForDeletion &&
                              transaction.ExpenseCategory != ExpenseCategory.Excluded &&
                              transaction.ParentTransactionId is null &&
                              transaction.OccurredOn >= fromDate &&
                              transaction.OccurredOn <= toDate)
                .ToList();

            var incomeInPeriod = transactions
                .Where(transaction => transaction.Type == TransactionType.Income &&
                                      !transaction.IsForDeletion &&
                                      transaction.ExpenseCategory != ExpenseCategory.Excluded &&
                                      transaction.OccurredOn >= fromDate && transaction.OccurredOn <= toDate)
                .ToList();

            var totalIncome = incomeInPeriod.Sum(log => log.Amount);
            var totalExpense = expenseInPeriod.Sum(log => log.Amount);

            var dateSequence = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(offset => DateOnly.FromDayNumber(from.DayNumber + offset))
                .ToArray();

            var incomeByDay = incomeInPeriod
                .GroupBy(transaction => DateOnly.FromDateTime(transaction.OccurredOn))
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

            var expenseByDay = expenseInPeriod
                .GroupBy(transaction => DateOnly.FromDateTime(transaction.OccurredOn))
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

            var timeSeries = dateSequence
                .Select(day => new AnalyticsTimeSeriesPoint(
                    day,
                    incomeByDay.TryGetValue(day, out var income) ? income : 0m,
                    expenseByDay.TryGetValue(day, out var expense) ? expense : 0m))
                .ToArray();

            var categoryTotals = expenseInPeriod
                .Where(transaction => transaction.ExpenseCategory.HasValue)
                .GroupBy(transaction => transaction.ExpenseCategory!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

            var categoryRatio =
                new[]
                {
                    ExpenseCategory.Needs,
                    ExpenseCategory.Wants,
                    ExpenseCategory.Savings
                }
                .Select(category => new AnalyticsCategorySlice(
                    category,
                    categoryTotals.TryGetValue(category, out var total) ? total : 0m))
                .ToArray();

            var tagTotals = expenseInPeriod
                .Where(transaction => transaction.Tag is not null)
                .GroupBy(transaction => new
                {
                    transaction.Tag!.Id,
                    transaction.Tag.Name,
                    transaction.Tag.HexCode
                })
                .Select(group => new AnalyticsTagTotal(
                    group.Key.Name,
                    group.Key.HexCode,
                    group.Sum(item => item.Amount)))
                .OrderByDescending(item => item.Total)
                .ToArray();

            var goalsCreatedInPeriod = goals
                .Where(goal => goal.CreatedOn >= fromDate && goal.CreatedOn <= toDate)
                .OrderByDescending(goal => goal.CreatedOn)
                .ThenBy(goal => goal.Name)
                .Select(goal => new AnalyticsGoalItem(
                    goal.Id,
                    goal.Name,
                    goal.CurrentAmount,
                    goal.TargetAmount,
                    goal.CreatedOn,
                    goal.SavingEndDate))
                .ToArray();

        return new AnalyticsDto(
            TotalIncome: totalIncome,
            TotalExpense: totalExpense,
            TimeSeries: timeSeries,
            CategoryRatio: categoryRatio,
            TopSpendingTags: tagTotals,
            GoalsCreatedInPeriod: goalsCreatedInPeriod);
    }
}
