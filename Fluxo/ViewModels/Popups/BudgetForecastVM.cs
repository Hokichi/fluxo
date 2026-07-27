using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;

using Fluxo.DataModels.Popups.BudgetForecastVM;
namespace Fluxo.ViewModels.Popups;

public sealed partial class BudgetForecastVM : ObservableObject
{
    private readonly IAppDataService _appData;
    private BudgetAllocation _allocation = new();
    private decimal _baseNeeds;
    private decimal _baseWants;
    private decimal _baseInvest;
    private int _allocationPeriodDays = 1;

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private decimal _net;
    [ObservableProperty] private decimal _needs;
    [ObservableProperty] private decimal _wants;
    [ObservableProperty] private decimal _invest;
    [ObservableProperty] private bool _isTestTransactionVisible;
    [ObservableProperty] private BudgetForecastAccountRowVM? _selectedPurchaseAccount;
    [ObservableProperty] private ExpenseCategory _selectedPurchaseCategory = ExpenseCategory.Needs;
    [ObservableProperty] private string _purchaseAmountText = string.Empty;
    [ObservableProperty] private string _purchaseMessage = string.Empty;
    [ObservableProperty] private string _purchaseMessageBrushKey = "Brush.Text.Muted";

    public BudgetForecastVM(IAppDataService appData)
    {
        _appData = appData;
    }

    public ObservableCollection<BudgetForecastAccountRowVM> Accounts { get; } = [];
    public ObservableCollection<BudgetForecastRecurringRowVM> RecurringTransactions { get; } = [];
    public bool IsPurchaseNeedsCategory
    {
        get => SelectedPurchaseCategory == ExpenseCategory.Needs;
        set { if (value) SelectedPurchaseCategory = ExpenseCategory.Needs; }
    }

    public bool IsPurchaseWantsCategory
    {
        get => SelectedPurchaseCategory == ExpenseCategory.Wants;
        set { if (value) SelectedPurchaseCategory = ExpenseCategory.Wants; }
    }

    public bool IsPurchaseInvestCategory
    {
        get => SelectedPurchaseCategory == ExpenseCategory.Savings;
        set { if (value) SelectedPurchaseCategory = ExpenseCategory.Savings; }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _allocation = await _appData.GetBudgetAllocationAsync(cancellationToken);
        var accounts = await _appData.GetAccountsAsync(cancellationToken);
        var recurringTransactions = await _appData.GetRecurringTransactionsAsync(cancellationToken);
        var expenseLogs = (await _appData.GetTransactionsAsync(cancellationToken))
            .Where(transaction => transaction.Type == TransactionType.Expense)
            .ToList();

        Accounts.Clear();
        foreach (var account in accounts.Where(account => account.IsEnabled && !account.IsForDeletion).OrderBy(account => account.Name))
        {
            Accounts.Add(new BudgetForecastAccountRowVM(account.Id, account.Name, GetAvailableBalance(account)));
        }

        RecurringTransactions.Clear();
        foreach (var row in recurringTransactions
                     .Where(transaction => transaction.IsEnabled)
                     .OrderBy(transaction => transaction.Name)
                     .Select(transaction => ToRecurringRow(transaction, accounts)))
        {
            row.PropertyChanged += OnRecurringRowChanged;
            RecurringTransactions.Add(row);
        }

        ApplyCurrentBudgetRemaining(expenseLogs, accounts);

        SelectedPurchaseAccount ??= Accounts.FirstOrDefault();

        Recalculate();
    }

