using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class CalendarVMTests
{
    [Fact]
    public void CalendarVM_Constructor_LoadsCurrentMonthWithBufferedWeeks()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        Assert.Equal(new DateOnly(2026, 6, 12), vm.SelectedDate);
        Assert.Equal("June 2026", vm.VisibleMonthLabel);
        Assert.Equal(10, vm.BufferedWeeks.Count);
        Assert.Equal(6, vm.FrameWeeks.Count);
        Assert.Equal(new DateOnly(2026, 5, 17), vm.BufferedWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 5, 31), vm.FrameWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 11), vm.FrameWeeks[5].Days[6].Date);
        Assert.Equal(new DateOnly(2026, 7, 12), vm.BufferedWeeks[8].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 19), vm.BufferedWeeks[9].Days[0].Date);
        Assert.True(vm.FrameWeeks[1].Days[5].IsSelected);
        Assert.True(vm.FrameWeeks[0].Days[0].IsOutsideVisibleMonth);
        Assert.True(vm.FrameWeeks[1].Days[5].IsCurrentDay);
    }

    [Fact]
    public void CalendarVM_ShiftDown_DropsFirstFrameWeekAndAppendsNextFrameWeek()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        vm.ShiftCalendarFrameRowsCommand.Execute(1);

        Assert.Equal(new DateOnly(2026, 6, 7), vm.FrameWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 18), vm.FrameWeeks[5].Days[6].Date);
        Assert.Equal(new DateOnly(2026, 5, 24), vm.BufferedWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 26), vm.BufferedWeeks[9].Days[0].Date);
    }

    [Fact]
    public void CalendarVM_ShiftUp_PrependsPreviousFrameWeekAndDropsLastFrameWeek()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        vm.ShiftCalendarFrameRowsCommand.Execute(-1);

        Assert.Equal(new DateOnly(2026, 5, 24), vm.FrameWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 4), vm.FrameWeeks[5].Days[6].Date);
        Assert.Equal(new DateOnly(2026, 5, 10), vm.BufferedWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 12), vm.BufferedWeeks[9].Days[0].Date);
    }

    [Fact]
    public void CalendarVM_ShiftDown_WhenNextMonthHasMoreVisibleDates_SwitchesBrightMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        vm.ShiftCalendarFrameRowsCommand.Execute(1);
        vm.ShiftCalendarFrameRowsCommand.Execute(1);

        Assert.Equal("July 2026", vm.VisibleMonthLabel);
        var julyFirst = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 7, 1));
        Assert.False(julyFirst.IsOutsideVisibleMonth);
        var juneThirtieth = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 6, 30));
        Assert.True(juneThirtieth.IsOutsideVisibleMonth);
    }

    [Fact]
    public void CalendarVM_ShiftDown_WhenNextMonthStaysDominant_KeepsBrightMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        vm.ShiftCalendarFrameRowsCommand.Execute(1);
        vm.ShiftCalendarFrameRowsCommand.Execute(1);

        Assert.Equal("July 2026", vm.VisibleMonthLabel);
        var julyFirst = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 7, 1));
        Assert.False(julyFirst.IsOutsideVisibleMonth);
        var juneTwentyEighth = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 6, 28));
        Assert.True(juneTwentyEighth.IsOutsideVisibleMonth);
    }

    [Fact]
    public void CalendarVM_ShiftUp_WhenPreviousMonthHasMoreVisibleDates_SwitchesBrightMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        vm.ShiftCalendarFrameRowsCommand.Execute(-1);
        vm.ShiftCalendarFrameRowsCommand.Execute(-1);
        vm.ShiftCalendarFrameRowsCommand.Execute(-1);

        Assert.Equal("May 2026", vm.VisibleMonthLabel);
        var mayThirtyFirst = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 5, 31));
        Assert.False(mayThirtyFirst.IsOutsideVisibleMonth);
        var juneFirst = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 6, 1));
        Assert.True(juneFirst.IsOutsideVisibleMonth);
    }

    [Fact]
    public void CalendarVM_ShiftDown_WhenVisibleDatesAreTied_KeepsCurrentBrightMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 2, 12));

        vm.ShiftCalendarFrameRowsCommand.Execute(1);

        Assert.Equal("February 2026", vm.VisibleMonthLabel);
        var februaryTwentyEighth = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 2, 28));
        Assert.False(februaryTwentyEighth.IsOutsideVisibleMonth);
        var marchFirst = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 3, 1));
        Assert.True(marchFirst.IsOutsideVisibleMonth);
    }

    [Fact]
    public async Task CalendarVM_NavigateToNextMonth_FillsGridFromFirstWeekOfNextMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        await vm.NavigateToNextMonthCommand.ExecuteAsync(null);

        Assert.Equal("July 2026", vm.VisibleMonthLabel);
        Assert.Equal(new DateOnly(2026, 6, 28), vm.FrameWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 8, 8), vm.FrameWeeks[5].Days[6].Date);
        Assert.True(vm.FrameWeeks[0].Days[0].IsOutsideVisibleMonth);
        Assert.False(vm.FrameWeeks[0].Days[3].IsOutsideVisibleMonth);
    }

    [Fact]
    public async Task CalendarVM_NavigateToPreviousMonth_FillsGridFromFirstWeekOfPreviousMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        await vm.NavigateToPreviousMonthCommand.ExecuteAsync(null);

        Assert.Equal("May 2026", vm.VisibleMonthLabel);
        Assert.Equal(new DateOnly(2026, 4, 26), vm.FrameWeeks[0].Days[0].Date);
        Assert.Equal(new DateOnly(2026, 6, 6), vm.FrameWeeks[5].Days[6].Date);
        Assert.True(vm.FrameWeeks[0].Days[0].IsOutsideVisibleMonth);
        Assert.False(vm.FrameWeeks[0].Days[5].IsOutsideVisibleMonth);
    }

    [Fact]
    public async Task CalendarVM_NavigateToNextMonth_ScrollsThroughIntermediateWeekStartsBeforeSettlingOnTargetMonth()
    {
        CalendarVM? vm = null;
        var observedWeekStarts = new List<DateOnly>();
        vm = CreateVm(
            currentDate: new DateTime(2026, 6, 12),
            navigationDelayAsync: _ =>
            {
                observedWeekStarts.Add(vm!.FrameWeeks[0].Days[0].Date);
                return Task.CompletedTask;
            });

        await vm.NavigateToNextMonthCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                new DateOnly(2026, 6, 7),
                new DateOnly(2026, 6, 14),
                new DateOnly(2026, 6, 21)
            ],
            observedWeekStarts);
        Assert.Equal(new DateOnly(2026, 6, 28), vm.FrameWeeks[0].Days[0].Date);
        Assert.Equal("July 2026", vm.VisibleMonthLabel);
    }

    [Fact]
    public async Task CalendarVM_SelectDate_LoadsCalendarDataAndUpdatesSummaryAndEmptyStates()
    {
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(new DateOnly(2026, 6, 12), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CalendarDto(
                new DateOnly(2026, 6, 12),
                92m,
                450m,
                [new CalendarExpenseItem(1, "Groceries", 74m, "Checking", "Food")],
                [new CalendarIncomeItem(2, "Freelance", 450m, "Checking")],
                [],
                [new CalendarRecurringTransactionItem(3, "Rent", 1200m, RecurringTransactionType.Expense, RecurringPeriod.Monthly, 12, "Checking")])));
        var vm = new CalendarVM(service, new DateTime(2026, 6, 1));

        await vm.SelectDateAsync(new DateOnly(2026, 6, 12));

        Assert.Equal(new DateOnly(2026, 6, 12), vm.SelectedDate);
        Assert.Equal("92", vm.TotalSpentText);
        Assert.Equal("450", vm.TotalEarnedText);
        Assert.Equal("0", vm.GoalsDueText);
        Assert.Equal("1", vm.PaymentDueText);
        Assert.False(vm.HasNoExpenses);
        Assert.False(vm.HasNoIncomes);
        Assert.True(vm.HasNoGoalDeadlines);
        Assert.False(vm.HasNoRecurringTransactions);
    }

    [Fact]
    public async Task CalendarVM_SelectDate_ListItemAmountTextDoesNotIncludeCurrencySymbol()
    {
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(new DateOnly(2026, 6, 12), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CalendarDto(
                new DateOnly(2026, 6, 12),
                92m,
                450m,
                [new CalendarExpenseItem(1, "Groceries", 74m, "Checking", "Food")],
                [new CalendarIncomeItem(2, "Freelance", 450m, "Checking")],
                [new CalendarGoalDeadlineItem(4, "Vacation", 1250m, 5000m, new DateTime(2026, 6, 12))],
                [new CalendarRecurringTransactionItem(3, "Rent", 1200m, RecurringTransactionType.Expense, RecurringPeriod.Monthly, 12, "Checking")])));
        var vm = new CalendarVM(service, new DateTime(2026, 6, 1));

        await vm.SelectDateAsync(new DateOnly(2026, 6, 12));

        Assert.Equal("74", vm.Expenses.Single().AmountText);
        Assert.Equal("450", vm.Incomes.Single().AmountText);
        Assert.Equal("1,250 / 5,000", vm.GoalDeadlines.Single().ProgressText);
        Assert.Equal("1,200", vm.RecurringTransactions.Single().AmountText);
    }

    [Theory]
    [InlineData(-1, 2026, 6, 11)]
    [InlineData(1, 2026, 6, 13)]
    [InlineData(-7, 2026, 6, 5)]
    [InlineData(7, 2026, 6, 19)]
    public async Task CalendarVM_SelectRelativeDateAsync_SelectsDateRelativeToCurrentSelection(
        int dayOffset,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        await vm.SelectRelativeDateAsync(dayOffset);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), vm.SelectedDate);
        Assert.Contains(vm.FrameWeeks.SelectMany(week => week.Days), day => day.Date == vm.SelectedDate && day.IsSelected);
    }

    [Fact]
    public async Task CalendarVM_SelectCurrentDateAsync_SelectsTodayAndRestoresCurrentMonthFrame()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));
        await vm.SelectDateAsync(new DateOnly(2026, 7, 15));

        await vm.SelectCurrentDateAsync();

        Assert.Equal(new DateOnly(2026, 6, 12), vm.SelectedDate);
        Assert.Equal("June 2026", vm.VisibleMonthLabel);
        Assert.Equal(new DateOnly(2026, 5, 31), vm.FrameWeeks[0].Days[0].Date);
    }

    [Fact]
    public void CalendarVM_Constructor_WhenSelectedDateIsCurrentDate_IsAtCurrentDay()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        Assert.True(vm.IsAtCurrentDay);
    }

    [Fact]
    public async Task CalendarVM_SelectDate_WhenSelectedDateChanges_UpdatesIsAtCurrentDay()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 12));

        await vm.SelectDateAsync(new DateOnly(2026, 6, 13));

        Assert.False(vm.IsAtCurrentDay);

        await vm.SelectDateAsync(new DateOnly(2026, 6, 12));

        Assert.True(vm.IsAtCurrentDay);
    }

    [Fact]
    public async Task CalendarVM_SelectCurrentDateAsync_WhenAlreadyCurrent_DoesNotReloadCalendarData()
    {
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(CreateDto(call.Arg<DateOnly>(), totalSpent: 0m)));
        var vm = new CalendarVM(service, new DateTime(2026, 6, 12));

        await vm.SelectCurrentDateAsync();

        await service.DidNotReceive().GetCalendarDayAsync(
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CalendarVM_SelectDate_WhenSelectedDateIsInDifferentMonth_BrightensSelectedMonth()
    {
        var vm = CreateVm(currentDate: new DateTime(2026, 6, 5));

        await vm.SelectDateAsync(new DateOnly(2026, 5, 29));

        Assert.Equal("May 2026", vm.VisibleMonthLabel);
        var mayTwentyNinth = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 5, 29));
        Assert.False(mayTwentyNinth.IsOutsideVisibleMonth);
        var juneFirst = vm.FrameWeeks.SelectMany(week => week.Days).Single(day => day.Date == new DateOnly(2026, 6, 1));
        Assert.True(juneFirst.IsOutsideVisibleMonth);
    }

    [Fact]
    public async Task CalendarVM_SelectDate_WhenOlderLoadCompletesAfterNewerLoad_KeepsNewerDetails()
    {
        var olderDate = new DateOnly(2026, 6, 12);
        var newerDate = new DateOnly(2026, 6, 13);
        var olderLoad = new TaskCompletionSource<CalendarDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerLoad = new TaskCompletionSource<CalendarDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(olderDate, Arg.Any<CancellationToken>()).Returns(olderLoad.Task);
        service.GetCalendarDayAsync(newerDate, Arg.Any<CancellationToken>()).Returns(newerLoad.Task);
        var vm = new CalendarVM(service, new DateTime(2026, 6, 1));

        var olderSelection = vm.SelectDateAsync(olderDate);
        var newerSelection = vm.SelectDateAsync(newerDate);
        newerLoad.SetResult(CreateDto(newerDate, totalSpent: 200m));
        await newerSelection;

        olderLoad.SetResult(CreateDto(olderDate, totalSpent: 100m));
        await olderSelection;

        Assert.Equal(newerDate, vm.SelectedDate);
        Assert.Equal("200", vm.TotalSpentText);
    }

    [Fact]
    public async Task CalendarVM_SelectDate_WhenNewerLoadCancelsOlderLoad_DoesNotDisposeOlderTokenBeforeItFinishes()
    {
        var olderDate = new DateOnly(2026, 6, 12);
        var newerDate = new DateOnly(2026, 6, 13);
        var inspectOlderToken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(olderDate, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                await inspectOlderToken.Task;
                using var registration = token.Register(() => { });
                return CreateDto(olderDate, totalSpent: 100m);
            });
        service.GetCalendarDayAsync(newerDate, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateDto(newerDate, totalSpent: 200m)));
        var vm = new CalendarVM(service, new DateTime(2026, 6, 1));

        var olderSelection = vm.SelectDateAsync(olderDate);
        var newerSelection = vm.SelectDateAsync(newerDate);
        await newerSelection;
        inspectOlderToken.SetResult();
        await olderSelection;

        Assert.Equal(newerDate, vm.SelectedDate);
        Assert.Equal("200", vm.TotalSpentText);
    }

    [Fact]
    public async Task CalendarVM_SelectDate_WhenCurrentLoadIsCanceled_ClearsLoadingState()
    {
        using var cts = new CancellationTokenSource();
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(new DateOnly(2026, 6, 12), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var load = new TaskCompletionSource<CalendarDto>(TaskCreationOptions.RunContinuationsAsynchronously);
                var token = call.Arg<CancellationToken>();
                token.Register(() => load.SetCanceled(token));
                return load.Task;
            });
        var vm = new CalendarVM(service, new DateTime(2026, 6, 1));

        var selection = vm.SelectDateAsync(new DateOnly(2026, 6, 12), cts.Token);
        cts.Cancel();
        await selection;

        Assert.False(vm.IsLoading);
    }

    private static CalendarVM CreateVm(
        DateTime currentDate,
        Func<TimeSpan, Task>? navigationDelayAsync = null)
    {
        var service = Substitute.For<ICalendarService>();
        service.GetCalendarDayAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new CalendarDto(
                call.Arg<DateOnly>(),
                0m,
                0m,
                [],
                [],
                [],
                [])));

        return new CalendarVM(service, currentDate, navigationDelayAsync);
    }

    private static CalendarDto CreateDto(DateOnly date, decimal totalSpent)
    {
        return new CalendarDto(
            date,
            totalSpent,
            0m,
            [],
            [],
            [],
            []);
    }
}
