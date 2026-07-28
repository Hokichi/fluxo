using System.Collections.ObjectModel;
using Fluxo.Helpers.Settings;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Ui;
using Fluxo.Services.Updates;
using Fluxo.ViewModels.Shell;

namespace Fluxo.ViewModels.Popups.Settings;

public partial class SettingsPersonalizationTabVM : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly Dictionary<string, bool> _savedNotificationSettings = new(StringComparer.Ordinal);
    private readonly IAppDataService _appData;
    private readonly IUiLockPasswordProtector _passwordProtector;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IAppUpdateInteractionService? _appUpdateInteractionService;
    private string _savedPreferredAppName = string.Empty;
    private bool _savedShouldRunAtStartup;
    private AppCloseBehavior _savedCloseBehavior = AppCloseBehavior.Exit;
    private bool _savedIsAppAutoLocked;
    private int _savedAppAutoLockedInterval = AutoLockPreset.DefaultIntervalSeconds;
    private string _savedUiLockingPassword = string.Empty;

    [ObservableProperty] private string _selectedPreferencesPage = "Personalization";
    [ObservableProperty] private string _preferredAppName = string.Empty;
    [ObservableProperty] private bool _shouldRunAtStartup;
    [ObservableProperty] private AppCloseBehavior _closeBehavior = AppCloseBehavior.Exit;
    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _isAppAutoLocked;
    [ObservableProperty] private int _appAutoLockedInterval = AutoLockPreset.DefaultIntervalSeconds;
    [ObservableProperty] private string _selectedAppAutoLockPreset = AutoLockPreset.Seconds30;
    [ObservableProperty] private string _uiLockingPassword = string.Empty;
    [ObservableProperty] private bool _isUiLockingPasswordVisible;

    public SettingsPersonalizationTabVM(
        IAppDataService appData,
        IMessenger? messenger = null,
        IAppUpdateService? appUpdateService = null,
        IAppUpdateInteractionService? appUpdateInteractionService = null,
        IUiLockPasswordProtector? passwordProtector = null)
    {
        _appData = appData;
        _passwordProtector = passwordProtector ?? new DpapiUiLockPasswordProtector();
        _appUpdateService = appUpdateService ?? new AppUpdateService();
        _appUpdateInteractionService = appUpdateInteractionService;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public ObservableCollection<SettingsNotificationOptionVM> NotificationSettings { get; } = [];

    public bool IsMinimizeToTrayCloseBehaviorSelected => CloseBehavior == AppCloseBehavior.MinimizeToTray;

    public bool IsExitCloseBehaviorSelected => CloseBehavior == AppCloseBehavior.Exit;

    public string CurrentVersion { get; } = AppVersionResolver.ResolveCurrentVersion();

    public bool HasAutoLockInterval => IsAppAutoLocked;

    public bool IsCustomAutoLockInterval => AutoLockPreset.IsCustom(SelectedAppAutoLockPreset);

    public bool IsPersonalizationPageSelected =>
        string.Equals(SelectedPreferencesPage, "Personalization", StringComparison.Ordinal);

    public bool IsNotificationPageSelected =>
        string.Equals(SelectedPreferencesPage, "Notification", StringComparison.Ordinal);

    public bool HasPendingChanges =>
        !string.Equals((PreferredAppName ?? string.Empty).Trim(), _savedPreferredAppName, StringComparison.Ordinal) ||
        ShouldRunAtStartup != _savedShouldRunAtStartup ||
        CloseBehavior != _savedCloseBehavior ||
        IsAppAutoLocked != _savedIsAppAutoLocked ||
        AppAutoLockedInterval != _savedAppAutoLockedInterval ||
        !string.Equals(UiLockingPassword ?? string.Empty, _savedUiLockingPassword, StringComparison.Ordinal) ||
        NotificationSettings.Any(setting =>
            _savedNotificationSettings.TryGetValue(setting.SettingName, out var savedValue)
                ? savedValue != setting.IsEnabled
                : setting.IsEnabled);

    public bool HasPendingPasswordChange =>
        !string.Equals(UiLockingPassword ?? string.Empty, _savedUiLockingPassword, StringComparison.Ordinal);

    public bool HasPendingAutoLockEnabledChange => IsAppAutoLocked != _savedIsAppAutoLocked;

    public bool HasPendingAutoLockIntervalChange => AppAutoLockedInterval != _savedAppAutoLockedInterval;

    public bool HasPendingNotificationChanges =>
        NotificationSettings.Any(setting =>
            _savedNotificationSettings.TryGetValue(setting.SettingName, out var savedValue)
                ? savedValue != setting.IsEnabled
                : setting.IsEnabled);

    public async Task LoadAsync()
    {
        var settingsByName = await SettingsShared.GetSettingsDictionaryAsync(_appData);
        PreferredAppName = SettingsShared.ParseString(settingsByName, UserSettingNames.PreferredDisplayName, string.Empty);
        ShouldRunAtStartup = SettingsShared.ParseBool(settingsByName, UserSettingNames.ShouldRunAtStartup, false);
        CloseBehavior = SettingsShared.ParseCloseBehavior(settingsByName, UserSettingNames.CloseBehavior, AppCloseBehavior.Exit);
        IsAppAutoLocked = SettingsShared.ParseBool(settingsByName, UserSettingNames.IsAppAutoLocked, false);
        AppAutoLockedInterval =
            SettingsShared.ParsePositiveInt(
                settingsByName,
                UserSettingNames.AppAutoLockedInterval,
                AutoLockPreset.DefaultIntervalSeconds);
        SelectedAppAutoLockPreset = AutoLockPreset.FromIntervalSeconds(AppAutoLockedInterval);
        UiLockingPassword = _passwordProtector.Unprotect(
            SettingsShared.ParseString(settingsByName, UserSettingNames.UILockingPassword, string.Empty));
        _savedPreferredAppName = (PreferredAppName ?? string.Empty).Trim();
        _savedShouldRunAtStartup = ShouldRunAtStartup;
        _savedCloseBehavior = CloseBehavior;
        _savedIsAppAutoLocked = IsAppAutoLocked;
        _savedAppAutoLockedInterval = AppAutoLockedInterval;
        _savedUiLockingPassword = UiLockingPassword;
        LoadNotificationSettings(settingsByName);
        OnPropertyChanged(nameof(HasPendingPasswordChange));
        OnPropertyChanged(nameof(HasPendingNotificationChanges));
        RaiseAutoLockPendingProperties();
        PublishPendingState();
    }

    public Task<SettingsMaintenanceResult> ResetAllSettingsAsync()
    {
        return SendMaintenanceRequestAsync(SettingsMaintenanceRequestType.ResetAllSettings, keepSettings: true);
    }

    public Task<SettingsMaintenanceResult> DeleteAllDataAsync(bool keepSettings)
    {
        return SendMaintenanceRequestAsync(SettingsMaintenanceRequestType.DeleteAllData, keepSettings);
    }

    public void RequestClosePopup()
    {
        _messenger.Send(new SettingsPopupCloseRequestedMessage(new SettingsPopupCloseRequest()));
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        try
        {
            return await _appUpdateService.CheckForUpdatesAsync(CurrentVersion);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    public Task<string> DownloadUpdateInstallerAsync(AppUpdateCheckResult update)
    {
        if (update.Status != AppUpdateCheckStatus.UpdateAvailable
            || string.IsNullOrWhiteSpace(update.InstallerDownloadUrl)
            || string.IsNullOrWhiteSpace(update.InstallerAssetName))
        {
            throw new InvalidOperationException("No Fluxo update installer is available to download.");
        }

        return _appUpdateService.DownloadInstallerAsync(
            update.InstallerDownloadUrl,
            update.InstallerAssetName);
    }

    public void DeleteDownloadedInstaller(string installerPath)
    {
        _appUpdateService.DeleteInstaller(installerPath);
    }

    public Task HandleAvailableUpdateAsync(AppUpdateCheckResult update, Window? owner)
    {
        if (_appUpdateInteractionService is null)
            return Task.CompletedTask;

        return _appUpdateInteractionService.HandleAvailableUpdateAsync(update, owner);
    }

    public async Task<(
        SettingsOperationResult Result,
        List<ILogMemoryAction> Actions,
        string? OldUsername,
        string? NewUsername,
        bool ShouldRunAtStartup)>
        BuildApplyChangesAsync()
    {
        var actions = new List<ILogMemoryAction>();

        await SettingsShared.UpdateUserSettingAsync(_appData, UserSettingNames.PreferredDisplayName,
            string.IsNullOrWhiteSpace(PreferredAppName) ? null : PreferredAppName.Trim(), actions);

        foreach (var notificationSetting in NotificationSettings)
            await SettingsShared.UpdateUserSettingAsync(_appData, notificationSetting.SettingName,
                notificationSetting.IsEnabled.ToString(CultureInfo.InvariantCulture), actions);

        await SettingsShared.UpdateUserSettingAsync(_appData, UserSettingNames.ShouldRunAtStartup,
            ShouldRunAtStartup.ToString(CultureInfo.InvariantCulture), actions);

        await SettingsShared.UpdateUserSettingAsync(_appData, UserSettingNames.CloseBehavior,
            CloseBehavior.ToString(), actions);

        await SettingsShared.UpdateUserSettingAsync(_appData, UserSettingNames.IsAppAutoLocked,
            IsAppAutoLocked.ToString(CultureInfo.InvariantCulture), actions);

        await SettingsShared.UpdateUserSettingAsync(_appData, UserSettingNames.AppAutoLockedInterval,
            Math.Max(1, AppAutoLockedInterval).ToString(CultureInfo.InvariantCulture), actions);

        var protectedPassword = _passwordProtector.Protect(UiLockingPassword);
        await SettingsShared.UpdateUserSettingAsync(_appData, UserSettingNames.UILockingPassword,
            string.IsNullOrWhiteSpace(protectedPassword) ? null : protectedPassword, actions);

        var newUsername = string.IsNullOrWhiteSpace(PreferredAppName) ? "User" : PreferredAppName.Trim();
        var oldUsername = string.IsNullOrWhiteSpace(_savedPreferredAppName) ? "User" : _savedPreferredAppName;
        return (SettingsOperationResult.Success(), actions, oldUsername, newUsername, ShouldRunAtStartup);
    }

    public void CommitSavedState()
    {
        _savedPreferredAppName = (PreferredAppName ?? string.Empty).Trim();
        _savedShouldRunAtStartup = ShouldRunAtStartup;
        _savedCloseBehavior = CloseBehavior;
        _savedIsAppAutoLocked = IsAppAutoLocked;
        _savedAppAutoLockedInterval = AppAutoLockedInterval;
        _savedUiLockingPassword = UiLockingPassword;
        _savedNotificationSettings.Clear();
        foreach (var setting in NotificationSettings)
            _savedNotificationSettings[setting.SettingName] = setting.IsEnabled;
        OnPropertyChanged(nameof(HasPendingPasswordChange));
        OnPropertyChanged(nameof(HasPendingNotificationChanges));
        RaiseAutoLockPendingProperties();
        PublishPendingState();
    }

    public void RevertChanges()
    {
        PreferredAppName = _savedPreferredAppName;
        ShouldRunAtStartup = _savedShouldRunAtStartup;
        CloseBehavior = _savedCloseBehavior;
        IsAppAutoLocked = _savedIsAppAutoLocked;
        AppAutoLockedInterval = _savedAppAutoLockedInterval;
        SelectedAppAutoLockPreset = AutoLockPreset.FromIntervalSeconds(AppAutoLockedInterval);
        UiLockingPassword = _savedUiLockingPassword;
        foreach (var setting in NotificationSettings)
            if (_savedNotificationSettings.TryGetValue(setting.SettingName, out var value))
                setting.IsEnabled = value;
        OnPropertyChanged(nameof(HasPendingPasswordChange));
        OnPropertyChanged(nameof(HasPendingNotificationChanges));
        RaiseAutoLockPendingProperties();
        PublishPendingState();
    }

    partial void OnSelectedPreferencesPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsPersonalizationPageSelected));
        OnPropertyChanged(nameof(IsNotificationPageSelected));
        PublishPendingState();
    }

    partial void OnPreferredAppNameChanged(string value)
    {
        PublishPendingState();
    }

    partial void OnShouldRunAtStartupChanged(bool value)
    {
        PublishPendingState();
    }

    partial void OnCloseBehaviorChanged(AppCloseBehavior value)
    {
        OnPropertyChanged(nameof(IsMinimizeToTrayCloseBehaviorSelected));
        OnPropertyChanged(nameof(IsExitCloseBehaviorSelected));
        PublishPendingState();
    }

    partial void OnIsAppAutoLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAutoLockInterval));
        OnPropertyChanged(nameof(HasPendingAutoLockEnabledChange));
        PublishPendingState();
    }

    partial void OnAppAutoLockedIntervalChanged(int value)
    {
        if (value < 1)
        {
            AppAutoLockedInterval = 1;
            return;
        }

        var preset = AutoLockPreset.FromIntervalSeconds(value);
        if (!string.Equals(SelectedAppAutoLockPreset, preset, StringComparison.Ordinal))
            SelectedAppAutoLockPreset = preset;

        OnPropertyChanged(nameof(HasPendingAutoLockIntervalChange));
        PublishPendingState();
    }

    partial void OnSelectedAppAutoLockPresetChanged(string value)
    {
        if (AutoLockPreset.TryGetSeconds(value, out var seconds) &&
            AppAutoLockedInterval != seconds)
        {
            AppAutoLockedInterval = seconds;
        }

        OnPropertyChanged(nameof(IsCustomAutoLockInterval));
        PublishPendingState();
    }

    partial void OnUiLockingPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(HasPendingPasswordChange));
        PublishPendingState();
    }

    partial void OnIsUiLockingPasswordVisibleChanged(bool value)
    {
        PublishPendingState();
    }

    [RelayCommand]
    private void SetCloseBehavior(AppCloseBehavior closeBehavior)
    {
        CloseBehavior = closeBehavior;
    }

    private void LoadNotificationSettings(IReadOnlyDictionary<string, string> settingsByName)
    {
        foreach (var notificationSetting in NotificationSettings)
            notificationSetting.PropertyChanged -= OnNotificationSettingPropertyChanged;

        SettingsShared.ReplaceCollection(NotificationSettings,
        [
            new SettingsNotificationOptionVM(
                "Overdue recurring transactions",
                "Warn when a recurring transaction needs completion.",
                UserSettingNames.IsRecurringOverdueNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsRecurringOverdueNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Overdue saving goals",
                "Warn when a saving goal is past target date.",
                UserSettingNames.IsGoalOverdueNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsGoalOverdueNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Daily allowance alerts",
                "Warn when today’s spending reaches allowance.",
                UserSettingNames.IsDailyAllowanceNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsDailyAllowanceNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Late payment alerts",
                "Warn when Credit payments are past due.",
                UserSettingNames.IsLatePaymentNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsLatePaymentNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Budget threshold alerts",
                "Warn when Needs, Wants, or Invest allocations are nearly spent.",
                UserSettingNames.IsBudgetThresholdNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsBudgetThresholdNotifEnabled, true)),
            new SettingsNotificationOptionVM(
                "Low credit usage alerts",
                "Warn when credit accounts cross their usage threshold.",
                UserSettingNames.IsLowCreditNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsLowCreditNotifEnabled, false)),
            new SettingsNotificationOptionVM(
                "Low account balance alerts",
                "Warn when checking or cash sources are running low.",
                UserSettingNames.IsLowAccountBalanceNotifEnabled,
                SettingsShared.ParseBool(settingsByName, UserSettingNames.IsLowAccountBalanceNotifEnabled,
                    SettingsShared.ParseBool(settingsByName, UserSettingNames.IsLowCreditNotifEnabled, false)))
        ]);

        _savedNotificationSettings.Clear();
        foreach (var notificationSetting in NotificationSettings)
        {
            notificationSetting.PropertyChanged += OnNotificationSettingPropertyChanged;
            _savedNotificationSettings[notificationSetting.SettingName] = notificationSetting.IsEnabled;
        }
    }

    private void OnNotificationSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsNotificationOptionVM.IsEnabled))
        {
            OnPropertyChanged(nameof(HasPendingNotificationChanges));
            PublishPendingState();
        }
    }

    private void PublishPendingState()
    {
        _messenger.Send(new SettingsPendingChangesChangedMessage(
            new SettingsPendingChangesChanged(SettingsTabKey.Personalization, HasPendingChanges)));
    }

    private void RaiseAutoLockPendingProperties()
    {
        OnPropertyChanged(nameof(HasPendingAutoLockEnabledChange));
        OnPropertyChanged(nameof(HasPendingAutoLockIntervalChange));
    }

    private async Task<SettingsMaintenanceResult> SendMaintenanceRequestAsync(SettingsMaintenanceRequestType requestType,
        bool keepSettings)
    {
        var completionSource = new TaskCompletionSource<SettingsMaintenanceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _messenger.Send(new SettingsMaintenanceRequestedMessage(
            new SettingsMaintenanceRequest(requestType, keepSettings, completionSource)));

        var completedTask = await Task.WhenAny(completionSource.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        return completedTask == completionSource.Task
            ? await completionSource.Task
            : SettingsMaintenanceResult.Failure("Unable to run this settings action.");
    }
}