    public void Recalculate()
    {
        var balances = Accounts.ToDictionary(account => account.Id, account => account.CurrentBalance);
        var needs = CalculateCategoryBudget(_baseNeeds, DayCount, _allocationPeriodDays);
        var wants = CalculateCategoryBudget(_baseWants, DayCount, _allocationPeriodDays);
        var invest = CalculateCategoryBudget(_baseInvest, DayCount, _allocationPeriodDays);

        foreach (var recurring in RecurringTransactions.Where(row => row.IsIncluded))
        {
            var occurrences = CountRecurringOccurrences(
                recurring.RecurringPeriod,
                recurring.RecurringTime,
                DateTime.Today,
                SelectedDate);
            if (occurrences <= 0)
                continue;

            var total = recurring.Amount * occurrences;
            ApplyProjection(
                balances,
                recurring.AccountId,
                recurring.Type,
                total,
                recurring.Category,
                ref needs,
                ref wants,
                ref invest);
        }

        foreach (var account in Accounts)
        {
            account.Balance = balances.TryGetValue(account.Id, out var balance)
                ? RoundMoney(balance)
                : account.CurrentBalance;
        }

        Needs = RoundMoney(needs);
        Wants = RoundMoney(wants);
        Invest = RoundMoney(invest);
        Net = RoundMoney(Accounts.Sum(account => account.Balance) - Accounts.Sum(account => account.CurrentBalance));
        RefreshPurchaseMessage();
    }

    public static int CountRecurringOccurrences(
        RecurringPeriod period,
        int recurringTime,
        DateTime today,
        DateTime targetDate)
    {
        var start = today.Date.AddDays(1);
        var end = targetDate.Date;
        if (end < start)
            return 0;

        return period switch
        {
            RecurringPeriod.None => 0,
            RecurringPeriod.Monthly => CountMonthlyOccurrences(recurringTime, start, end),
            RecurringPeriod.Weekly => CountDayIntervalOccurrences(recurringTime, start, end, 7),
            // ponytail: recurring data has no biweekly anchor; add one to the schema before making true alternating-week forecasts.
            RecurringPeriod.Biweekly => CountDayIntervalOccurrences(recurringTime, start, end, 14),
            _ => 0
        };
    }

    public static int CountAllocationPeriods(int dayCount, int allocationPeriodDays)
    {
        if (dayCount <= 0 || allocationPeriodDays <= 0)
            return 0;

        return (int)Math.Ceiling(dayCount / (decimal)allocationPeriodDays);
    }

    public static decimal CalculateCategoryBudget(decimal baseAllocation, int dayCount, int allocationPeriodDays)
    {
        return RoundMoney(baseAllocation * CountAllocationPeriods(dayCount, allocationPeriodDays));
    }

    public static BudgetForecastPurchaseResult BuildPurchaseResult(
        decimal purchaseAmount,
        decimal accountBalance,
        decimal categoryRemaining,
        string categoryName,
        string accountName)
    {
        if (purchaseAmount <= 0m)
            return new BudgetForecastPurchaseResult(string.Empty, "Brush.Text.Muted");

        if (purchaseAmount > accountBalance)
        {
            var shortage = RoundMoney(purchaseAmount - accountBalance);
            return new BudgetForecastPurchaseResult(
                $"Not affordable - {FormatMoney(shortage)} short from {accountName}.",
                "Brush.Danger");
        }

        if (purchaseAmount > categoryRemaining)
        {
            var overflow = RoundMoney(purchaseAmount - categoryRemaining);
            return new BudgetForecastPurchaseResult(
                $"Not recommended - {FormatMoney(overflow)} over {categoryName} budget. You might want to reconsider.",
                "Brush.Warning");
        }

        return new BudgetForecastPurchaseResult(
            $"Affordable - {FormatMoney(categoryRemaining - purchaseAmount)} left in {categoryName}, {FormatMoney(accountBalance - purchaseAmount)} left in {accountName}",
            "Brush.Success");
    }

    private int DayCount => Math.Max(0, (SelectedDate.Date - DateTime.Today).Days);

    partial void OnSelectedDateChanged(DateTime value)
    {
        Recalculate();
    }

    partial void OnPurchaseAmountTextChanged(string value)
    {
        RefreshPurchaseMessage();
    }

    partial void OnSelectedPurchaseAccountChanged(BudgetForecastAccountRowVM? value)
    {
        RefreshPurchaseMessage();
    }

    partial void OnSelectedPurchaseCategoryChanged(ExpenseCategory value)
    {
        OnPropertyChanged(nameof(IsPurchaseNeedsCategory));
        OnPropertyChanged(nameof(IsPurchaseWantsCategory));
        OnPropertyChanged(nameof(IsPurchaseInvestCategory));
        RefreshPurchaseMessage();
    }

