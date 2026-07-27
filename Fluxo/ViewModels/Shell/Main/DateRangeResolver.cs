using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

using Fluxo.DataModels.Shell.Main.DateRangeResolver;
namespace Fluxo.ViewModels.Shell.Main;


public static class DateRangeResolver
{
    public static DateRange ResolveAllTransactions(IEnumerable<DateTime> occurredDates, DateTime today)
    {
        var end = today.Date;
        var start = occurredDates
            .Select(date => date.Date)
            .Where(date => date <= end)
            .DefaultIfEmpty(end)
            .Min();

        return new DateRange(start, end);
    }

    public static DateRange Resolve(DateTime selectedDate, MainContentViewMode viewMode)
    {
        var date = selectedDate.Date;

        return viewMode switch
        {
            MainContentViewMode.Daily => new DateRange(date, date),
            MainContentViewMode.Weekly =>
                new DateRange(GetStartOfWeek(date), GetStartOfWeek(date).AddDays(6)),
            MainContentViewMode.Monthly => new DateRange(
                CreateDate(date.Year, date.Month, 1, date.Kind),
                CreateDate(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), date.Kind)),
            MainContentViewMode.AllocationPeriod => throw new InvalidOperationException(
                "Allocation-period view requires a budget allocation."),
            MainContentViewMode.AllTime => throw new InvalidOperationException(
                "All-time view does not have a bounded date range."),
            _ => throw new InvalidOperationException($"Unsupported view mode: {viewMode}.")
        };
    }

    public static DateRange ResolveAllocationPeriod(DateTime today, BudgetAllocation budgetAllocation)
    {
        ArgumentNullException.ThrowIfNull(budgetAllocation);

        var period = BudgetAllocationPeriodRules.ResolveCurrentPeriod(
            budgetAllocation.AllocationPeriod,
            today,
            budgetAllocation.PeriodStart);

        return new DateRange(period.Start, period.End);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek;

        return date.AddDays(mondayOffset);
    }

    private static DateTime CreateDate(int year, int month, int day, DateTimeKind kind)
    {
        return new DateTime(year, month, day, 0, 0, 0, kind);
    }
}
