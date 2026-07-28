using Fluxo.Core.Constants;
using Fluxo.Helpers.Settings;
using Fluxo.ViewModels.Shell.Main;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class MainVMAppLockTests
{
    [Fact]
    public async Task ApplySettings_LoadsAppLockSettings()
    {
        var mainViewModel = CreateMainViewModel(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserSettingNames.IsAppAutoLocked] = bool.TrueString,
            [UserSettingNames.AppAutoLockedInterval] = "120",
            [UserSettingNames.UILockingPassword] = "protected:secret-pass"
        });
        await mainViewModel.ReloadUserSettingsAsync();

        Assert.True(mainViewModel.IsAppAutoLocked);
        Assert.Equal(120, mainViewModel.AppAutoLockedInterval);
        Assert.True(mainViewModel.HasUiLockingPassword);
    }

    [Fact]
    public async Task ApplySettings_UsesThirtySecondDefault_WhenIntervalMissing()
    {
        var mainViewModel = CreateMainViewModel(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserSettingNames.IsAppAutoLocked] = bool.TrueString
        });
        await mainViewModel.ReloadUserSettingsAsync();

        Assert.True(mainViewModel.IsAppAutoLocked);
        Assert.Equal(30, mainViewModel.AppAutoLockedInterval);
    }

    [Theory]
    [InlineData(30, "30")]
    [InlineData(60, "60")]
    [InlineData(180, "180")]
    [InlineData(300, "300")]
    [InlineData(600, "600")]
    [InlineData(45, "Custom")]
    public void AutoLockPreset_FromIntervalSeconds_MapsKnownValuesAndCustomValues(
        int seconds,
        string expectedPreset)
    {
        Assert.Equal(expectedPreset, AutoLockPreset.FromIntervalSeconds(seconds));
    }

    [Theory]
    [InlineData("30", true, 30)]
    [InlineData("60", true, 60)]
    [InlineData("180", true, 180)]
    [InlineData("300", true, 300)]
    [InlineData("600", true, 600)]
    [InlineData("Custom", false, 0)]
    public void AutoLockPreset_TryGetSeconds_MapsFixedPresetValues(
        string preset,
        bool expectedResult,
        int expectedSeconds)
    {
        var result = AutoLockPreset.TryGetSeconds(preset, out var seconds);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedSeconds, seconds);
    }

    [Fact]
    public void LockUi_SetsLockedState()
    {
        var mainViewModel = CreateMainViewModel();

        mainViewModel.LockUi();

        Assert.True(mainViewModel.IsAppLocked);
    }

    [Fact]
    public void TryUnlockUi_ReturnsTrue_WhenNoPasswordSaved()
    {
        var mainViewModel = CreateMainViewModel();
        mainViewModel.LockUi();

        var unlocked = mainViewModel.TryUnlockUi(null);

        Assert.True(unlocked);
        Assert.False(mainViewModel.IsAppLocked);
    }

    [Fact]
    public async Task TryUnlockUi_ReturnsFalse_WhenPasswordMismatch()
    {
        var mainViewModel = CreateMainViewModel(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserSettingNames.UILockingPassword] = "protected:secret-pass"
        });
        await mainViewModel.ReloadUserSettingsAsync();
        mainViewModel.LockUi();

        var unlocked = mainViewModel.TryUnlockUi("wrong");

        Assert.False(unlocked);
        Assert.True(mainViewModel.IsAppLocked);
    }

    [Fact]
    public async Task TryUnlockUi_ReturnsTrue_WhenPasswordMatches()
    {
        var mainViewModel = CreateMainViewModel(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserSettingNames.UILockingPassword] = "protected:secret-pass"
        });
        await mainViewModel.ReloadUserSettingsAsync();
        mainViewModel.LockUi();

        var unlocked = mainViewModel.TryUnlockUi("secret-pass");

        Assert.True(unlocked);
        Assert.False(mainViewModel.IsAppLocked);
    }

    private static MainVM CreateMainViewModel(Dictionary<string, string>? settings = null) =>
        MainVMUserSettingsTests.CreateMainViewModel(
            new MainVMUserSettingsTests.TestUserSettingsUnitOfWork(
                settings ?? new Dictionary<string, string>(StringComparer.Ordinal)));
}