    private void ApplyCurrentBudgetRemaining(IReadOnlyList<Transaction> expenseLogs, IReadOnlyList<Account> accounts)
    {
        var budgetEffectiveLogs = SelectBudgetEffectiveLogs(expenseLogs);
        var currentPeriod = BudgetAllocationCalculator.ResolveCurrentPeriod(
            _allocation.AllocationPeriod,
            DateTime.Today,
            _allocation.PeriodStart);
        var previousPeriod = BudgetAllocationCalculator.ResolvePreviousPeriod(
            _allocation.AllocationPeriod,
            DateTime.Today,
            _allocation.PeriodStart);

        var currentSpent = SumSpentByCategory(budgetEffectiveLogs, currentPeriod.Start, currentPeriod.End);
        var previousSpent = SumSpentByCategory(budgetEffectiveLogs, previousPeriod.Start, previousPeriod.End);
        var fallbackBudgetBase = accounts
            .Where(account => account.IsEnabled && !account.IsForDeletion)
            .Sum(GetAvailableBalance);
        var snapshot = BudgetAllocationCalculator.CalculateSnapshot(
            _allocation,
            currentSpent,
            previousSpent,
            DateTime.Today,
            fallbackBudgetBase);

        _baseNeeds = snapshot.Needs.BaseAllocation;
        _baseWants = snapshot.Wants.BaseAllocation;
        _baseInvest = snapshot.Invest.BaseAllocation;
        _allocationPeriodDays = Math.Max(1, snapshot.CurrentPeriod.DayCount);
    }

    private void ApplyProjection(
        IDictionary<int, decimal> balances,
        int accountId,
        RecurringTransactionType type,
        decimal amount,
        ExpenseCategory category,
        ref decimal needs,
        ref decimal wants,
        ref decimal invest)
    {
        if (!balances.ContainsKey(accountId))
            return;

        if (type == RecurringTransactionType.Income)
        {
            balances[accountId] += amount;
            return;
        }

        balances[accountId] -= amount;
        ApplyExpenseToCategory(amount, category, ref needs, ref wants, ref invest);
    }

    private static void ApplyExpenseToCategory(
        decimal amount,
        ExpenseCategory category,
        ref decimal needs,
        ref decimal wants,
        ref decimal invest)
    {
        switch (category)
        {
            case ExpenseCategory.Wants:
                wants -= amount;
                break;
            case ExpenseCategory.Savings:
                invest -= amount;
                break;
            default:
                needs -= amount;
                break;
        }
    }

    private void RefreshPurchaseMessage()
    {
        if (SelectedPurchaseAccount is null ||
            !TryParseMoney(PurchaseAmountText, out var amount) ||
            amount <= 0m)
        {
            PurchaseMessage = string.Empty;
            PurchaseMessageBrushKey = "Brush.Text.Muted";
            return;
        }

        var result = BuildPurchaseResult(
            amount,
            SelectedPurchaseAccount.Balance,
            GetCategoryRemaining(SelectedPurchaseCategory),
            SelectedPurchaseCategory switch
            {
                ExpenseCategory.Wants => "Wants",
                ExpenseCategory.Savings => "Invest",
                _ => "Needs"
            },
            SelectedPurchaseAccount.Name);
        PurchaseMessage = result.Message;
        PurchaseMessageBrushKey = result.BrushKey;
    }

    private decimal GetCategoryRemaining(ExpenseCategory category)
    {
        return category switch
        {
            ExpenseCategory.Wants => Wants,
            ExpenseCategory.Savings => Invest,
            _ => Needs
        };
    }

    private static BudgetForecastRecurringRowVM ToRecurringRow(
        RecurringTransaction transaction,
        IReadOnlyList<Account> accounts)
    {
        var accountName = accounts.FirstOrDefault(account => account.Id == transaction.SourceId)?.Name ?? "Account";
        return new BudgetForecastRecurringRowVM(
            transaction.Id,
            transaction.Name,
            transaction.Amount,
            transaction.SourceId,
            accountName,
            transaction.Type,
            transaction.Category ?? ExpenseCategory.Needs,
            transaction.RecurringPeriod,
            transaction.RecurringTime,
            isIncluded: true);
    }

