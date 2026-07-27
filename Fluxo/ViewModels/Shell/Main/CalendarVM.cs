using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Interfaces.Services;

using Fluxo.DataModels.Shell.Main.Calendar;
namespace Fluxo.ViewModels.Shell.Main;

public sealed partial class CalendarVM : ObservableObject, IDisposable
{
    private static readonly TimeSpan MonthNavigationStepDelay = TimeSpan.FromMilliseconds(45);
    private const int FrameWeekCount = 6;
    private const int BufferWeekCount = 2;
    private const int BufferedWeekCount = FrameWeekCount + BufferWeekCount * 2;

    private readonly ICalendarService _calendarService;
    private readonly Func<TimeSpan, Task> _navigationDelayAsync;
    private readonly DateOnly _currentDate;
    private CancellationTokenSource? _loadCts;
    private int _loadRequestVersion;
    private DateOnly _firstFrameWeekStart;
    private DateOnly _visibleMonth;

    [ObservableProperty] private DateOnly _selectedDate;
    [ObservableProperty] private string _visibleMonthLabel = string.Empty;
    [ObservableProperty] private string _totalSpentText = "0";
    [ObservableProperty] private string _totalEarnedText = "0";
    [ObservableProperty] private string _goalsDueText = "0";
    [ObservableProperty] private string _paymentDueText = "0";
    [ObservableProperty] private IReadOnlyList<CalendarExpenseListItem> _expenses = [];
    [ObservableProperty] private IReadOnlyList<CalendarIncomeListItem> _incomes = [];
    [ObservableProperty] private IReadOnlyList<CalendarGoalDeadlineListItem> _goalDeadlines = [];
    [ObservableProperty] private IReadOnlyList<CalendarRecurringTransactionListItem> _recurringTransactions = [];
    [ObservableProperty] private bool _isLoading;

    public CalendarVM(ICalendarService calendarService)
        : this(calendarService, DateTime.Today)
    {
    }

