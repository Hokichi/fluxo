using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Helpers.MainWindow;
using System.Reflection;
using Fluxo.Core.Enums;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Controls;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public class DaySpinnerVMTests
{
    [Fact]
    public void DaySpinnerVM_SelectedDay_InDailyMode_PublishesExpectedRange()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);

        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));
        recipient.DateRanges.Clear();

        var selectedDay = vm.DaysOfWeek.First(day => !ReferenceEquals(day, vm.SelectedDay));

        vm.SelectedDay = selectedDay;

        var expected = DateRangeResolver.Resolve(selectedDay.Date, MainContentViewMode.Daily);

        Assert.Single(recipient.DateRanges);
        Assert.Equal(expected.From, recipient.DateRanges[0].From);
        Assert.Equal(expected.To, recipient.DateRanges[0].To);
    }

    [Fact]
    public void DaySpinnerVM_AllTimeMode_PublishesAllTimeViewModeMessageButKeepsSpinnerVisible()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, AllTimeViewModeMessage>(
            recipient,
            static (target, message) => target.AllTimeMessages.Add(message));

        var vm = new DaySpinnerVM(messenger);
        var visibleDates = vm.DaysOfWeek.Select(day => day.Date).ToArray();
        var selectedDate = vm.SelectedDay.Date;

        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.AllTime));

        Assert.True(vm.IsSpinnerVisible);
        Assert.Equal(visibleDates, vm.DaysOfWeek.Select(day => day.Date));
        Assert.Equal(selectedDate, vm.SelectedDay.Date);
        Assert.Single(recipient.AllTimeMessages);
    }

    [Fact]
    public void DaySpinnerVM_AllocationPeriodMode_DisablesSpinnerWithoutPublishingDateRange()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        var visibleDates = vm.DaysOfWeek.Select(day => day.Date).ToArray();
        var selectedDate = vm.SelectedDay.Date;

        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.AllocationPeriod));

        Assert.True(vm.IsSpinnerVisible);
        Assert.False(vm.IsSpinnerEnabled);
        Assert.False(vm.CanNavigateForward);
        Assert.Equal(visibleDates, vm.DaysOfWeek.Select(day => day.Date));
        Assert.Equal(selectedDate, vm.SelectedDay.Date);
        Assert.Empty(recipient.DateRanges);
    }

    [Fact]
    public void DaySpinnerVM_WeeklyMode_WhenSelectedSunday_PublishesMondayToSundayRange()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);

        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Weekly));
        recipient.DateRanges.Clear();

        var selectedSunday = new DayOfWeekVM
        {
            Date = new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Local),
            DayName = "Sun",
            DayNumber = "19",
            IsSelected = true
        };

        vm.SelectedDay = selectedSunday;

        Assert.Single(recipient.DateRanges);
        Assert.Equal(new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Local), recipient.DateRanges[0].From);
        Assert.Equal(new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Local), recipient.DateRanges[0].To);
    }

    [Fact]
    public void DaySpinnerVM_ComputeWeeklyPageOffset_AcrossIsoYearBoundary_UsesAbsoluteMondayWindows()
    {
        // 2021-01-04 (ISO week 1 of 2021) and 2020-12-28 (ISO week 53 of 2020) both
        // fall within the same 28-day absolute window (2020-12-14 to 2021-01-10), so
        // the offset is 0 — the ISO year boundary does not cause a page split.
        var today = new DateTime(2021, 1, 4, 0, 0, 0, DateTimeKind.Unspecified);
        var previousIsoWeekMonday = new DateTime(2020, 12, 28, 0, 0, 0, DateTimeKind.Unspecified);

        var result = InvokeWeeklyPageOffset(today, previousIsoWeekMonday);

        Assert.Equal(0, result);
    }

    [Fact]
    public void DaySpinnerVM_MoveToCurrentPeriodRequestedMessage_MovesSpinnerToCurrentPeriodAndPublishesRange()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));
        recipient.DateRanges.Clear();

        var today = DateTime.Today;
        var nonCurrentDay = vm.DaysOfWeek.First(day => day.Date.Date != today);
        vm.SelectedDay = nonCurrentDay;
        recipient.DateRanges.Clear();

        messenger.Send(new MoveToCurrentPeriodRequestedMessage());

        Assert.Single(recipient.DateRanges);
        Assert.Equal(today, recipient.DateRanges[0].From);
        Assert.Equal(today, recipient.DateRanges[0].To);
    }

    [Fact]
    public void DaySpinnerVM_NavigateSpinnerBack_WhenSelectedDateIsOutOfVisibleRange_ShowsNoHighlightedItemWithoutPublishingRange()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));

        var selectedDateBeforeNavigation = vm.SelectedDay.Date;
        recipient.DateRanges.Clear();

        vm.NavigateSpinnerBackCommand.Execute(null);

        Assert.Empty(recipient.DateRanges);
        Assert.Equal(selectedDateBeforeNavigation, vm.SelectedDay.Date);
        Assert.All(vm.DaysOfWeek, day => Assert.False(day.IsSelected));
        Assert.False(vm.IsAtCurrentPeriod);
    }

    [Fact]
    public void DaySpinnerVM_NavigateSpinnerForward_WhenReturningToVisibleRange_RehighlightsCurrentSelectionWithoutPublishingRange()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));

        var persistedSelectedDate = vm.SelectedDay.Date;
        vm.NavigateSpinnerBackCommand.Execute(null);
        recipient.DateRanges.Clear();

        vm.NavigateSpinnerForwardCommand.Execute(null);

        Assert.Empty(recipient.DateRanges);
        Assert.Equal(persistedSelectedDate, vm.SelectedDay.Date);
        var selectedVisibleDay = Assert.Single(vm.DaysOfWeek, day => day.IsSelected);
        Assert.Equal(persistedSelectedDate, selectedVisibleDay.Date);
    }

    [Fact]
    public void DaySpinnerVM_NavigateSpinnerForward_WhenFutureNavigationAllowed_MovesIntoFuturePage()
    {
        var vm = new DaySpinnerVM
        {
            AllowFuturePeriodNavigation = true
        };
        var initialDates = vm.DaysOfWeek.Select(day => day.Date).ToArray();

        vm.NavigateSpinnerForwardCommand.Execute(null);

        Assert.NotEqual(initialDates, vm.DaysOfWeek.Select(day => day.Date));
        Assert.True(vm.CanNavigateForward);
        Assert.All(vm.DaysOfWeek, day => Assert.False(day.IsSelected));
        Assert.False(vm.IsAtCurrentPeriod);
    }

    [Fact]
    public void DaySpinnerVM_NavigateSpinnerForward_WhenFutureNavigationDisabled_DoesNotMoveIntoFuturePage()
    {
        var vm = new DaySpinnerVM();
        var initialDates = vm.DaysOfWeek.Select(day => day.Date).ToArray();

        vm.NavigateSpinnerForwardCommand.Execute(null);

        Assert.Equal(initialDates, vm.DaysOfWeek.Select(day => day.Date));
        Assert.False(vm.CanNavigateForward);
    }

    [Fact]
    public async Task DaySpinnerVM_SelectAdjacentVisibleDayFromUserAsync_WhenFutureNavigationDisabled_DoesNotSelectFuturePeriod()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));
        var vm = new DaySpinnerVM(messenger);
        var today = DateTime.Today;
        var currentDay = vm.DaysOfWeek.Single(day => day.Date.Date == today);
        vm.SelectedDay = currentDay;
        recipient.DateRanges.Clear();

        await vm.SelectAdjacentVisibleDayFromUserAsync(1);

        Assert.Equal(today, vm.SelectedDay.Date.Date);
        Assert.Empty(recipient.DateRanges);
    }

    [Fact]
    public async Task DaySpinnerVM_SelectAdjacentVisibleDayFromUserAsync_WhenFutureNavigationAllowed_SelectsNextFuturePageDay()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));
        var vm = new DaySpinnerVM(messenger)
        {
            AllowFuturePeriodNavigation = true
        };
        vm.NavigateSpinnerForwardCommand.Execute(null);
        vm.SelectedDay = vm.DaysOfWeek[0];
        recipient.DateRanges.Clear();

        await vm.SelectAdjacentVisibleDayFromUserAsync(1);

        Assert.Equal(vm.DaysOfWeek[1].Date.Date, vm.SelectedDay.Date.Date);
        Assert.True(vm.SelectedDay.Date.Date > DateTime.Today);
        Assert.Single(recipient.DateRanges);
    }

    [Fact]
    public void DaySpinnerVM_SelectingNonCurrentDay_PublishesSpinnerStateWithIsAtCurrentPeriodFalse()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, SpinnerPeriodStateChangedMessage>(
            recipient,
            static (target, message) => target.SpinnerStates.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));

        recipient.SpinnerStates.Clear();
        var today = DateTime.Today;
        var nonCurrentDay = vm.DaysOfWeek.First(day => day.Date.Date != today);
        vm.SelectedDay = nonCurrentDay;

        Assert.NotEmpty(recipient.SpinnerStates);
        Assert.False(recipient.SpinnerStates[^1].IsAtCurrentPeriod);
    }

    [Fact]
    public async Task DaySpinnerVM_SelectAdjacentVisibleDayFromUserAsync_SelectsNextVisibleDayWithoutChangingPage()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        vm.AllowFuturePeriodNavigation = true;
        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));
        var visibleDates = vm.DaysOfWeek.Select(day => day.Date).ToArray();
        var firstVisibleDay = vm.DaysOfWeek[0];
        vm.SelectedDay = firstVisibleDay;
        recipient.DateRanges.Clear();

        await vm.SelectAdjacentVisibleDayFromUserAsync(1);

        Assert.Equal(vm.DaysOfWeek[1].Date, vm.SelectedDay.Date);
        Assert.Equal(firstVisibleDay.Date.AddDays(1), vm.SelectedDay.Date);
        Assert.Equal(visibleDates, vm.DaysOfWeek.Select(day => day.Date));
        Assert.Single(recipient.DateRanges);
        Assert.Equal(vm.SelectedDay.Date.Date, recipient.DateRanges[0].From);
    }

    [Fact]
    public async Task DaySpinnerVM_SelectAdjacentVisibleDayFromUserAsync_DoesNotNavigateOutsideVisibleItems()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new MessageCaptureRecipient();
        messenger.Register<MessageCaptureRecipient, DateRangeSelectionChangedMessage>(
            recipient,
            static (target, message) => target.DateRanges.Add(message.Value));

        var vm = new DaySpinnerVM(messenger);
        messenger.Send(new ViewModeChangeMessage(MainContentViewMode.Daily));
        vm.SelectedDay = vm.DaysOfWeek[0];
        recipient.DateRanges.Clear();

        await vm.SelectAdjacentVisibleDayFromUserAsync(-1);

        Assert.Equal(vm.DaysOfWeek[0].Date, vm.SelectedDay.Date);
        Assert.Empty(recipient.DateRanges);
    }

    private static int InvokeWeeklyPageOffset(DateTime today, DateTime referenceDate)
    {
        var method = typeof(DaySpinnerVM).GetMethod(
            "ComputeWeeklyPageOffset",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        return (int)method!.Invoke(null, [today, referenceDate])!;
    }

    private sealed class MessageCaptureRecipient
    {
        public List<(DateTime From, DateTime To)> DateRanges { get; } = [];

        public List<AllTimeViewModeMessage> AllTimeMessages { get; } = [];

        public List<SpinnerPeriodState> SpinnerStates { get; } = [];
    }
}
