namespace Fluxo.Helpers.Popups;

public static class MonthlyDueDateHelper
{
    public const int MinMonthlyDay = 1;
    public const int MaxMonthlyDay = 28;

    public static int? Normalize(int? monthlyDueDate)
    {
        if (!monthlyDueDate.HasValue || monthlyDueDate.Value < MinMonthlyDay)
            return null;

        return Math.Min(monthlyDueDate.Value, MaxMonthlyDay);
    }

    public static DateTime? ResolveUpcomingDate(int? monthlyDueDate, DateTime today)
    {
        var normalizedDueDate = Normalize(monthlyDueDate);
        if (!normalizedDueDate.HasValue)
            return null;

        var dueDay = normalizedDueDate.Value;
        var dueDate = new DateTime(today.Year, today.Month, dueDay);
        if (dueDate.Date >= today.Date)
            return dueDate;

        var nextMonth = today.AddMonths(1);
        return new DateTime(nextMonth.Year, nextMonth.Month, dueDay);
    }

    public static DateTime? ToPickerDate(int? monthlyDueDate)
    {
        return ResolveUpcomingDate(monthlyDueDate, DateTime.Today);
    }
}