    internal CalendarVM(
        ICalendarService calendarService,
        DateTime currentDate,
        Func<TimeSpan, Task>? navigationDelayAsync = null)
    {
        _calendarService = calendarService;
        _navigationDelayAsync = navigationDelayAsync ?? Task.Delay;
        _currentDate = DateOnly.FromDateTime(currentDate.Date);
        SelectedDate = _currentDate;
        _visibleMonth = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);
        _firstFrameWeekStart = StartOfWeek(_visibleMonth);
        RebuildVisibleWeeks();
    }

    public ObservableCollection<CalendarWeekRow> BufferedWeeks { get; } = [];

    public IReadOnlyList<CalendarWeekRow> FrameWeeks => BufferedWeeks
        .Skip(BufferWeekCount)
        .Take(FrameWeekCount)
        .ToArray();

    public bool HasNoExpenses => Expenses.Count == 0;
    public bool HasNoIncomes => Incomes.Count == 0;
    public bool HasNoGoalDeadlines => GoalDeadlines.Count == 0;
    public bool HasNoRecurringTransactions => RecurringTransactions.Count == 0;
    public bool IsAtCurrentDay => SelectedDate == _currentDate;

    [RelayCommand]
    private async Task SelectDate(CalendarDayItem day)
    {
        await SelectDateAsync(day.Date);
    }

    [RelayCommand]
    private void ShiftCalendarFrameRows(int delta)
    {
        if (delta == 0)
            return;

        _firstFrameWeekStart = _firstFrameWeekStart.AddDays(delta > 0 ? 7 : -7);
        RebuildVisibleWeeks();
    }

    [RelayCommand]
    private async Task NavigateToPreviousMonth()
    {
        await ScrollToVisibleMonthAsync(_visibleMonth.AddMonths(-1));
    }

    [RelayCommand]
    private async Task NavigateToNextMonth()
    {
        await ScrollToVisibleMonthAsync(_visibleMonth.AddMonths(1));
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return SelectDateAsync(SelectedDate, cancellationToken);
    }

    public async Task SelectDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        SelectedDate = date;
        EnsureDateVisible(date);
        _visibleMonth = GetMonthStart(date);
        RebuildVisibleWeeks(updateVisibleMonthFromFrame: false);

        _loadCts?.Cancel();
        var loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts = loadCts;
        var requestVersion = ++_loadRequestVersion;
        var token = loadCts.Token;

        IsLoading = true;
        try
        {
            var dto = await _calendarService.GetCalendarDayAsync(date, token);
            if (IsCurrentLoad(requestVersion, loadCts) && !token.IsCancellationRequested)
                Apply(dto);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (IsCurrentLoad(requestVersion, loadCts))
            {
                IsLoading = false;
                _loadCts = null;
            }

            loadCts.Dispose();
        }
    }

    public Task SelectRelativeDateAsync(int dayOffset, CancellationToken cancellationToken = default)
    {
        return SelectDateAsync(SelectedDate.AddDays(dayOffset), cancellationToken);
    }

    public Task SelectCurrentDateAsync(CancellationToken cancellationToken = default)
    {
        if (IsAtCurrentDay)
            return Task.CompletedTask;

        _visibleMonth = GetMonthStart(_currentDate);
        _firstFrameWeekStart = StartOfWeek(_visibleMonth);
        return SelectDateAsync(_currentDate, cancellationToken);
    }

    partial void OnSelectedDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(IsAtCurrentDay));
    }

    private bool IsCurrentLoad(int requestVersion, CancellationTokenSource loadCts)
    {
        return requestVersion == _loadRequestVersion && ReferenceEquals(_loadCts, loadCts);
    }

    private void Apply(CalendarDto dto)
    {
        TotalSpentText = FormatMoney(dto.TotalSpent);
        TotalEarnedText = FormatMoney(dto.TotalEarned);
        GoalsDueText = dto.GoalsDue.ToString(CultureInfo.InvariantCulture);
        PaymentDueText = dto.PaymentsDue.ToString(CultureInfo.InvariantCulture);
        Expenses = dto.Expenses.Select(item => new CalendarExpenseListItem(item.Name, item.Amount, item.AccountName, item.TagName)).ToArray();
        Incomes = dto.Incomes.Select(item => new CalendarIncomeListItem(item.Name, item.Amount, item.AccountName)).ToArray();
        GoalDeadlines = dto.GoalDeadlines.Select(item => new CalendarGoalDeadlineListItem(item.Name, item.CurrentAmount, item.TargetAmount)).ToArray();
        RecurringTransactions = dto.RecurringTransactions.Select(item => new CalendarRecurringTransactionListItem(item.Name, item.Amount, item.Type.ToString(), item.SourceName)).ToArray();
        OnPropertyChanged(nameof(HasNoExpenses));
        OnPropertyChanged(nameof(HasNoIncomes));
        OnPropertyChanged(nameof(HasNoGoalDeadlines));
        OnPropertyChanged(nameof(HasNoRecurringTransactions));
    }

    private void RebuildVisibleWeeks(bool updateVisibleMonthFromFrame = true)
    {
        if (updateVisibleMonthFromFrame)
            UpdateVisibleMonthFromFrame();

        BufferedWeeks.Clear();
        var firstBufferedWeekStart = _firstFrameWeekStart.AddDays(-BufferWeekCount * 7);
        for (var weekIndex = 0; weekIndex < BufferedWeekCount; weekIndex++)
        {
            var weekStart = firstBufferedWeekStart.AddDays(weekIndex * 7);
            var days = Enumerable.Range(0, 7)
                .Select(offset =>
                {
                    var date = weekStart.AddDays(offset);
                    return new CalendarDayItem(
                        date,
                        date.Day.ToString(CultureInfo.InvariantCulture),
                        date == SelectedDate,
                        date == _currentDate,
                        date.Month != _visibleMonth.Month || date.Year != _visibleMonth.Year);
                })
                .ToArray();
            BufferedWeeks.Add(new CalendarWeekRow(days));
        }

        OnPropertyChanged(nameof(FrameWeeks));
        VisibleMonthLabel = _visibleMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    private async Task ScrollToVisibleMonthAsync(DateOnly month)
    {
        var targetMonth = GetMonthStart(month);
        var targetFirstFrameWeekStart = StartOfWeek(targetMonth);

        while (_firstFrameWeekStart != targetFirstFrameWeekStart)
        {
            var direction = targetFirstFrameWeekStart.CompareTo(_firstFrameWeekStart) > 0 ? 1 : -1;
            ShiftCalendarFrameRows(direction);

            if (_firstFrameWeekStart != targetFirstFrameWeekStart)
                await _navigationDelayAsync(MonthNavigationStepDelay);
        }
    }

    private void EnsureDateVisible(DateOnly date)
    {
        while (date < _firstFrameWeekStart)
            _firstFrameWeekStart = _firstFrameWeekStart.AddDays(-7);

        while (date >= _firstFrameWeekStart.AddDays(FrameWeekCount * 7))
            _firstFrameWeekStart = _firstFrameWeekStart.AddDays(7);
    }

    private void UpdateVisibleMonthFromFrame()
    {
        _visibleMonth = ResolveDominantVisibleMonth(_firstFrameWeekStart, _visibleMonth);
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = (int)date.DayOfWeek;
        return date.AddDays(-offset);
    }

    private static DateOnly GetMonthStart(DateOnly date)
    {
        return new DateOnly(date.Year, date.Month, 1);
    }

    private static DateOnly ResolveDominantVisibleMonth(DateOnly firstVisibleWeekStart, DateOnly currentVisibleMonth)
    {
        var currentMonth = new DateOnly(currentVisibleMonth.Year, currentVisibleMonth.Month, 1);
        var visibleMonthCounts = Enumerable.Range(0, FrameWeekCount * 7)
            .Select(offset => firstVisibleWeekStart.AddDays(offset))
            .GroupBy(date => new DateOnly(date.Year, date.Month, 1))
            .Select(group => new
            {
                Month = group.Key,
                Count = group.Count()
            })
            .ToArray();
        var highestVisibleDateCount = visibleMonthCounts.Max(group => group.Count);
        var dominantMonths = visibleMonthCounts
            .Where(group => group.Count == highestVisibleDateCount)
            .Select(group => group.Month)
            .Order()
            .ToArray();

        return dominantMonths.Contains(currentMonth)
            ? currentMonth
            : dominantMonths[0];
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts = null;
    }
}
