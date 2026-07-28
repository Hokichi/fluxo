using System.Collections.ObjectModel;
using Fluxo.Helpers.Settings;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups.Settings;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardNotificationVM : ObservableObject
{
    private readonly IAppDataService _appData;
    private readonly IMessenger _messenger;

    [ObservableProperty] private bool _isStep7Active;
    [ObservableProperty] private bool _shouldRunAtStartup;
    [ObservableProperty] private AppCloseBehavior _closeBehavior = AppCloseBehavior.Exit;

    public QuickSetupWizardNotificationVM(IAppDataService appData, IMessenger? messenger = null)
    {
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public ObservableCollection<SettingsNotificationOptionVM> NotificationSettings { get; } = [];

    public bool IsMinimizeToTrayCloseBehaviorSelected => CloseBehavior == AppCloseBehavior.MinimizeToTray;

    public bool IsExitCloseBehaviorSelected => CloseBehavior == AppCloseBehavior.Exit;

    public async Task LoadAsync()
    {
        var settings = await _appData.GetUserSettingsAsync();
        var settingsByName = settings.ToDictionary(setting => setting.Name, setting => setting.Value, StringComparer.Ordinal);

        ReplaceNotificationSettings(
        [
            new SettingsNotificationOptionVM(
                "Overdue recurring transactions",
                "Warn when a recurring transaction needs completion.",
                UserSettingNames.IsRecurringOverdueNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsRecurringOverdueNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Overdue saving goals",
                "Warn when a savings goal is past its target date.",
                UserSettingNames.IsGoalOverdueNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsGoalOverdueNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Daily allowance alerts",
                "Warn when daily spending reaches its allowance.",
                UserSettingNames.IsDailyAllowanceNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsDailyAllowanceNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Late payment alerts",
                "Warn when Credit payments are past due.",
                UserSettingNames.IsLatePaymentNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsLatePaymentNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Budget threshold alerts",
                "Warn when Needs, Wants, or Invest allocations are nearly spent.",
                UserSettingNames.IsBudgetThresholdNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsBudgetThresholdNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Low credit usage alerts",
                "Warn when credit accounts cross their usage threshold.",
                UserSettingNames.IsLowCreditNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsLowCreditNotifEnabled, false)),
            new SettingsNotificationOptionVM(
                "Low account balance alerts",
                "Warn when checking or cash sources are running low.",
                UserSettingNames.IsLowAccountBalanceNotifEnabled,
                QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsLowAccountBalanceNotifEnabled, false))
        ]);

        ShouldRunAtStartup = QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.ShouldRunAtStartup, false);
        CloseBehavior = QuickSetupWizardShared.ParseCloseBehavior(settingsByName, UserSettingNames.CloseBehavior, AppCloseBehavior.Exit);

        PublishSnapshot();
    }

    public async Task<SettingsOperationResult> SaveAsync()
    {
        await ApplyAsync(_appData);
        await _appData.SaveChangesAsync();
        _messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.Notifications));
        PublishSnapshot();
        return SettingsOperationResult.Success();
    }

    public async Task ApplyAsync(IAppDataService appData)
    {
        foreach (var setting in NotificationSettings)
            await QuickSetupWizardShared.UpsertUserSettingAsync(
                appData,
                setting.SettingName,
                setting.IsEnabled.ToString());

        await QuickSetupWizardShared.UpsertUserSettingAsync(
            appData,
            UserSettingNames.ShouldRunAtStartup,
            ShouldRunAtStartup.ToString());

        await QuickSetupWizardShared.UpsertUserSettingAsync(
            appData,
            UserSettingNames.CloseBehavior,
            CloseBehavior.ToString());
    }

    private void ReplaceNotificationSettings(IEnumerable<SettingsNotificationOptionVM> options)
    {
        foreach (var existing in NotificationSettings)
            existing.PropertyChanged -= OnNotificationOptionPropertyChanged;

        QuickSetupWizardShared.ReplaceCollection(NotificationSettings, options);

        foreach (var option in NotificationSettings)
            option.PropertyChanged += OnNotificationOptionPropertyChanged;
    }

    private void OnNotificationOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SettingsNotificationOptionVM.IsEnabled), StringComparison.Ordinal))
            return;

        PublishSnapshot();
    }

    private void PublishSnapshot()
    {
        _messenger.Send(new QuickSetupWizardNotificationsChangedMessage(
            new QuickSetupWizardNotificationsChanged(
                EnabledCount: NotificationSettings.Count(setting => setting.IsEnabled),
                TotalCount: NotificationSettings.Count)));
    }

    partial void OnCloseBehaviorChanged(AppCloseBehavior value)
    {
        OnPropertyChanged(nameof(IsMinimizeToTrayCloseBehaviorSelected));
        OnPropertyChanged(nameof(IsExitCloseBehaviorSelected));
    }

    [RelayCommand]
    private void SetCloseBehavior(AppCloseBehavior closeBehavior)
    {
        CloseBehavior = closeBehavior;
    }
}

