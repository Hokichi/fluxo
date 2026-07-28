using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Helpers.Settings;
using Fluxo.Services.Ui;

namespace Fluxo.Helpers.MainWindow;

internal sealed class AppLockHelper
{
    private readonly IUiLockPasswordProtector _passwordProtector;
    private string _uiLockingPassword = string.Empty;

    public AppLockHelper(IUiLockPasswordProtector? passwordProtector = null)
    {
        _passwordProtector = passwordProtector ?? new DpapiUiLockPasswordProtector();
    }

    public bool HasUiLockingPassword => !string.IsNullOrEmpty(_uiLockingPassword);

    public (bool IsAppAutoLocked, int AppAutoLockedInterval) ApplySettings(
        IReadOnlyDictionary<string, string> settingsByName)
    {
        _uiLockingPassword = _passwordProtector.Unprotect(
            settingsByName.TryGetValue(UserSettingNames.UILockingPassword, out var protectedPassword)
                ? protectedPassword
                : string.Empty);

        return (
            SettingsShared.ParseBool(settingsByName, UserSettingNames.IsAppAutoLocked, false),
            SettingsShared.ParsePositiveInt(
                settingsByName,
                UserSettingNames.AppAutoLockedInterval,
                AutoLockPreset.DefaultIntervalSeconds));
    }

    public bool CanUnlock(string? password) =>
        !HasUiLockingPassword ||
        string.Equals(password ?? string.Empty, _uiLockingPassword, StringComparison.Ordinal);
}
