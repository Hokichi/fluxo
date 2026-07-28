using System.Collections.ObjectModel;
using Fluxo.Helpers.MainWindow;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;
using Fluxo.ViewModels.Controls;

namespace Fluxo.ViewModels.Shell.Main;

public partial class DaySpinnerVM : ObservableRecipient,
    IRecipient<ViewModeChangeMessage>,
    IRecipient<MoveToCurrentPeriodRequestedMessage>
{
    private readonly IDialogService? _dialogService;
    private readonly IUiSettleAwaiter? _uiSettleAwaiter;
    private readonly SemaphoreSlim _spinnerInteractionGate = new(1, 1);
    private bool _suppressSelectionPublishing;
    private MainContentViewMode _selectedMainContentViewMode = MainContentViewMode.Daily;
    private int _spinnerPageOffset;

    public DaySpinnerVM(
        IMessenger? messenger = null,
        IDialogService? dialogService = null,
        IUiSettleAwaiter? uiSettleAwaiter = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _dialogService = dialogService;
        _uiSettleAwaiter = uiSettleAwaiter;
        BuildSpinnerForMode(DateTime.Today, publishSelection: false);
        IsActive = true;
    }

    [ObservableProperty]
    private ObservableCollection<DayOfWeekVM> _daysOfWeek = [];

    [ObservableProperty]
    private DayOfWeekVM _selectedDay = new();

    [ObservableProperty]
    private bool _canNavigateForward;

    [ObservableProperty]
    private bool _isSpinnerVisible = true;

    [ObservableProperty]
    private bool _isSpinnerEnabled = true;

    [ObservableProperty]
    private bool _isAtCurrentPeriod = true;

    [ObservableProperty]
    private bool _allowFuturePeriodNavigation;

    public string MoveToCurrentLabel => _selectedMainContentViewMode switch
    {
        MainContentViewMode.Daily => "Move to today",
        MainContentViewMode.Weekly => "Move to current week",
        MainContentViewMode.Monthly => "Move to current month",
        MainContentViewMode.AllocationPeriod => string.Empty,
        _ => string.Empty
    };

    public void Receive(ViewModeChangeMessage message)
    {
        _selectedMainContentViewMode = message.Value;
        OnPropertyChanged(nameof(MoveToCurrentLabel));

        if (message.Value is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
        {
            _suppressSelectionPublishing = true;
            try
            {
                IsSpinnerVisible = true;
                IsSpinnerEnabled = false;
                IsAtCurrentPeriod = message.Value == MainContentViewMode.AllocationPeriod;
                CanNavigateForward = false;
                _spinnerPageOffset = 0;
            }
            finally
            {
                _suppressSelectionPublishing = false;
            }

            PublishSpinnerPeriodState();
            if (message.Value == MainContentViewMode.AllTime)
                Messenger.Send(new AllTimeViewModeMessage());
            return;
        }

        IsSpinnerVisible = true;
        IsSpinnerEnabled = true;
        BuildSpinnerForMode(DateTime.Today, publishSelection: true);
    }

    public void Receive(MoveToCurrentPeriodRequestedMessage message)
    {
        MoveToCurrentPeriodCore();
    }

    public void PublishCurrentSelection()
    {
        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
        {
            if (_selectedMainContentViewMode == MainContentViewMode.AllTime)
                Messenger.Send(new AllTimeViewModeMessage());
            return;
        }

        PublishSelectedRange(SelectedDay.Date);
    }

    public Task NavigateSpinnerBackFromUserAsync(Window? owner = null)
    {
        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
            return Task.CompletedTask;

        NavigateSpinnerBackCore();
        return Task.CompletedTask;
    }

    public Task NavigateSpinnerForwardFromUserAsync(Window? owner = null)
    {
        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
            return Task.CompletedTask;

        if (_spinnerPageOffset >= 0 && !AllowFuturePeriodNavigation)
            return Task.CompletedTask;

        NavigateSpinnerForwardCore();
        return Task.CompletedTask;
    }

    public async Task SelectAdjacentVisibleDayFromUserAsync(int offset, Window? owner = null)
    {
        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod || offset == 0)
            return;

        var selectedIndex = DaysOfWeek.IndexOf(SelectedDay);
        if (selectedIndex < 0)
        {
            var selectedDate = SelectedDay.Date.Date;
            selectedIndex = DaysOfWeek
                .Select((day, index) => new { day, index })
                .FirstOrDefault(item => item.day.Date.Date == selectedDate)
                ?.index ?? -1;
        }

        var targetIndex = selectedIndex + offset;
        if (targetIndex < 0 || targetIndex >= DaysOfWeek.Count)
            return;

        var targetDay = DaysOfWeek[targetIndex];
        if (!AllowFuturePeriodNavigation && IsFuturePeriod(targetDay.Date))
            return;

        await SelectDayFromUserAsync(targetDay, owner);
    }

    public async Task SelectDayFromUserAsync(DayOfWeekVM selectedDay, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(selectedDay);

        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
            return;

        await RunSpinnerInteractionWithFeedbackAsync(
            MainDataLoadingMessageFormatter.Build(_selectedMainContentViewMode, selectedDay.Date),
            () =>
            {
                SelectDay(selectedDay, publishSelection: true);
                return Task.CompletedTask;
            },
            owner);
    }

    [RelayCommand]
    private void NavigateSpinnerBack()
    {
        NavigateSpinnerBackCore();
    }

    [RelayCommand]
    private void NavigateSpinnerForward()
    {
        NavigateSpinnerForwardCore();
    }

    [RelayCommand]
    private void MoveToCurrentPeriod()
    {
        MoveToCurrentPeriodCore();
    }

    partial void OnSelectedDayChanged(DayOfWeekVM value)
    {
        if (_suppressSelectionPublishing)
            return;

        if (value is null)
            return;

        foreach (var item in DaysOfWeek)
            item.IsSelected = ReferenceEquals(item, value);

        IsAtCurrentPeriod = EvaluateIsAtCurrentPeriod(value.Date);
        PublishSelectedRange(value.Date);
        PublishSpinnerPeriodState();
    }

    private void BuildSpinnerForMode(DateTime referenceDate, bool publishSelection)
    {
        ComputeSpinnerOffset(referenceDate);
        BuildSpinnerItems();

        if (publishSelection)
            SelectSpinnerItemForDate(referenceDate);
        else
            SelectFirstSpinnerItem(publishSelection: false);

        CanNavigateForward = AllowFuturePeriodNavigation || _spinnerPageOffset < 0;
    }

    private void PublishSelectedRange(DateTime selectedDate)
    {
        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
            return;

        var range = DateRangeResolver.Resolve(selectedDate, _selectedMainContentViewMode);
        Messenger.Send(new DateRangeSelectionChangedMessage(range.From, range.To));
    }

    private void ComputeSpinnerOffset(DateTime referenceDate)
    {
        var today = DateTime.Today;

        switch (_selectedMainContentViewMode)
        {
            case MainContentViewMode.Daily:
                {
                    var todayMonday = GetMonday(today);
                    var refMonday = GetMonday(referenceDate);
                    _spinnerPageOffset = (refMonday - todayMonday).Days / 7;
                    break;
                }
            case MainContentViewMode.Weekly:
                {
                    _spinnerPageOffset = ComputeWeeklyPageOffset(today, referenceDate);
                    break;
                }
            case MainContentViewMode.Monthly:
                {
                    var todayGroupMonth = (today.Month - 1) / 4 * 4 + 1;
                    var refGroupMonth = (referenceDate.Month - 1) / 4 * 4 + 1;
                    var todayBase = new DateTime(today.Year, todayGroupMonth, 1);
                    var refBase = new DateTime(referenceDate.Year, refGroupMonth, 1);
                    _spinnerPageOffset = ((refBase.Year - todayBase.Year) * 12 + refBase.Month - todayBase.Month) / 4;
                    break;
                }
        }
    }

    private void BuildSpinnerItems()
    {
        switch (_selectedMainContentViewMode)
        {
            case MainContentViewMode.Daily:
                BuildDailySpinnerItems();
                break;

            case MainContentViewMode.Weekly:
                BuildWeeklySpinnerItems();
                break;

            case MainContentViewMode.Monthly:
                BuildMonthlySpinnerItems();
                break;
        }
    }

    private void BuildDailySpinnerItems()
    {
        var currentWeekMonday = GetMonday(DateTime.Today);
        var firstDay = currentWeekMonday.AddDays(_spinnerPageOffset * 7);

        DaysOfWeek = new ObservableCollection<DayOfWeekVM>(
            Enumerable.Range(0, 7).Select(offset =>
            {
                var day = firstDay.AddDays(offset);
                return new DayOfWeekVM
                {
                    Date = day,
                    DayName = day.ToString("ddd", CultureInfo.InvariantCulture),
                    DayNumber = day.Day.ToString(CultureInfo.InvariantCulture),
                    IsSelected = false
                };
            }));
    }

    private void BuildWeeklySpinnerItems()
    {
        var today = DateTime.Today;
        var baseMonday = GetWeeklyWindowStart(GetMonday(today));
        var firstMonday = baseMonday.AddDays(_spinnerPageOffset * 28);

        DaysOfWeek = new ObservableCollection<DayOfWeekVM>(
            Enumerable.Range(0, 4).Select(offset =>
            {
                var weekMonday = firstMonday.AddDays(offset * 7);
                var weekNumber = ISOWeek.GetWeekOfYear(weekMonday);
                return new DayOfWeekVM
                {
                    Date = weekMonday,
                    DayName = "Week",
                    DayNumber = weekNumber.ToString(CultureInfo.InvariantCulture),
                    IsSelected = false
                };
            }));
    }

    private void BuildMonthlySpinnerItems()
    {
        var today = DateTime.Today;
        var groupStartMonth = (today.Month - 1) / 4 * 4 + 1;
        var baseDate = new DateTime(today.Year, groupStartMonth, 1);
        var firstMonth = baseDate.AddMonths(_spinnerPageOffset * 4);

        DaysOfWeek = new ObservableCollection<DayOfWeekVM>(
            Enumerable.Range(0, 4).Select(offset =>
            {
                var monthDate = firstMonth.AddMonths(offset);
                return new DayOfWeekVM
                {
                    Date = monthDate,
                    DayName = monthDate.Year.ToString(CultureInfo.InvariantCulture),
                    DayNumber = monthDate.ToString("MMM", CultureInfo.InvariantCulture),
                    IsSelected = false
                };
            }));
    }

    private void SelectSpinnerItemForDate(DateTime referenceDate)
    {
        var match = FindSpinnerItemForDate(referenceDate);

        SelectDay(match ?? Enumerable.FirstOrDefault<DayOfWeekVM>(DaysOfWeek) ?? new DayOfWeekVM(), publishSelection: true);
    }

    private DayOfWeekVM? FindSpinnerItemForDate(DateTime referenceDate)
    {
        return _selectedMainContentViewMode switch
        {
            MainContentViewMode.Daily =>
                Enumerable.FirstOrDefault<DayOfWeekVM>(DaysOfWeek, day => day.Date.Date == referenceDate.Date),

            MainContentViewMode.Weekly =>
                Enumerable.FirstOrDefault<DayOfWeekVM>(DaysOfWeek, day =>
                    referenceDate.Date >= day.Date.Date && referenceDate.Date < day.Date.AddDays(7).Date),

            MainContentViewMode.Monthly =>
                Enumerable.FirstOrDefault<DayOfWeekVM>(DaysOfWeek, day =>
                    day.Date.Year == referenceDate.Year && day.Date.Month == referenceDate.Month),

            _ => null
        };
    }

    private void SyncVisibleSelectionAfterNavigation()
    {
        var selectedDate = SelectedDay.Date;
        if (selectedDate == default)
        {
            ClearVisibleSelection();
            return;
        }

        var match = FindSpinnerItemForDate(selectedDate);
        if (match is not null)
        {
            SelectDay(match, publishSelection: false);
            return;
        }

        ClearVisibleSelection();
    }

    private void ClearVisibleSelection()
    {
        foreach (var item in DaysOfWeek)
            item.IsSelected = false;

        IsAtCurrentPeriod = false;
        PublishSpinnerPeriodState();
    }

    private void SelectFirstSpinnerItem(bool publishSelection = true)
    {
        SelectDay(Enumerable.FirstOrDefault<DayOfWeekVM>(DaysOfWeek) ?? new DayOfWeekVM(), publishSelection);
    }

    private void SelectDay(DayOfWeekVM day, bool publishSelection)
    {
        _suppressSelectionPublishing = true;
        try
        {
            SelectedDay = day;
        }
        finally
        {
            _suppressSelectionPublishing = false;
        }

        foreach (var item in DaysOfWeek)
            item.IsSelected = ReferenceEquals(item, day);

        IsAtCurrentPeriod = EvaluateIsAtCurrentPeriod(day.Date);

        if (publishSelection)
            PublishSelectedRange(day.Date);

        PublishSpinnerPeriodState();
    }

    private void MoveToCurrentPeriodCore()
    {
        if (_selectedMainContentViewMode is MainContentViewMode.AllTime or MainContentViewMode.AllocationPeriod)
            return;

        BuildSpinnerForMode(DateTime.Today, publishSelection: true);
    }

    private void NavigateSpinnerBackCore()
    {
        _spinnerPageOffset--;
        CanNavigateForward = true;
        BuildSpinnerItems();
        SyncVisibleSelectionAfterNavigation();
    }

    private void NavigateSpinnerForwardCore()
    {
        if (_spinnerPageOffset >= 0 && !AllowFuturePeriodNavigation)
            return;

        _spinnerPageOffset++;
        CanNavigateForward = AllowFuturePeriodNavigation || _spinnerPageOffset < 0;
        BuildSpinnerItems();
        SyncVisibleSelectionAfterNavigation();
    }

    private async Task RunSpinnerInteractionWithFeedbackAsync(string message, Func<Task> operation, Window? owner)
    {
        if (_dialogService is null || _uiSettleAwaiter is null)
        {
            await operation();
            return;
        }

        await _spinnerInteractionGate.WaitAsync();
        try
        {
            await _dialogService.ShowToastWhileAsync(
                message,
                async () =>
                {
                    await operation();
                    await _uiSettleAwaiter.WaitForUiReadyAsync(owner);
                },
                owner);
        }
        finally
        {
            _spinnerInteractionGate.Release();
        }
    }

    private bool EvaluateIsAtCurrentPeriod(DateTime selectedDate)
    {
        var today = DateTime.Today;
        var date = selectedDate.Date;

        return _selectedMainContentViewMode switch
        {
            MainContentViewMode.Daily => date == today,
            MainContentViewMode.Weekly => today >= date && today < date.AddDays(7),
            MainContentViewMode.Monthly => date.Year == today.Year && date.Month == today.Month,
            _ => false
        };
    }

    private bool IsFuturePeriod(DateTime selectedDate)
    {
        var today = DateTime.Today;
        var date = selectedDate.Date;

        return _selectedMainContentViewMode switch
        {
            MainContentViewMode.Daily => date > today,
            MainContentViewMode.Weekly => date > GetMonday(today),
            MainContentViewMode.Monthly => date.Year > today.Year || date.Year == today.Year && date.Month > today.Month,
            _ => false
        };
    }

    partial void OnAllowFuturePeriodNavigationChanged(bool value)
    {
        CanNavigateForward = value || _spinnerPageOffset < 0;
    }

    private void PublishSpinnerPeriodState()
    {
        Messenger.Send(new SpinnerPeriodStateChangedMessage(new SpinnerPeriodState(
            IsAtCurrentPeriod,
            IsSpinnerVisible,
            MoveToCurrentLabel)));
    }

    private static DateTime GetMonday(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek;
        return date.Date.AddDays(mondayOffset);
    }

    internal static int ComputeWeeklyPageOffset(DateTime today, DateTime referenceDate)
    {
        var todayMonday = GetMonday(today);
        var referenceMonday = GetMonday(referenceDate);

        return GetWeeklyWindowIndex(referenceMonday) - GetWeeklyWindowIndex(todayMonday);
    }

    private static DateTime GetWeeklyWindowStart(DateTime mondayDate)
    {
        return DateTime.MinValue.Date.AddDays(GetWeeklyWindowIndex(mondayDate) * 28);
    }

    private static DateTime GetMonthlyWindowStart(DateTime date)
    {
        var groupStartMonth = (date.Month - 1) / 4 * 4 + 1;
        return new DateTime(date.Year, groupStartMonth, 1);
    }

    private static int GetWeeklyWindowIndex(DateTime mondayDate)
    {
        var daysSinceEpochMonday = (mondayDate.Date - DateTime.MinValue.Date).Days;
        return FloorDivide(daysSinceEpochMonday, 28);
    }

    private static int FloorDivide(int dividend, int divisor)
    {
        var quotient = dividend / divisor;
        var remainder = dividend % divisor;

        if (remainder != 0 && dividend < 0)
            quotient--;

        return quotient;
    }
}
