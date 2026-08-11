using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Persistence;

public sealed class CalendarService(IDataOperationRunner dataOperationRunner) : ICalendarService
{
    public async Task<CalendarDto> GetCalendarDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await dataOperationRunner.RunAsync("load calendar data", async (scope, ct) =>
        {
            var unitOfWork = scope.UnitOfWork;
            var selectedDate = date.ToDateTime(TimeOnly.MinValue).Date;

            var transactions = await unitOfWork.Transactions.GetAllAsync(ct);
            var goals = await unitOfWork.SavingGoals.GetAllAsync(ct);
            var recurringTransactions = await unitOfWork.RecurringTransactions.GetAllAsync(ct);

            var expenses = transactions
                .Where(transaction => transaction.Type == TransactionType.Expense && !transaction.IsForDeletion &&
                                      transaction.ParentTransactionId is null && transaction.OccurredOn.Date == selectedDate)
                .OrderByDescending(transaction => transaction.OccurredOn)
                .ThenByDescending(transaction => transaction.LoggedOn)
                .ThenBy(transaction => transaction.Id)
                .Select(transaction => new CalendarExpenseItem(
                    transaction.Id,
                    transaction.Name,
                    transaction.Amount,
                    transaction.Account?.Name ?? string.Empty,
                    transaction.Tag?.Name))
                .ToArray();

            var incomes = transactions
                .Where(transaction => transaction.Type == TransactionType.Income && transaction.OccurredOn.Date == selectedDate)
                .OrderByDescending(transaction => transaction.OccurredOn)
                .ThenByDescending(transaction => transaction.LoggedOn)
                .ThenBy(transaction => transaction.Id)
                .Select(transaction => new CalendarIncomeItem(
                    transaction.Id,
                    transaction.Name,
                    transaction.Amount,
                    transaction.Account?.Name ?? string.Empty))
                .ToArray();

            var goalDeadlines = goals
                .Where(goal => goal.SavingEndDate.HasValue && goal.SavingEndDate.Value.Date == selectedDate)
                .OrderBy(goal => goal.Name)
                .ThenBy(goal => goal.Id)
                .Select(goal => new CalendarGoalDeadlineItem(
                    goal.Id,
                    goal.Name,
                    goal.CurrentAmount,
                    goal.TargetAmount,
                    goal.SavingEndDate!.Value))
                .ToArray();

            var dueRecurringTransactions = recurringTransactions
                .Where(transaction => transaction.IsEnabled && IsDueOn(transaction, date))
                .OrderBy(transaction => transaction.Name)
                .ThenBy(transaction => transaction.Id)
                .Select(transaction => new CalendarRecurringTransactionItem(
                    transaction.Id,
                    transaction.Name,
                    transaction.Amount,
                    transaction.Type,
                    transaction.RecurringPeriod,
                    transaction.RecurringTime,
                    transaction.Source?.Name ?? string.Empty))
                .ToArray();

            return new CalendarDto(
                date,
                transactions.Where(transaction =>
                        transaction.Type == TransactionType.Expense &&
                        !transaction.IsForDeletion &&
                        transaction.ExpenseCategory != ExpenseCategory.Excluded &&
                        transaction.ParentTransactionId is null &&
                        transaction.OccurredOn.Date == selectedDate)
                    .Sum(transaction => transaction.Amount),
                transactions.Where(transaction =>
                        transaction.Type == TransactionType.Income &&
                        !transaction.IsForDeletion &&
                        transaction.ExpenseCategory != ExpenseCategory.Excluded &&
                        transaction.OccurredOn.Date == selectedDate)
                    .Sum(transaction => transaction.Amount),
                expenses,
                incomes,
                goalDeadlines,
                dueRecurringTransactions);
        }, cancellationToken);
    }

    internal static bool IsDueOn(RecurringTransaction transaction, DateOnly selectedDate)
    {
        return transaction.RecurringPeriod switch
        {
            RecurringPeriod.None => false,
            RecurringPeriod.Weekly or RecurringPeriod.Biweekly => IsMatchingWeekday(transaction.RecurringTime, selectedDate),
            RecurringPeriod.Monthly => transaction.RecurringTime == selectedDate.Day,
            _ => false
        };
    }

    private static bool IsMatchingWeekday(int recurringTime, DateOnly selectedDate)
    {
        if (recurringTime is < 1 or > 7)
            return false;

        var selectedDay = selectedDate.DayOfWeek == DayOfWeek.Sunday
            ? 7
            : (int)selectedDate.DayOfWeek;
        return selectedDay == recurringTime;
    }
}
