using System.Globalization;
using Fluxo.Core.Enums;
using Fluxo.Helpers.Popups;

namespace Fluxo.Helpers.Transaction;

public static class RecurringTransactionValidationHelper
{
    public static Result ValidateTime(RecurringPeriod period, string? text)
    {
        if (period == RecurringPeriod.None)
            return Result.Success();

        return TryNormalizeTime(period, text, out _)
            ? Result.Success()
            : Result.Failure(GetTimeValidationMessage(period));
    }

    public static Result ValidateInstallments(
        RecurringPeriod period,
        string? recurringTimeText,
        DateTime endDate,
        DateTime startDate)
    {
        if (period == RecurringPeriod.None)
            return Result.Failure("Installments need a weekly, biweekly, or monthly recurrence.");

        var timeValidation = ValidateTime(period, recurringTimeText);
        if (!timeValidation.IsValid)
            return timeValidation;
        TryNormalizeTime(period, recurringTimeText, out var recurringTime);

        startDate = startDate.Date;
        endDate = endDate.Date;
        if (endDate < startDate)
            return Result.Failure("Installment end date must be today or later.");

        var count = 0;
        var occurrence = FindClosestOccurrence(startDate, period, recurringTime);
        while (occurrence <= endDate)
        {
            count++;
            occurrence = AddOccurrence(occurrence, period);
        }

        return count > 0
            ? Result.Success(count)
            : Result.Failure("Installment end date must include at least one recurrence.");
    }

    public static bool TryNormalizeTime(RecurringPeriod period, string? text, out int recurringTime)
    {
        recurringTime = 0;
        if (period == RecurringPeriod.None)
            return true;

        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        var max = IsWeekdayPeriod(period) ? 7 : MonthlyDueDateHelper.MaxMonthlyDay;
        if (parsed < 1 || parsed > max)
            return false;

        recurringTime = parsed;
        return true;
    }

    public static bool IsWeekdayPeriod(RecurringPeriod period) =>
        period is RecurringPeriod.Weekly or RecurringPeriod.Biweekly;

    public static string GetDefaultTimeText(RecurringPeriod period, DateTime today)
    {
        if (period == RecurringPeriod.None)
            return string.Empty;
        if (IsWeekdayPeriod(period))
            return GetIsoDayOfWeek(today.DayOfWeek).ToString(CultureInfo.InvariantCulture);
        return MonthlyDueDateHelper.Normalize(today.Day)?.ToString(CultureInfo.InvariantCulture) ?? "1";
    }

    public static string FormatScheduleLabel(RecurringPeriod period, string? recurringTimeText, CultureInfo culture)
    {
        if (!TryNormalizeTime(period, recurringTimeText, out var recurringTime))
            return string.Empty;
        if (period == RecurringPeriod.Monthly)
            return FormatOrdinal(recurringTime);
        return recurringTime is >= 1 and <= 7 ? culture.DateTimeFormat.DayNames[recurringTime % 7] : string.Empty;
    }

    public static string GetTimeValidationMessage(RecurringPeriod period) => IsWeekdayPeriod(period)
        ? "Recurring weekday must be between Monday and Sunday."
        : "Recurring day must be between 1 and 28.";

    private static DateTime FindClosestOccurrence(DateTime today, RecurringPeriod period, int recurringTime)
    {
        if (period == RecurringPeriod.Monthly)
        {
            var candidate = new DateTime(today.Year, today.Month, recurringTime);
            var previous = candidate <= today ? candidate : candidate.AddMonths(-1);
            var next = candidate >= today ? candidate : candidate.AddMonths(1);
            return IsCloserToToday(next, previous, today) ? next : previous;
        }

        var previousDate = today;
        while (GetIsoDayOfWeek(previousDate.DayOfWeek) != recurringTime)
            previousDate = previousDate.AddDays(-1);
        var nextDate = today;
        while (GetIsoDayOfWeek(nextDate.DayOfWeek) != recurringTime)
            nextDate = nextDate.AddDays(1);
        return IsCloserToToday(nextDate, previousDate, today) ? nextDate : previousDate;
    }

    private static bool IsCloserToToday(DateTime candidate, DateTime comparison, DateTime today) =>
        Math.Abs((candidate.Date - today.Date).TotalDays) < Math.Abs((comparison.Date - today.Date).TotalDays);

    private static DateTime AddOccurrence(DateTime occurrence, RecurringPeriod period) => period switch
    {
        RecurringPeriod.Weekly => occurrence.AddDays(7),
        RecurringPeriod.Biweekly => occurrence.AddDays(14),
        RecurringPeriod.Monthly => occurrence.AddMonths(1),
        _ => occurrence
    };

    private static string FormatOrdinal(int value)
    {
        var suffix = (value % 100) is 11 or 12 or 13 ? "th" : (value % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };
        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static int GetIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;

    public readonly record struct Result(bool IsValid, string? ErrorMessage, int OccurrenceCount)
    {
        public static Result Success(int occurrenceCount = 0) => new(true, null, occurrenceCount);
        public static Result Failure(string errorMessage) => new(false, errorMessage, 0);
    }
}
