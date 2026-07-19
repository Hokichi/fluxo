using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Shell.Main;

public partial class MainVM : ObservableRecipient
{
    private readonly IDataOperationRunner _dataOperationRunner;
    private bool _isInitialized;

    [ObservableProperty] private string _username = "User";

    public bool IsInitialized => _isInitialized;

    public MainVM(
        IDataOperationRunner dataOperationRunner,
        DashboardVM dashboard,
        Main.DaySpinnerVM daySpinner,
        Main.LedgerVM? ledger = null,
        IUiLockPasswordProtector? passwordProtector = null)
    {
        _dataOperationRunner = dataOperationRunner;
        Dashboard = dashboard;
        DaySpinner = daySpinner;
        Ledger = ledger;
        AppLock = new AppLockState(passwordProtector);
        Dashboard.PropertyChanged += OnDashboardPropertyChanged;
        AppLock.PropertyChanged += OnAppLockPropertyChanged;

        WeakReferenceMessenger.Default.Register<MainVM, UsernameChangedMessage>(this,
            static (recipient, message) => recipient.Username = message.Value);
        WeakReferenceMessenger.Default.Register<MainVM, TransactionDetailUpdatedMessage>(this,
            static (recipient, message) => recipient.HandleTransactionDetailUpdatedMessage(message));
    }

    public DashboardVM Dashboard { get; }
    public Main.NotificationPanelVM NotificationPanel => Dashboard.NotificationPanel;
    public Main.BudgetAllocationPanelVM BudgetPanel => Dashboard.BudgetPanel;
    public Main.SpentAllowancePanelVM SpentAllowancePanel => Dashboard.SpentAllowancePanel;
    public Main.SavingGoalsPanelVM SavingGoalsPanel => Dashboard.SavingGoalsPanel;
    public Main.UpcomingEventsPanelVM UpcomingEventsPanel => Dashboard.UpcomingEventsPanel;
    public Main.DaySpinnerVM DaySpinner { get; }
    public Main.LedgerVM? Ledger { get; }
    public AppLockState AppLock { get; }

    public bool IsDashboardSpendingAmountGateLocked => Dashboard.IsDashboardSpendingAmountGateLocked;
    public bool IsSufficientFundsActionGateLocked => Dashboard.IsSufficientFundsActionGateLocked;
    public bool IsAppAutoLocked => AppLock.IsAppAutoLocked;
    public int AppAutoLockedInterval => AppLock.AppAutoLockedInterval;
    public bool IsAppLocked => AppLock.IsAppLocked;
    public bool HasUiLockingPassword => AppLock.HasUiLockingPassword;
    public bool IsAnyActionGateLocked => IsAppLocked || IsSufficientFundsActionGateLocked;
    public string AppLockButtonText => AppLock.AppLockButtonText;

    public ObservableCollection<AccountVM> Accounts => Dashboard.Accounts;

    public void ToggleAccountFilter(AccountVM? account)
    {
        Dashboard.ToggleAccountFilter(account);
    }

    public Task Initialize()
    {
        return InitializeWithStartupStagesAsync(static () => Task.CompletedTask);
    }

    public async Task InitializeWithStartupStagesAsync(Func<Task> betweenStagesAsync)
    {
        ArgumentNullException.ThrowIfNull(betweenStagesAsync);

        await LoadUserSettingsAsync();
        await betweenStagesAsync();

        await Dashboard.InitializeWithStartupStagesAsync(betweenStagesAsync);

        if (Ledger is not null)
        {
            await Ledger.LoadAsync();
            await betweenStagesAsync();
        }

        _isInitialized = true;
        OnPropertyChanged(nameof(IsInitialized));
    }

    public Task ReloadCurrentDataAsync() => ReloadCurrentDataAsync(reloadNotifications: true);

    public async Task ReloadCurrentDataAsync(bool reloadNotifications)
    {
        await Dashboard.ReloadCurrentDataAsync(reloadNotifications);

        if (Ledger is not null)
            await Ledger.LoadAsync();
    }

    public Task ReloadUserSettingsAsync()
    {
        return LoadUserSettingsAsync();
    }

    private async Task LoadUserSettingsAsync()
    {
        var settingsByName = await _dataOperationRunner.RunAsync(async (scope, ct) =>
        {
            var settings = await scope.UnitOfWork.UserSettings.GetAllAsync(ct);
            return settings.ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal);
        });

        if (settingsByName.TryGetValue(UserSettingNames.PreferredDisplayName, out var name))
        {
            var trimmed = (name ?? string.Empty).Trim();
            Username = trimmed.Length > 0 ? trimmed : "User";
        }

        AppLock.ApplySettings(settingsByName);
        OnPropertyChanged(nameof(IsAppAutoLocked));
        OnPropertyChanged(nameof(AppAutoLockedInterval));
        OnPropertyChanged(nameof(HasUiLockingPassword));
    }

    public void LockUi()
    {
        AppLock.LockUi();
    }

    public bool TryUnlockUi(string? password)
    {
        return AppLock.TryUnlockUi(password);
    }

    private void HandleTransactionDetailUpdatedMessage(TransactionDetailUpdatedMessage message)
    {
        if (!message.Value.HasChanges)
            return;

        _ = ReloadCurrentDataAsync(
            reloadNotifications: !message.Value.SuppressNotificationInvalidation);
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DashboardVM.IsDashboardSpendingAmountGateLocked):
                OnPropertyChanged(nameof(IsDashboardSpendingAmountGateLocked));
                break;
            case nameof(DashboardVM.IsSufficientFundsActionGateLocked):
                OnPropertyChanged(nameof(IsSufficientFundsActionGateLocked));
                OnPropertyChanged(nameof(IsAnyActionGateLocked));
                break;
        }
    }

    private void OnAppLockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppLockState.IsAppAutoLocked):
                OnPropertyChanged(nameof(IsAppAutoLocked));
                break;
            case nameof(AppLockState.AppAutoLockedInterval):
                OnPropertyChanged(nameof(AppAutoLockedInterval));
                break;
            case nameof(AppLockState.IsAppLocked):
                OnPropertyChanged(nameof(IsAppLocked));
                OnPropertyChanged(nameof(IsAnyActionGateLocked));
                OnPropertyChanged(nameof(AppLockButtonText));
                break;
            case nameof(AppLockState.AppLockButtonText):
                OnPropertyChanged(nameof(AppLockButtonText));
                break;
            case nameof(AppLockState.HasUiLockingPassword):
                OnPropertyChanged(nameof(HasUiLockingPassword));
                break;
        }
    }
}
