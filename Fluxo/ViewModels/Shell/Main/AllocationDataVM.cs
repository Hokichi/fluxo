using AutoMapper;
using Fluxo.Helpers.Transaction;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.ViewModels.Entities;
using CoreILogMemoryAction = Fluxo.Core.Interfaces.History.ILogMemoryAction;

namespace Fluxo.ViewModels.Shell.Main;

public partial class AllocationDataVM : ObservableRecipient,
    IRecipient<DashboardDataInvalidatedMessage>,
    IRecipient<RecordLogMemoryMessage>,
    IRecipient<LogMemoryActionAppliedMessage>
{
    private readonly IAppDataService _appData;
    private readonly IMapper _mapper;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    private List<TransactionVM> _allExpenseLogs = [];
    private List<TransactionVM> _allIncomeLogs = [];
    private List<AccountVM> _accounts = [];
    private BudgetAllocation _budgetAllocation = new();

    public AllocationDataVM(
        IAppDataService appData,
        IMapper mapper,
        IMessenger? messenger = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _appData = appData;
        _mapper = mapper;

        IsActive = true;
    }

    [ObservableProperty]
    private decimal _totalIncomeAmount;

    [ObservableProperty]
    private decimal _totalSpent;

    [ObservableProperty]
    private int _dailyAllowance;

    [ObservableProperty]
    private decimal _needsAvailable;

    [ObservableProperty]
    private decimal _wantsAvailable;

    [ObservableProperty]
    private decimal _investAvailable;

    [ObservableProperty]
    private decimal _needsSpent;

    [ObservableProperty]
    private decimal _wantsSpent;

    [ObservableProperty]
    private decimal _investSpent;

    [ObservableProperty]
    private decimal _needsRemaining;

    [ObservableProperty]
    private decimal _wantsRemaining;

    [ObservableProperty]
    private decimal _investRemaining;

    public bool IsNeedsOverflowing => NeedsRemaining < 0m;
    public bool IsWantsOverflowing => WantsRemaining < 0m;
    public bool IsInvestOverflowing => InvestRemaining < 0m;

    public decimal NeedsRemainingDisplay => Math.Abs(NeedsRemaining);
    public decimal WantsRemainingDisplay => Math.Abs(WantsRemaining);
    public decimal InvestRemainingDisplay => Math.Abs(InvestRemaining);

    public string NeedsRemainingLabel => IsNeedsOverflowing ? "overflowing" : "remaining";
    public string WantsRemainingLabel => IsWantsOverflowing ? "overflowing" : "remaining";
    public string InvestRemainingLabel => IsInvestOverflowing ? "overflowing" : "remaining";

    [ObservableProperty]
    private int _needsPercentage;

    [ObservableProperty]
    private int _wantsPercentage;

    [ObservableProperty]
    private int _investPercentage;

    [ObservableProperty]
    private decimal _needsThreshold = 0.5m;

    [ObservableProperty]
    private decimal _wantsThreshold = 0.3m;

    [ObservableProperty]
    private decimal _investThreshold = 0.2m;

    public int NeedsAllocationPercentage => ConvertThresholdToPercentage(NeedsThreshold);

    public int WantsAllocationPercentage => ConvertThresholdToPercentage(WantsThreshold);

    public int InvestAllocationPercentage => ConvertThresholdToPercentage(InvestThreshold);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _budgetAllocation = await LoadBudgetAllocationAsync(cancellationToken);
        NeedsThreshold = _budgetAllocation.NeedsThreshold / 100m;
        WantsThreshold = _budgetAllocation.WantsThreshold / 100m;
        InvestThreshold = _budgetAllocation.InvestThreshold / 100m;

        var transactions = _mapper.Map<IReadOnlyList<TransactionVM>>(
            await _appData.GetTransactionsAsync(cancellationToken));
        _allExpenseLogs = transactions
            .Where(transaction => transaction.Type == TransactionType.Expense && !transaction.IsForDeletion)
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ToList();
        _allIncomeLogs = transactions
            .Where(transaction => transaction.Type == TransactionType.Income && !transaction.IsForDeletion)
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ToList();
        _accounts = _mapper.Map<IReadOnlyList<AccountVM>>(
                await _appData.GetAccountsAsync(cancellationToken))
            .ToList();

        RefreshBudgetMetrics();
    }

    public void Receive(DashboardDataInvalidatedMessage message)
    {
        if (!message.Value.HasFlag(DashboardDataInvalidationScope.Budget))
            return;

        _ = ReloadFromServicesAsync();
    }

    public void Receive(RecordLogMemoryMessage message)
    {
        ApplyLogMemoryAction(message.Value, LogMemoryApplyDirection.Reapply);
    }

    public void Receive(LogMemoryActionAppliedMessage message)
    {
        var (action, direction) = message.Value;
        ApplyLogMemoryAction(action, direction);
    }

    partial void OnNeedsThresholdChanged(decimal value)
    {
        OnPropertyChanged(nameof(NeedsAllocationPercentage));
    }

    partial void OnWantsThresholdChanged(decimal value)
    {
        OnPropertyChanged(nameof(WantsAllocationPercentage));
    }

    partial void OnInvestThresholdChanged(decimal value)
    {
        OnPropertyChanged(nameof(InvestAllocationPercentage));
    }

    partial void OnNeedsRemainingChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsNeedsOverflowing));
        OnPropertyChanged(nameof(NeedsRemainingDisplay));
        OnPropertyChanged(nameof(NeedsRemainingLabel));
    }

    partial void OnWantsRemainingChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsWantsOverflowing));
        OnPropertyChanged(nameof(WantsRemainingDisplay));
        OnPropertyChanged(nameof(WantsRemainingLabel));
    }

    partial void OnInvestRemainingChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsInvestOverflowing));
        OnPropertyChanged(nameof(InvestRemainingDisplay));
        OnPropertyChanged(nameof(InvestRemainingLabel));
    }

    private async Task ReloadFromServicesAsync()
    {
        await _reloadGate.WaitAsync();

        try
        {
            await LoadAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task<BudgetAllocation> LoadBudgetAllocationAsync(CancellationToken cancellationToken)
    {
        return await _appData.GetBudgetAllocationAsync(cancellationToken);
    }

    private void RefreshBudgetMetrics()
    {
        TotalIncomeAmount = CalculateBudgetAvailableBase();
        var snapshot = BudgetAllocationCalculator.CalculateSnapshot(
            _budgetAllocation,
            CalculateSpentByCategory(BudgetAllocationCalculator.ResolveCurrentPeriod(
                _budgetAllocation.AllocationPeriod,
                DateTime.Today,
                _budgetAllocation.PeriodStart)),
            CalculateSpentByCategory(BudgetAllocationCalculator.ResolvePreviousPeriod(
                _budgetAllocation.AllocationPeriod,
                DateTime.Today,
                _budgetAllocation.PeriodStart)),
            DateTime.Today,
            TotalIncomeAmount);

        NeedsAvailable = snapshot.Needs.Available;
        WantsAvailable = snapshot.Wants.Available;
        InvestAvailable = snapshot.Invest.Available;

        NeedsSpent = snapshot.Needs.Spent;
        WantsSpent = snapshot.Wants.Spent;
        InvestSpent = snapshot.Invest.Spent;
        TotalSpent = NeedsSpent + WantsSpent + InvestSpent;

        NeedsRemaining = snapshot.Needs.Remaining;
        WantsRemaining = snapshot.Wants.Remaining;
        InvestRemaining = snapshot.Invest.Remaining;

        NeedsPercentage = snapshot.Needs.Percentage;
        WantsPercentage = snapshot.Wants.Percentage;
        InvestPercentage = snapshot.Invest.Percentage;
        DailyAllowance = (int)snapshot.DailyAllowance;
    }

    private IReadOnlyDictionary<ExpenseCategory, decimal> CalculateSpentByCategory(BudgetAllocationPeriod snapshotPeriod)
    {
        return BudgetEffectiveTransactionFilter.Select(_allExpenseLogs)
            .Where(log => log.OccurredOn.Date >= snapshotPeriod.Start && log.OccurredOn.Date <= snapshotPeriod.End)
            .Where(log => log.ExpenseCategory.HasValue)
            .GroupBy(log => log.ExpenseCategory!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));
    }

    private decimal CalculateBudgetAvailableBase()
    {
        if (_budgetAllocation.AllocationLimit > 0m)
            return _budgetAllocation.AllocationLimit;

        var balanceBackedSourceIds = _accounts
            .Where(IsBalanceBackedSource)
            .Select(source => source.Id)
            .ToHashSet();

        var balanceBackedExpenseAmount = BudgetEffectiveTransactionFilter.Select(_allExpenseLogs)
            .Where(log => log.Account is { } source && balanceBackedSourceIds.Contains(source.Id))
            .Sum(log => log.Amount);

        return _accounts.Sum(source => source.Balance) + balanceBackedExpenseAmount;
    }

    private void ApplyLogMemoryAction(CoreILogMemoryAction action, LogMemoryApplyDirection direction)
    {
        if (action is CompositeLogMemoryAction composite)
        {
            foreach (var child in composite.Actions)
                ApplyLogMemoryAction(child, direction);
            return;
        }

        if (action is AddTransactionMemoryAction or EditTransactionMemoryAction or DeleteTransactionMemoryAction)
            _ = ReloadFromServicesAsync();
    }
    private static bool IsBalanceBackedSource(AccountVM source)
    {
        return source.AccountType != AccountType.Credit;
    }

    private static int ConvertThresholdToPercentage(decimal threshold)
    {
        return (int)Math.Round(threshold * 100m, MidpointRounding.AwayFromZero);
    }
}
