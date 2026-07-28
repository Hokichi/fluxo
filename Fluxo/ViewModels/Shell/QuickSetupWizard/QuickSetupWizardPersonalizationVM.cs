using System.Globalization;
using Fluxo.Helpers.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.ViewModels.Shell;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardPersonalizationVM : ObservableObject
{
    private readonly IAppDataService _appData;
    private readonly IUiLockPasswordProtector _passwordProtector;

    [ObservableProperty] private bool _isStep6Active;
    [ObservableProperty] private bool _isAppAutoLocked;
    [ObservableProperty] private int _appAutoLockedInterval = AutoLockPreset.DefaultIntervalSeconds;
    [ObservableProperty] private string _selectedAppAutoLockPreset = AutoLockPreset.Seconds30;
    [ObservableProperty] private string _uiLockingPassword = string.Empty;
    [ObservableProperty] private bool _isUiLockingPasswordVisible;

    public QuickSetupWizardPersonalizationVM(
        IAppDataService appData,
        IUiLockPasswordProtector passwordProtector)
    {
        _appData = appData;
        _passwordProtector = passwordProtector;
    }

    public bool HasAutoLockInterval => IsAppAutoLocked;

    public bool IsCustomAutoLockInterval => AutoLockPreset.IsCustom(SelectedAppAutoLockPreset);

    public async Task LoadAsync()
    {
        var settings = await _appData.GetUserSettingsAsync();
        var settingsByName = settings.ToDictionary(setting => setting.Name, setting => setting.Value, StringComparer.Ordinal);

        IsAppAutoLocked = QuickSetupWizardShared.ParseBool(settingsByName, UserSettingNames.IsAppAutoLocked, false);
        AppAutoLockedInterval = QuickSetupWizardShared.ParsePositiveInt(
            settingsByName,
            UserSettingNames.AppAutoLockedInterval,
            AutoLockPreset.DefaultIntervalSeconds);
        SelectedAppAutoLockPreset = AutoLockPreset.FromIntervalSeconds(AppAutoLockedInterval);
        UiLockingPassword = _passwordProtector.Unprotect(
            QuickSetupWizardShared.ParseString(settingsByName, UserSettingNames.UILockingPassword, string.Empty));
    }

    public async Task<SettingsOperationResult> SaveAsync()
    {
        await ApplyAsync(_appData);
        await _appData.SaveChangesAsync();
        return SettingsOperationResult.Success();
    }

    public async Task ApplyAsync(IAppDataService appData)
    {
        await QuickSetupWizardShared.UpsertUserSettingAsync(
            appData,
            UserSettingNames.IsAppAutoLocked,
            IsAppAutoLocked.ToString());
        await QuickSetupWizardShared.UpsertUserSettingAsync(
            appData,
            UserSettingNames.AppAutoLockedInterval,
            AppAutoLockedInterval.ToString(CultureInfo.InvariantCulture));

        var protectedPassword = _passwordProtector.Protect(UiLockingPassword);
        await QuickSetupWizardShared.UpsertUserSettingAsync(
            appData,
            UserSettingNames.UILockingPassword,
            string.IsNullOrWhiteSpace(protectedPassword) ? null : protectedPassword);
    }

    partial void OnIsAppAutoLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAutoLockInterval));
    }

    partial void OnAppAutoLockedIntervalChanged(int value)
    {
        if (value <= 0)
        {
            AppAutoLockedInterval = 1;
            return;
        }

        var preset = AutoLockPreset.FromIntervalSeconds(value);
        if (!string.Equals(SelectedAppAutoLockPreset, preset, StringComparison.Ordinal))
            SelectedAppAutoLockPreset = preset;
    }

    partial void OnSelectedAppAutoLockPresetChanged(string value)
    {
        if (AutoLockPreset.TryGetSeconds(value, out var seconds) &&
            AppAutoLockedInterval != seconds)
        {
            AppAutoLockedInterval = seconds;
        }

        OnPropertyChanged(nameof(IsCustomAutoLockInterval));
    }
}
