using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Helpers.Settings;
using Fluxo.Services.Ui;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.ViewModels.Shell;

namespace Fluxo.ViewModels.Shell.Main;

public partial class AppLockState : ObservableObject
{
    private readonly IUiLockPasswordProtector _passwordProtector;
    private string _uiLockingPassword = string.Empty;

    [ObservableProperty] private bool _isAppAutoLocked;
    [ObservableProperty] private int _appAutoLockedInterval = AutoLockPreset.DefaultIntervalSeconds;
    [ObservableProperty] private bool _isAppLocked;

    public AppLockState(IUiLockPasswordProtector? passwordProtector = null)
    {
        _passwordProtector = passwordProtector ?? new DpapiUiLockPasswordProtector();
    }

    public bool HasUiLockingPassword => !string.IsNullOrEmpty(_uiLockingPassword);
    public string AppLockButtonText => IsAppLocked ? "Unlock fluxo" : "Lock fluxo";

    public void ApplySettings(IReadOnlyDictionary<string, string> settingsByName)
    {
        IsAppAutoLocked = SettingsShared.ParseBool(settingsByName, UserSettingNames.IsAppAutoLocked, false);
        AppAutoLockedInterval = SettingsShared.ParsePositiveInt(
            settingsByName,
            UserSettingNames.AppAutoLockedInterval,
            AutoLockPreset.DefaultIntervalSeconds);

        _uiLockingPassword = _passwordProtector.Unprotect(
            settingsByName.TryGetValue(UserSettingNames.UILockingPassword, out var protectedPassword)
                ? protectedPassword
                : string.Empty);
        OnPropertyChanged(nameof(HasUiLockingPassword));
    }

    public void LockUi()
    {
        if (IsAppLocked)
            return;

        IsAppLocked = true;
    }

    public bool TryUnlockUi(string? password)
    {
        if (!IsAppLocked)
            return true;

        if (!HasUiLockingPassword ||
            string.Equals(password ?? string.Empty, _uiLockingPassword, StringComparison.Ordinal))
        {
            IsAppLocked = false;
            return true;
        }

        return false;
    }

    partial void OnIsAppLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(AppLockButtonText));
    }
}
