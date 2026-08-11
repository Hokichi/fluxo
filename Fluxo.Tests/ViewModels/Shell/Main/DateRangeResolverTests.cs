using Fluxo.Core.Enums;
using Fluxo.Helpers.MainWindow;
using Fluxo.Core.Entities;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public class DateRangeResolverTests
{
    [Fact]
    public void DateRangeResolver_ResolveAllTransactions_UsesEarliestDateThroughToday()
    {
        var today = new DateTime(2026, 6, 30);

        var result = DateRangeResolver.ResolveAllTransactions(
            [new DateTime(2026, 6, 20, 12, 0, 0), new DateTime(2026, 5, 4), new DateTime(2026, 7, 1)],
            today);

        Assert.Equal(new DateTime(2026, 5, 4), result.From);
        Assert.Equal(today, result.To);
    }

    [Fact]
    public void DateRangeResolver_ResolveAllTransactions_WithoutPastTransactions_UsesTodayForBothBounds()
    {
        var today = new DateTime(2026, 6, 30);

        var result = DateRangeResolver.ResolveAllTransactions([], today);

        Assert.Equal(today, result.From);
        Assert.Equal(today, result.To);
    }

    [Fact]
    public void DateRangeResolver_Resolve_Daily_ReturnsSameFromAndTo()
    {
        var selected = new DateTime(2026, 4, 16);

        var result = DateRangeResolver.Resolve(selected, MainContentViewMode.Daily);

        Assert.Equal(selected, result.From);
        Assert.Equal(selected, result.To);
    }

    [Fact]
    public void DateRangeResolver_Resolve_Weekly_ReturnsMondayToSunday()
    {
        var selected = new DateTime(2026, 4, 19, 14, 30, 0, DateTimeKind.Local);
        var expectedFrom = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Local);
        var expectedTo = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Local);

        var result = DateRangeResolver.Resolve(selected, MainContentViewMode.Weekly);

        Assert.Equal(expectedFrom, result.From);
        Assert.Equal(expectedTo, result.To);
        Assert.Equal(DateTimeKind.Local, result.From.Kind);
        Assert.Equal(DateTimeKind.Local, result.To.Kind);
    }

    [Fact]
    public void DateRangeResolver_Resolve_Monthly_ReturnsFirstToLastDay()
    {
        var selected = new DateTime(2024, 2, 15, 9, 45, 0, DateTimeKind.Utc);
        var expectedFrom = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedTo = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc);

        var result = DateRangeResolver.Resolve(selected, MainContentViewMode.Monthly);

        Assert.Equal(expectedFrom, result.From);
        Assert.Equal(expectedTo, result.To);
        Assert.Equal(DateTimeKind.Utc, result.From.Kind);
        Assert.Equal(DateTimeKind.Utc, result.To.Kind);
    }

    [Fact]
    public void DateRangeResolver_Resolve_WeeklyWhenSelectedDayIsSunday_ReturnsMondayToSunday()
    {
        var selected = new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Unspecified);
        var expectedFrom = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Unspecified);
        var expectedTo = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Unspecified);

        var result = DateRangeResolver.Resolve(selected, MainContentViewMode.Weekly);

        Assert.Equal(expectedFrom, result.From);
        Assert.Equal(expectedTo, result.To);
    }

    [Fact]
    public void DateRangeResolver_Resolve_Monthly_PreservesKind()
    {
        var selected = new DateTime(2025, 11, 3, 18, 0, 0, DateTimeKind.Local);

        var result = DateRangeResolver.Resolve(selected, MainContentViewMode.Monthly);

        Assert.Equal(DateTimeKind.Local, result.From.Kind);
        Assert.Equal(DateTimeKind.Local, result.To.Kind);
    }

    [Fact]
    public void DateRangeResolver_Resolve_AllTime_ThrowsInvalidOperationException()
    {
        var selected = new DateTime(2026, 4, 16);

        Assert.Throws<InvalidOperationException>(() =>
            DateRangeResolver.Resolve(selected, MainContentViewMode.AllTime));
    }

    [Fact]
    public void DateRangeResolver_ResolveAllocationPeriod_UsesCurrentBudgetAllocationPeriod()
    {
        var today = new DateTime(2026, 6, 18);
        var budgetAllocation = new BudgetAllocation
        {
            AllocationPeriod = AllocationPeriod.Monthly,
            PeriodStart = 15
        };

        var result = DateRangeResolver.ResolveAllocationPeriod(today, budgetAllocation);

        Assert.Equal(new DateTime(2026, 6, 15), result.From);
        Assert.Equal(new DateTime(2026, 7, 14), result.To);
    }
}
