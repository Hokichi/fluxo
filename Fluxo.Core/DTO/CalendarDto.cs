namespace Fluxo.Core.DTO;

public sealed record CalendarDto(
    DateOnly Date,
    decimal TotalSpent,
    decimal TotalEarned,
    IReadOnlyList<CalendarExpenseItem> Expenses,
    IReadOnlyList<CalendarIncomeItem> Incomes,
    IReadOnlyList<CalendarGoalDeadlineItem> GoalDeadlines,
    IReadOnlyList<CalendarRecurringTransactionItem> RecurringTransactions)
{
    public int GoalsDue => GoalDeadlines.Count;
    public int PaymentsDue => RecurringTransactions.Count;
}
