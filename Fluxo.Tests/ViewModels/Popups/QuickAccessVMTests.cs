using System.Windows;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class QuickAccessVMTests
{
    [Fact]
    public void Catalog_UsesApprovedOrderAndThreeColumnRows()
    {
        RunInSta(() =>
        {
            var sut = CreateSut();

            Assert.Equal(
            [
                GlobalSearchFeatureTarget.NewTransaction,
                GlobalSearchFeatureTarget.ViewAccounts,
                GlobalSearchFeatureTarget.NewAccount,
                GlobalSearchFeatureTarget.NewRecurringTransaction,
                GlobalSearchFeatureTarget.NewSavingGoal,
                GlobalSearchFeatureTarget.NewTag,
                GlobalSearchFeatureTarget.PlanningReport,
                GlobalSearchFeatureTarget.BudgetForecast,
                GlobalSearchFeatureTarget.SearchEverything,
                GlobalSearchFeatureTarget.DataManagement,
                GlobalSearchFeatureTarget.LockApplication,
                GlobalSearchFeatureTarget.RunQuickSetup,
                GlobalSearchFeatureTarget.Hotkeys,
                GlobalSearchFeatureTarget.CheckForUpdates
            ],
                sut.Tiles.Select(tile => tile.Target));
            Assert.Equal([3, 3, 3, 3, 2], sut.TileRows.Select(row => row.Count));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("UnknownTarget,,NotAFeature")]
    public void LoadAsync_MissingOrUnrecognizedValuesKeepAllTilesEnabled(string? storedValue)
    {
        RunInSta(() =>
        {
            var setting = storedValue is null
                ? null
                : new UserSettings { Name = UserSettingNames.DisabledQuickAccessTiles, Value = storedValue };
            var sut = CreateSut(setting: setting);

            sut.LoadAsync().GetAwaiter().GetResult();

            Assert.All(sut.Tiles, tile => Assert.True(tile.IsUserEnabled));
            Assert.False(sut.HasPendingChanges);
        });
    }

    [Fact]
    public void LoadAsync_DisablesOnlyKnownStoredTargets()
    {
        RunInSta(() =>
        {
            var sut = CreateSut(setting: new UserSettings
            {
                Name = UserSettingNames.DisabledQuickAccessTiles,
                Value = "NewTransaction,RemovedFeature,Hotkeys"
            });

            sut.LoadAsync().GetAwaiter().GetResult();

            Assert.False(Tile(sut, GlobalSearchFeatureTarget.NewTransaction).IsUserEnabled);
            Assert.False(Tile(sut, GlobalSearchFeatureTarget.Hotkeys).IsUserEnabled);
            Assert.True(Tile(sut, GlobalSearchFeatureTarget.CheckForUpdates).IsUserEnabled);
            Assert.False(sut.HasPendingChanges);
        });
    }

    [Fact]
    public void Toggle_StagesChangesAndCanReturnToBaseline()
    {
        RunInSta(() =>
        {
            var sut = CreateSut();
            sut.LoadAsync().GetAwaiter().GetResult();
            var tile = Tile(sut, GlobalSearchFeatureTarget.NewTag);

            sut.BeginEditing();
            sut.Toggle(tile);

            Assert.True(sut.IsEditing);
            Assert.False(tile.IsUserEnabled);
            Assert.True(sut.HasPendingChanges);

            sut.Toggle(tile);

            Assert.True(tile.IsUserEnabled);
            Assert.False(sut.HasPendingChanges);
        });
    }

    [Fact]
    public void SaveAsync_PersistsDisabledTargetsInCatalogOrder()
    {
        RunInSta(() =>
        {
            UserSettings? addedSetting = null;
            var appData = CreateAppData();
            appData.AddUserSettingAsync(Arg.Do<UserSettings>(setting => addedSetting = setting),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var sut = new QuickAccessVM(appData);
            sut.LoadAsync().GetAwaiter().GetResult();
            sut.BeginEditing();
            sut.Toggle(Tile(sut, GlobalSearchFeatureTarget.ViewAccounts));
            sut.Toggle(Tile(sut, GlobalSearchFeatureTarget.NewTransaction));

            sut.SaveAsync().GetAwaiter().GetResult();

            Assert.NotNull(addedSetting);
            Assert.Equal(UserSettingNames.DisabledQuickAccessTiles, addedSetting.Name);
            Assert.Equal("NewTransaction,ViewAccounts", addedSetting.Value);
            Assert.False(sut.IsEditing);
            Assert.False(sut.HasPendingChanges);
            _ = appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_WhenUnchangedExitsEditingWithoutWriting()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            var sut = new QuickAccessVM(appData);
            sut.LoadAsync().GetAwaiter().GetResult();
            sut.BeginEditing();

            sut.SaveAsync().GetAwaiter().GetResult();

            Assert.False(sut.IsEditing);
            _ = appData.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void SaveAsync_WhenPersistenceFailsKeepsStagedEditingState()
    {
        RunInSta(() =>
        {
            var appData = CreateAppData();
            appData.SaveChangesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("save failed")));
            var sut = new QuickAccessVM(appData);
            sut.LoadAsync().GetAwaiter().GetResult();
            sut.BeginEditing();
            var tile = Tile(sut, GlobalSearchFeatureTarget.NewSavingGoal);
            sut.Toggle(tile);

            Assert.Throws<InvalidOperationException>(() => sut.SaveAsync().GetAwaiter().GetResult());
            Assert.True(sut.IsEditing);
            Assert.False(tile.IsUserEnabled);
            Assert.True(sut.HasPendingChanges);
        });
    }

    [Fact]
    public void SufficientFundsGate_DisablesOnlyGatedNormalActionsAndNotEditing()
    {
        RunInSta(() =>
        {
            var sut = CreateSut();
            var gated = Tile(sut, GlobalSearchFeatureTarget.NewRecurringTransaction);
            var ungated = Tile(sut, GlobalSearchFeatureTarget.NewAccount);

            sut.SetSufficientFundsGate(true);

            Assert.False(gated.IsOperationallyAvailable);
            Assert.False(gated.IsActionEnabled);
            Assert.Equal(0.4d, gated.PresentationOpacity);
            Assert.True(ungated.IsOperationallyAvailable);
            Assert.True(ungated.IsActionEnabled);

            sut.BeginEditing();

            Assert.True(gated.IsActionEnabled);
            Assert.Equal(1d, gated.PresentationOpacity);
        });
    }

    private static QuickAccessVM CreateSut(UserSettings? setting = null) =>
        new(CreateAppData(setting));

    private static IAppDataService CreateAppData(UserSettings? setting = null)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingByNameAsync(UserSettingNames.DisabledQuickAccessTiles,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(setting));
        appData.AddUserSettingAsync(Arg.Any<UserSettings>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return appData;
    }

    private static QuickAccessTileVM Tile(QuickAccessVM sut, GlobalSearchFeatureTarget target) =>
        Assert.Single(sut.Tiles, tile => tile.Target == target);

    private static void EnsureIconResources()
    {
        var application = Application.Current ?? new Application();
        if (application.Resources.MergedDictionaries.Any(dictionary =>
                dictionary.Source?.OriginalString.EndsWith(
                    "Resources/Icons.xaml",
                    StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Fluxo.Resources;component/Resources/Icons.xaml", UriKind.Relative)
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureIconResources();
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null)
            throw exception;
    }
}
