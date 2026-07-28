using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Helpers.MainWindow;
using Fluxo.Helpers.Settings;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Shell.Main;

public partial class MainVM : ObservableRecipient
{
    private readonly IDataOperationRunner _dataOperationRunner;
    private readonly AppLockHelper _appLockHelper;
    private bool _isInitialized;

    [ObservableProperty] private bool _isAppAutoLocked;
    [ObservableProperty] private int _appAutoLockedInterval = AutoLockPreset.DefaultIntervalSeconds;
    [ObservableProperty] private bool _isAppLocked;
    [ObservableProperty] private string _username = "User";

    public bool IsInitialized => _isInitialized;

    public MainVM(
        IDataOperationRunner dataOperationRunner,
        DashboardVM dashboard,
        Main.DaySpinnerVM daySpinner,
        Main.LedgerVM? ledger = null,
        IUiLockPasswordProtector? passwordProtector = null,
        IMessenger? messenger = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _dataOperationRunner = dataOperationRunner;
        Dashboard = dashboard;
        DaySpinner = daySpinner;
        Ledger = ledger;
        _appLockHelper = new AppLockHelper(passwordProtector);
        Dashboard.PropertyChanged += OnDashboardPropertyChanged;

        Messenger.Register<MainVM, UsernameChangedMessage>(this,
            static (recipient, message) => recipient.Username = message.Value);
        Messenger.Register<MainVM, TransactionDetailUpdatedMessage>(this,
            static (recipient, message) => recipient.HandleTransactionDetailUpdatedMessage(message));
        Messenger.Register<MainVM, DashboardDataInvalidatedMessage>(this,
            static (recipient, message) => recipient.HandleDashboardDataInvalidatedMessage(message));
    }

    public DashboardVM Dashboard { get; }
    public Main.NotificationPanelVM NotificationPanel => Dashboard.NotificationPanel;
    public Main.BudgetAllocationPanelVM BudgetPanel => Dashboard.BudgetPanel;
    public Main.SpentAllowancePanelVM SpentAllowancePanel => Dashboard.SpentAllowancePanel;
    public Main.SavingGoalsPanelVM SavingGoalsPanel => Dashboard.SavingGoalsPanel;
    public Main.UpcomingEventsPanelVM UpcomingEventsPanel => Dashboard.UpcomingEventsPanel;
    public Main.DaySpinnerVM DaySpinner { get; }
    public Main.LedgerVM? Ledger { get; }
    public bool IsDashboardSpendingAmountGateLocked => Dashboard.IsDashboardSpendingAmountGateLocked;
    public bool IsSufficientFundsActionGateLocked => Dashboard.IsSufficientFundsActionGateLocked;
    public bool HasUiLockingPassword => _appLockHelper.HasUiLockingPassword;
    public bool IsAnyActionGateLocked => IsAppLocked || IsSufficientFundsActionGateLocked;
    public string AppLockButtonText => IsAppLocked ? "Unlock fluxo" : "Lock fluxo";

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

        (IsAppAutoLocked, AppAutoLockedInterval) = _appLockHelper.ApplySettings(settingsByName);
        OnPropertyChanged(nameof(HasUiLockingPassword));
    }

    public void LockUi()
    {
        if (!IsAppLocked)
            IsAppLocked = true;
    }

    public bool TryUnlockUi(string? password)
    {
        if (!IsAppLocked)
            return true;

        if (!_appLockHelper.CanUnlock(password))
            return false;

        IsAppLocked = false;
        return true;
    }

    private void HandleTransactionDetailUpdatedMessage(TransactionDetailUpdatedMessage message)
    {
        if (!message.Value.HasChanges)
            return;

        _ = ReloadCurrentDataAsync(
            reloadNotifications: !message.Value.SuppressNotificationInvalidation);
    }

    private void HandleDashboardDataInvalidatedMessage(DashboardDataInvalidatedMessage message)
    {
        if (!message.Value.HasFlag(DashboardDataInvalidationScope.Budget) &&
            !message.Value.HasFlag(DashboardDataInvalidationScope.SavingGoals) &&
            !message.Value.HasFlag(DashboardDataInvalidationScope.All))
            return;

        if (Ledger is not null)
            _ = Ledger.LoadAsync();
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

    partial void OnIsAppLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyActionGateLocked));
        OnPropertyChanged(nameof(AppLockButtonText));
    }
}
