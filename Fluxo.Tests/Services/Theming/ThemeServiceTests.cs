using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Theming;
using Fluxo.Services.Theming;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Theming;

public sealed class ThemeServiceTests
{
    [Fact]
    public async Task RestoreAsync_MissingPreference_AppliesAndPersistsDark()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingByNameAsync(UserSettingNames.CurrentTheme, Arg.Any<CancellationToken>())
            .Returns((UserSettings?)null);
        var appliedThemes = new List<ApplicationTheme>();
        var service = CreateService(appData, appliedThemes, ApplicationTheme.Dark);

        await service.RestoreAsync();

        Assert.Equal([ApplicationTheme.Dark], appliedThemes);
        await appData.Received(1).AddUserSettingAsync(
            Arg.Is<UserSettings>(setting =>
                setting.Name == UserSettingNames.CurrentTheme && setting.Value == nameof(ApplicationTheme.Dark)),
            Arg.Any<CancellationToken>());
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_ValidPreference_AppliesStoredThemeWithoutRewritingIt()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingByNameAsync(UserSettingNames.CurrentTheme, Arg.Any<CancellationToken>())
            .Returns(new UserSettings { Name = UserSettingNames.CurrentTheme, Value = nameof(ApplicationTheme.Light) });
        var appliedThemes = new List<ApplicationTheme>();
        var service = CreateService(appData, appliedThemes, ApplicationTheme.Dark);

        await service.RestoreAsync();

        Assert.Equal([ApplicationTheme.Light], appliedThemes);
        await appData.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_InvalidPreference_FallsBackToDarkAndRepairsStoredValue()
    {
        var setting = new UserSettings { Name = UserSettingNames.CurrentTheme, Value = "unknown" };
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingByNameAsync(UserSettingNames.CurrentTheme, Arg.Any<CancellationToken>())
            .Returns(setting);
        var appliedThemes = new List<ApplicationTheme>();
        var service = CreateService(appData, appliedThemes, ApplicationTheme.Light);

        await service.RestoreAsync();

        Assert.Equal([ApplicationTheme.Dark], appliedThemes);
        Assert.Equal(nameof(ApplicationTheme.Dark), setting.Value);
        appData.Received(1).UpdateUserSetting(setting);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchThemeAsync_PersistenceFailure_RestoresPreviousTheme()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingByNameAsync(UserSettingNames.CurrentTheme, Arg.Any<CancellationToken>())
            .Returns(new UserSettings { Name = UserSettingNames.CurrentTheme, Value = nameof(ApplicationTheme.Dark) });
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("save failed")));
        var appliedThemes = new List<ApplicationTheme>();
        var service = CreateService(appData, appliedThemes, ApplicationTheme.Dark);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SwitchThemeAsync(ApplicationTheme.Light));

        Assert.Equal([ApplicationTheme.Light, ApplicationTheme.Dark], appliedThemes);
    }

    private static ThemeService CreateService(
        IAppDataService appData,
        List<ApplicationTheme> appliedThemes,
        ApplicationTheme initialTheme)
    {
        var currentTheme = initialTheme;
        return new ThemeService(
            appData,
            theme =>
            {
                currentTheme = theme;
                appliedThemes.Add(theme);
            },
            () => currentTheme);
    }
}
