using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;

using Fluxo.DataModels.Shell.Main.Analytics;
namespace Fluxo.ViewModels.Shell.Main;

public sealed partial class AnalyticsVM(
    IAnalyticsService analyticsService,
    IDialogService? dialogService = null,
    IUiSettleAwaiter? uiSettleAwaiter = null) : ObservableObject, IDisposable
{
    private const int MaxRangeDays = 31;
    private const int VerticalTrendLabelThresholdDays = 14;

    private readonly IAnalyticsService _analyticsService = analyticsService;
    private readonly IDialogService? _dialogService = dialogService;
    private readonly IUiSettleAwaiter? _uiSettleAwaiter = uiSettleAwaiter;
    private readonly SemaphoreSlim _refreshFeedbackGate = new(1, 1);
    private CancellationTokenSource? _refreshDebounceCts;
    private bool _isApplyingDateBounds;
    private IReadOnlyList<AnalyticsTrendPoint> _expenseTrendPoints = [];
    private IReadOnlyList<AnalyticsTrendPoint> _incomeTrendPoints = [];


    [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private AnalyticsTrendMode _selectedTrendMode = AnalyticsTrendMode.Both;

    [ObservableProperty] private string _totalIncomeText = "0";
    [ObservableProperty] private string _totalExpenseText = "0";
    [ObservableProperty] private string _netValueText = "0";

    [ObservableProperty] private IReadOnlyList<AnalyticsTrendBarItem> _trendBarItems = [];
    [ObservableProperty] private IReadOnlyList<string> _trendGridLineLabels = [];
    [ObservableProperty] private bool _hasTrendData;

    [ObservableProperty] private double _needsRatio;
    [ObservableProperty] private double _wantsRatio;
    [ObservableProperty] private double _investRatio;
    [ObservableProperty] private double _wantsArcRotationDegrees;
    [ObservableProperty] private double _investArcRotationDegrees;
    [ObservableProperty] private int _needsPercent;
    [ObservableProperty] private int _wantsPercent;
    [ObservableProperty] private int _investPercent;
    [ObservableProperty] private bool _hasRatioData;
    [ObservableProperty] private IReadOnlyList<AnalyticsTopTagCardItem> _topSpendingTagItems = [];
    [ObservableProperty] private bool _hasTagData;

    [ObservableProperty] private IReadOnlyList<AnalyticsGoalCardItem> _goalsCreatedInPeriod = [];
    [ObservableProperty] private bool _hasGoalsData;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isTrendLabelVertical;
    [ObservableProperty] private bool _hideTrendValueLabels;
    [ObservableProperty] private string _dateRangeWarningMessage = string.Empty;

    public IReadOnlyList<AnalyticsTrendMode> TrendModes { get; } =
        Enum.GetValues<AnalyticsTrendMode>();

    public async Task LoadAsync()
    {
        await RefreshWithFeedbackAsync(CancellationToken.None, showToast: true);
    }

    public Task RefreshForOpenAsync(bool showToast, CancellationToken cancellationToken = default)
    {
        if (!showToast)
            CancelPendingRefreshDebounce();

        return RefreshWithFeedbackAsync(cancellationToken, showToast);
    }

    public void ApplyExternalDateRange(DateTime startDate, DateTime endDate, bool refresh)
    {
        _isApplyingDateBounds = true;
        try
        {
            StartDate = startDate.Date;
            EndDate = endDate.Date;
        }
        finally
        {
            _isApplyingDateBounds = false;
        }

        ApplyDateRangeRulesAndRefresh(queueRefresh: refresh);
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (_isApplyingDateBounds)
            return;

        ApplyDateRangeRulesAndRefresh();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (_isApplyingDateBounds)
            return;

        ApplyDateRangeRulesAndRefresh();
    }

    partial void OnSelectedTrendModeChanged(AnalyticsTrendMode value)
    {
        OnPropertyChanged(nameof(IsExpenseTrendModeSelected));
        OnPropertyChanged(nameof(IsIncomeTrendModeSelected));
        OnPropertyChanged(nameof(IsBothTrendModeSelected));
        ApplyTrendMode();
    }

    public bool IsExpenseTrendModeSelected => SelectedTrendMode == AnalyticsTrendMode.Expenses;

    public bool IsIncomeTrendModeSelected => SelectedTrendMode == AnalyticsTrendMode.Incomes;

    public bool IsBothTrendModeSelected => SelectedTrendMode == AnalyticsTrendMode.Both;

    public bool HasDateRangeWarning => !string.IsNullOrWhiteSpace(DateRangeWarningMessage);

    partial void OnDateRangeWarningMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasDateRangeWarning));
    }

    [RelayCommand]
    private void SetTrendMode(AnalyticsTrendMode trendMode)
    {
        SelectedTrendMode = trendMode;
    }

    private void QueueRefresh()
    {
        CancelPendingRefreshDebounce();

        var cts = new CancellationTokenSource();
        _refreshDebounceCts = cts;

        _ = RefreshDebouncedAsync(cts.Token);
    }

    private async Task RefreshDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(220, cancellationToken);
            await RefreshWithFeedbackAsync(cancellationToken, showToast: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshWithFeedbackAsync(CancellationToken cancellationToken, bool showToast)
    {
        if (!showToast)
        {
            await RefreshAsync(cancellationToken);
            if (_uiSettleAwaiter is not null)
                await _uiSettleAwaiter.WaitForUiReadyAsync(cancellationToken: cancellationToken);

            return;
        }

        if (_dialogService is null || _uiSettleAwaiter is null)
        {
            await RefreshAsync(cancellationToken);
            return;
        }

        await _refreshFeedbackGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _dialogService.ShowToastWhileAsync(
                BuildAnalyticsLoadingMessage(),
                async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RefreshAsync(cancellationToken);
                    await _uiSettleAwaiter.WaitForUiReadyAsync(cancellationToken: cancellationToken);
                });
        }
        finally
        {
            _refreshFeedbackGate.Release();
        }
    }

    private string BuildAnalyticsLoadingMessage()
    {
        var from = StartDate.Date;
        var to = EndDate.Date;
        const string DateFormat = "dd MMM yyyy";

        return from == to
            ? $"Loading analytics for {from.ToString(DateFormat, CultureInfo.InvariantCulture)}"
            : $"Loading analytics from {from.ToString(DateFormat, CultureInfo.InvariantCulture)} to {to.ToString(DateFormat, CultureInfo.InvariantCulture)}";
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;

        try
        {
            var from = DateOnly.FromDateTime(StartDate.Date);
            var to = DateOnly.FromDateTime(EndDate.Date);
            var dto = await _analyticsService.GetAnalyticsAsync(from, to, cancellationToken);
            Apply(dto);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(AnalyticsDto dto)
    {
        TotalIncomeText = FormatMoney(dto.TotalIncome);
        TotalExpenseText = FormatMoney(dto.TotalExpense);
        NetValueText = FormatMoney(dto.TotalIncome - dto.TotalExpense);

        var labelFormat = dto.TimeSeries.Count <= 7 ? "ddd" : "dd MMM";
        _expenseTrendPoints = dto.TimeSeries
            .Select(point => new AnalyticsTrendPoint(
                point.Period.ToString(labelFormat, CultureInfo.InvariantCulture),
                point.Expense))
            .ToArray();
        _incomeTrendPoints = dto.TimeSeries
            .Select(point => new AnalyticsTrendPoint(
                point.Period.ToString(labelFormat, CultureInfo.InvariantCulture),
                point.Income))
            .ToArray();

        ApplyTrendMode();

        var categoryMap = dto.CategoryRatio.ToDictionary(item => item.Category, item => item.Total);
        var needs = categoryMap.GetValueOrDefault(ExpenseCategory.Needs, 0m);
        var wants = categoryMap.GetValueOrDefault(ExpenseCategory.Wants, 0m);
        var invest = categoryMap.GetValueOrDefault(ExpenseCategory.Savings, 0m);
        var ratioTotal = needs + wants + invest;
        HasRatioData = ratioTotal > 0m;

        if (ratioTotal > 0m)
        {
            NeedsRatio = (double)(needs / ratioTotal);
            WantsRatio = (double)(wants / ratioTotal);
            InvestRatio = (double)(invest / ratioTotal);
        }
        else
        {
            NeedsRatio = 0d;
            WantsRatio = 0d;
            InvestRatio = 0d;
        }

        NeedsPercent = (int)Math.Round(NeedsRatio * 100d, MidpointRounding.AwayFromZero);
        WantsPercent = (int)Math.Round(WantsRatio * 100d, MidpointRounding.AwayFromZero);
        InvestPercent = Math.Max(0, 100 - NeedsPercent - WantsPercent);
        WantsArcRotationDegrees = NeedsRatio * 360d;
        InvestArcRotationDegrees = (NeedsRatio + WantsRatio) * 360d;

        var topTags = dto.TopSpendingTags
            .OrderByDescending(item => item.Total)
            .Take(8)
            .ToArray();
        var maxTopTagTotal = topTags.Length == 0 ? 0m : topTags.Max(item => item.Total);

        TopSpendingTagItems = topTags
            .Select(item => new AnalyticsTopTagCardItem(
                item.TagName,
                item.HexCode,
                item.Total,
                maxTopTagTotal <= 0m ? 0d : Math.Clamp((double)(item.Total / maxTopTagTotal * 100m), 0d, 100d)))
            .ToArray();
        HasTagData = Enumerable.Any<AnalyticsTopTagCardItem>(TopSpendingTagItems, item => item.Total > 0m);

        GoalsCreatedInPeriod = dto.GoalsCreatedInPeriod
            .Select(goal => new AnalyticsGoalCardItem(
                goal.Name,
                goal.CurrentAmount,
                goal.TargetAmount,
                goal.CreatedOn,
                goal.SavingEndDate))
            .ToArray();
        HasGoalsData = GoalsCreatedInPeriod.Count > 0;
    }

    private void ApplyTrendMode()
    {
        var trendBarSeeds = SelectedTrendMode switch
        {
            AnalyticsTrendMode.Expenses => _expenseTrendPoints
                .Select(point => new TrendBarSeed(
                    point.Label,
                    PrimaryValue: point.Value,
                    IsPrimaryExpenseSeries: true,
                    IsPrimaryIncomeSeries: false,
                    SecondaryValue: 0m,
                    HasSecondaryValue: false,
                    IsSecondaryExpenseSeries: false,
                    IsSecondaryIncomeSeries: false))
                .ToArray(),
            AnalyticsTrendMode.Incomes => _incomeTrendPoints
                .Select(point => new TrendBarSeed(
                    point.Label,
                    PrimaryValue: point.Value,
                    IsPrimaryExpenseSeries: false,
                    IsPrimaryIncomeSeries: true,
                    SecondaryValue: 0m,
                    HasSecondaryValue: false,
                    IsSecondaryExpenseSeries: false,
                    IsSecondaryIncomeSeries: false))
                .ToArray(),
            _ => BuildBothTrendBarSeeds()
        };

        HasTrendData = trendBarSeeds.Any(point =>
            point.PrimaryValue > 0m ||
            point.HasSecondaryValue && point.SecondaryValue > 0m);
        var maxValue = trendBarSeeds
            .Select(point => point.HasSecondaryValue
                ? Math.Max(point.PrimaryValue, point.SecondaryValue)
                : point.PrimaryValue)
            .DefaultIfEmpty(0m)
            .Max();
        var scaleMaximum = CalculateTrendScaleMaximum(maxValue);
        TrendGridLineLabels = scaleMaximum <= 0m
            ? []
            : Enumerable.Range(1, 4)
                .Reverse()
                .Select(section => FormatMoney(scaleMaximum * section / 5m))
                .ToArray();
        var highlightedIndex = Array.FindLastIndex(
            trendBarSeeds,
            point =>
                (point.PrimaryValue > 0m || point.HasSecondaryValue && point.SecondaryValue > 0m) &&
                !string.IsNullOrWhiteSpace(point.Label));

        if (highlightedIndex < 0)
        {
            highlightedIndex = Array.FindLastIndex(
                trendBarSeeds,
                point => point.PrimaryValue > 0m || point.HasSecondaryValue && point.SecondaryValue > 0m);
        }

        TrendBarItems = trendBarSeeds
            .Select((point, index) => new AnalyticsTrendBarItem(
                point.Label,
                point.PrimaryValue,
                CalculateBarHeightRatio(point.PrimaryValue, scaleMaximum),
                point.SecondaryValue,
                point.HasSecondaryValue
                    ? CalculateBarHeightRatio(point.SecondaryValue, scaleMaximum)
                    : 0d,
                point.HasSecondaryValue,
                IsHighlighted: index == highlightedIndex,
                HideValueText: HideTrendValueLabels,
                RotateLabelVertical: IsTrendLabelVertical,
                IsExpenseMode: point.IsPrimaryExpenseSeries,
                IsIncomeMode: point.IsPrimaryIncomeSeries,
                IsSecondaryExpenseMode: point.IsSecondaryExpenseSeries,
                IsSecondaryIncomeMode: point.IsSecondaryIncomeSeries))
            .ToArray();
    }

    private void ApplyDateRangeRulesAndRefresh(bool queueRefresh = true)
    {
        _isApplyingDateBounds = true;
        try
        {
            var start = StartDate.Date;
            var end = EndDate.Date;

            if (end < start)
                end = start;

            if ((end - start).TotalDays > MaxRangeDays)
            {
                end = start.AddDays(MaxRangeDays);
                DateRangeWarningMessage =
                    "The selected range cannot exceed 31 days. End date was adjusted to 31 days after start date.";
            }
            else
            {
                DateRangeWarningMessage = string.Empty;
            }

            if (StartDate.Date != start)
                StartDate = start;
            if (EndDate.Date != end)
                EndDate = end;
        }
        finally
        {
            _isApplyingDateBounds = false;
        }

        UpdateTrendPresentation();
        ApplyTrendMode();
        if (queueRefresh)
            QueueRefresh();
    }

    private void UpdateTrendPresentation()
    {
        var isWideRange = (EndDate.Date - StartDate.Date).TotalDays > VerticalTrendLabelThresholdDays;
        IsTrendLabelVertical = isWideRange;
        HideTrendValueLabels = isWideRange;
    }

    private TrendBarSeed[] BuildBothTrendBarSeeds()
    {
        if (_expenseTrendPoints.Count == 0 || _incomeTrendPoints.Count == 0)
            return [];

        var count = Math.Min(_expenseTrendPoints.Count, _incomeTrendPoints.Count);
        var items = new TrendBarSeed[count];

        for (var index = 0; index < count; index++)
        {
            var expensePoint = _expenseTrendPoints[index];
            var incomePoint = _incomeTrendPoints[index];

            items[index] = new TrendBarSeed(
                expensePoint.Label,
                PrimaryValue: expensePoint.Value,
                IsPrimaryExpenseSeries: true,
                IsPrimaryIncomeSeries: false,
                SecondaryValue: incomePoint.Value,
                HasSecondaryValue: true,
                IsSecondaryExpenseSeries: false,
                IsSecondaryIncomeSeries: true);
        }

        return items;
    }

    private static double CalculateBarHeightRatio(decimal value, decimal maxValue)
    {
        if (value <= 0m || maxValue <= 0m)
            return 0.02d;

        const double minRatio = 0.12d;
        var normalized = (double)(value / maxValue);
        return Math.Clamp(Math.Max(minRatio, normalized), 0d, 1d);
    }

    private static decimal CalculateTrendScaleMaximum(decimal maxValue)
    {
        if (maxValue <= 0m)
            return 0m;

        var magnitude = (decimal)Math.Pow(10d, Math.Floor(Math.Log10((double)maxValue)));
        var normalized = maxValue / magnitude;
        var rounded = normalized <= 1m
            ? 1m
            : normalized <= 2m
                ? 2m
                : normalized <= 5m
                    ? 5m
                    : 10m;

        return rounded * magnitude;
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void CancelPendingRefreshDebounce()
    {
        _refreshDebounceCts?.Cancel();
        _refreshDebounceCts?.Dispose();
        _refreshDebounceCts = null;
    }

    public void Dispose()
    {
        CancelPendingRefreshDebounce();
    }
}
