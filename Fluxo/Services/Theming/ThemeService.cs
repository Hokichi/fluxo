using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Theming;

namespace Fluxo.Services.Theming;

public sealed class ThemeService
{
    private readonly IAppDataService _appData;
    private readonly Action<ApplicationTheme> _applyTheme;
    private readonly Func<ApplicationTheme> _getCurrentTheme;

    public ThemeService(IAppDataService appData)
        : this(appData, ThemeManager.SwitchTheme, () => ThemeManager.CurrentTheme)
    {
    }

    internal ThemeService(
        IAppDataService appData,
        Action<ApplicationTheme> applyTheme,
        Func<ApplicationTheme> getCurrentTheme)
    {
        _appData = appData;
        _applyTheme = applyTheme;
        _getCurrentTheme = getCurrentTheme;
    }

    public ApplicationTheme CurrentTheme => _getCurrentTheme();

    public async Task<ApplicationTheme> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _appData
            .GetUserSettingByNameAsync(UserSettingNames.CurrentTheme, cancellationToken);
        var theme = ParseTheme(setting?.Value);

        _applyTheme(theme);
        await PersistAsync(setting, theme, cancellationToken);
        return theme;
    }

    public async Task SwitchThemeAsync(
        ApplicationTheme theme,
        CancellationToken cancellationToken = default)
    {
        var previousTheme = CurrentTheme;
        UserSettings? setting = null;
        string? previousValue = null;

        _applyTheme(theme);

        try
        {
            setting = await _appData
                .GetUserSettingByNameAsync(UserSettingNames.CurrentTheme, cancellationToken);
            previousValue = setting?.Value;
            await PersistAsync(setting, theme, cancellationToken);
        }
        catch
        {
            if (setting is not null && previousValue is not null)
                setting.Value = previousValue;

            _applyTheme(previousTheme);
            throw;
        }
    }

    private static ApplicationTheme ParseTheme(string? value)
    {
        return Enum.TryParse<ApplicationTheme>(value, true, out var theme) &&
               Enum.IsDefined(theme)
            ? theme
            : ApplicationTheme.Dark;
    }

    private async Task PersistAsync(
        UserSettings? setting,
        ApplicationTheme theme,
        CancellationToken cancellationToken)
    {
        var value = theme.ToString();

        if (setting is null)
        {
            await _appData.AddUserSettingAsync(
                    new UserSettings { Name = UserSettingNames.CurrentTheme, Value = value },
                    cancellationToken);
        }
        else
        {
            if (string.Equals(setting.Value, value, StringComparison.Ordinal))
                return;

            setting.Value = value;
            _appData.UpdateUserSetting(setting);
        }

        await _appData.SaveChangesAsync(cancellationToken);
    }
}
