using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Shell.Main;

public partial class DashboardVM : ObservableObject
{
    private bool _isInitialized;

    [ObservableProperty] private bool _isDashboardSpendingAmountGateLocked;
    [ObservableProperty] private bool _isSufficientFundsActionGateLocked;

    public DashboardVM(
        NotificationPanelVM notificationPanel,
        RecentActivitiesVM budgetPanel,
        SpentAllowancePanelVM spentAllowancePanel,
        SavingGoalsPanelVM savingGoalsPanel,
        UpcomingEventsPanelVM upcomingEventsPanel,
        MainViewModeToggleVM viewModeToggle,
        AllocationDataVM? allocationData = null)
    {
        NotificationPanel = notificationPanel;
        AllocationData = allocationData;
        BudgetPanel = budgetPanel;
        SpentAllowancePanel = spentAllowancePanel;
        SavingGoalsPanel = savingGoalsPanel;
        UpcomingEventsPanel = upcomingEventsPanel;
        ViewModeToggle = viewModeToggle;
    }

    public bool IsInitialized => _isInitialized;

    public NotificationPanelVM NotificationPanel { get; }
    public AllocationDataVM? AllocationData { get; }
    public RecentActivitiesVM BudgetPanel { get; }
    public SpentAllowancePanelVM SpentAllowancePanel { get; }
    public SavingGoalsPanelVM SavingGoalsPanel { get; }
    public UpcomingEventsPanelVM UpcomingEventsPanel { get; }
    public MainViewModeToggleVM ViewModeToggle { get; }

    public ObservableCollection<AccountVM> Accounts => BudgetPanel.Accounts;

    public void ToggleAccountFilter(AccountVM? account)
    {
        BudgetPanel.ToggleSelectedAccount(account);
    }

    public Task Initialize()
    {
        return InitializeWithStartupStagesAsync(static () => Task.CompletedTask);
    }

    public async Task InitializeWithStartupStagesAsync(Func<Task> betweenStagesAsync)
    {
        ArgumentNullException.ThrowIfNull(betweenStagesAsync);

        await BudgetPanel.LoadAsync();
        RefreshSpendingAmountGateStates();
        await betweenStagesAsync();

        if (AllocationData is not null)
        {
            await AllocationData.LoadAsync();
            await betweenStagesAsync();
        }

        await SpentAllowancePanel.LoadAsync();
        await betweenStagesAsync();

        await SavingGoalsPanel.LoadAsync();
        await betweenStagesAsync();

        await UpcomingEventsPanel.LoadAsync();
        await betweenStagesAsync();

        await NotificationPanel.LoadAsync();
        await betweenStagesAsync();

        ViewModeToggle.SetSelectedMainContentViewCommand.Execute(
            ViewModeToggle.SelectedMainContentViewMode);
        _isInitialized = true;
        OnPropertyChanged(nameof(IsInitialized));
    }

    public Task ReloadCurrentDataAsync() => ReloadCurrentDataAsync(reloadNotifications: true);

    public async Task ReloadCurrentDataAsync(bool reloadNotifications)
    {
        await BudgetPanel.LoadAsync();
        if (AllocationData is not null)
            await AllocationData.LoadAsync();
        RefreshSpendingAmountGateStates();

        await SpentAllowancePanel.LoadAsync();
        await SavingGoalsPanel.LoadAsync();
        await UpcomingEventsPanel.LoadAsync();
        if (reloadNotifications)
            await NotificationPanel.LoadAsync();
    }

    public static bool ShouldLockDashboardForSpendingAmount(
        IEnumerable<AccountVM> accounts,
        IEnumerable<TransactionVM> expenseLogs)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(expenseLogs);

        return !accounts.Any(source => source.IsEnabled);
    }

    public static bool ShouldLockActionsForSufficientFunds(
        IEnumerable<AccountVM> accounts,
        IEnumerable<TransactionVM> expenseLogs)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(expenseLogs);

        var hasUsableFunds = accounts
            .Where(source => source.IsEnabled)
            .Any(HasUsableFunds);
        var hasActiveExpenseLogs = expenseLogs.Any(log => !log.IsForDeletion && log.ParentTransactionId is null);

        return !hasUsableFunds && !hasActiveExpenseLogs;
    }

    private static bool HasUsableFunds(AccountVM source)
    {
        return source.AccountType == Fluxo.Core.Enums.AccountType.Credit
            ? source.AccountLimit > 0m
            : source.Balance > 0m;
    }

    private void RefreshSpendingAmountGateStates()
    {
        IsDashboardSpendingAmountGateLocked = ShouldLockDashboardForSpendingAmount(
            Accounts,
            BudgetPanel.GetAllExpenseLogs());
        IsSufficientFundsActionGateLocked =
            IsDashboardSpendingAmountGateLocked ||
            ShouldLockActionsForSufficientFunds(Accounts, BudgetPanel.GetAllExpenseLogs());
    }
}