    public static decimal GetAvailableBalance(Account account)
    {
        return account.AccountType == AccountType.Credit
            ? account.AccountLimit - account.SpentAmount
            : account.Balance;
    }

    private static IReadOnlyDictionary<ExpenseCategory, decimal> SumSpentByCategory(
        IEnumerable<Transaction> logs,
        DateTime start,
        DateTime end)
    {
        return logs
            .Where(log => log.OccurredOn.Date >= start.Date && log.OccurredOn.Date <= end.Date)
            .GroupBy(log => log.ExpenseCategory ?? ExpenseCategory.Needs)
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));
    }

    private static IReadOnlyList<Transaction> SelectBudgetEffectiveLogs(IReadOnlyList<Transaction> expenseLogs)
    {
        var activeLogs = expenseLogs.Where(log => !log.IsForDeletion && !log.IsExcludedFromBudget).ToList();
        var parentLogIds = activeLogs
            .Where(log => log.ParentTransactionId is > 0)
            .Select(log => log.ParentTransactionId!.Value)
            .ToHashSet();

        return activeLogs
            .Where(log => !parentLogIds.Contains(log.Id))
            .ToList();
    }

    private static int CountMonthlyOccurrences(int recurringTime, DateTime start, DateTime end)
    {
        if (recurringTime is < 1 or > 28)
            return 0;

        var count = 0;
        var cursor = new DateTime(start.Year, start.Month, recurringTime);
        if (cursor < start)
            cursor = cursor.AddMonths(1);

        while (cursor <= end)
        {
            count++;
            cursor = cursor.AddMonths(1);
        }

        return count;
    }

    private static int CountDayIntervalOccurrences(int recurringTime, DateTime start, DateTime end, int intervalDays)
    {
        if (recurringTime is < 1 or > 7)
            return 0;

        var cursor = start;
        while (GetIsoDayOfWeek(cursor.DayOfWeek) != recurringTime)
            cursor = cursor.AddDays(1);

        var count = 0;
        while (cursor <= end)
        {
            count++;
            cursor = cursor.AddDays(intervalDays);
        }

        return count;
    }

    private static int GetIsoDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
    }

    private static bool TryParseMoney(string text, out decimal amount)
    {
        return decimal.TryParse(
            text?.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static string FormatMoney(decimal amount)
    {
        return amount.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private void OnRecurringRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BudgetForecastRecurringRowVM.IsIncluded))
            Recalculate();
    }

}

public sealed partial class BudgetForecastAccountRowVM(
    int id,
    string name,
    decimal currentBalance) : ObservableObject
{
    [ObservableProperty] private decimal _balance = currentBalance;

    public int Id { get; } = id;
    public string Name { get; } = name;
    public decimal CurrentBalance { get; } = currentBalance;
    public bool IsBalanceDecreased => Balance < CurrentBalance;
    public bool IsBalanceIncreased => Balance > CurrentBalance;

    partial void OnBalanceChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsBalanceDecreased));
        OnPropertyChanged(nameof(IsBalanceIncreased));
    }
}

public sealed partial class BudgetForecastRecurringRowVM : ObservableObject
{
    [ObservableProperty] private bool _isIncluded;

    public BudgetForecastRecurringRowVM(
        int id,
        string name,
        decimal amount,
        int accountId,
        string accountName,
        RecurringTransactionType type,
        ExpenseCategory category,
        RecurringPeriod recurringPeriod,
        int recurringTime,
        bool isIncluded)
    {
        Id = id;
        Name = name;
        Amount = amount;
        AccountId = accountId;
        AccountName = accountName;
        Type = type;
        Category = category;
        RecurringPeriod = recurringPeriod;
        RecurringTime = recurringTime;
        _isIncluded = isIncluded;
    }

    public int Id { get; }
    public string Name { get; }
    public decimal Amount { get; }
    public decimal SignedAmount => Type == RecurringTransactionType.Income ? Amount : -Amount;
    public int AccountId { get; }
    public string AccountName { get; }
    public RecurringTransactionType Type { get; }
    public ExpenseCategory Category { get; }
    public RecurringPeriod RecurringPeriod { get; }
    public int RecurringTime { get; }
}
