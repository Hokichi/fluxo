using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Persistence;
using Fluxo.Services.Ui;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Settings;

public sealed class SettingsVMOrchestrationTests
{
    [Fact]
    public async Task ApplyConfigurationAsync_WhenSaveFails_RevertsPendingSettings()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("save failed")));
        var settings = CreateApplySettingsViewModel(null!, appData, messenger);
        await settings.BudgetTab.LoadAsync();
        await settings.PersonalizationTab.LoadAsync();
        settings.PersonalizationTab.ShouldRunAtStartup = true;

        var result = await settings.ApplyConfigurationAsync();

        Assert.False(result.IsSuccess);
        Assert.False(settings.PersonalizationTab.ShouldRunAtStartup);
        Assert.False(settings.HasPendingConfigurationChanges);
    }

    [Fact]
    public async Task ApplyConfigurationAsync_WhenStagingFails_DiscardsBatchAndRevertsPendingSettings()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new BudgetAllocation()),
            Task.FromException<BudgetAllocation>(new InvalidOperationException("staging failed")));
        var settings = CreateApplySettingsViewModel(null!, appData, messenger);
        await settings.BudgetTab.LoadAsync();
        await settings.PersonalizationTab.LoadAsync();
        settings.PersonalizationTab.ShouldRunAtStartup = true;

        var result = await settings.ApplyConfigurationAsync();

        Assert.False(result.IsSuccess);
        Assert.False(settings.PersonalizationTab.ShouldRunAtStartup);
        Assert.False(settings.HasPendingConfigurationChanges);
        appData.Received(1).DiscardPendingChanges();
    }

    [Fact]
    public async Task ApplyConfigurationAsync_WhenRefreshFailsAfterSave_KeepsCommittedSettings()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        var settingsReadCount = 0;
        appData.GetUserSettingsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            settingsReadCount++ == 0
                ? Task.FromResult<IReadOnlyList<UserSettings>>([])
                : Task.FromException<IReadOnlyList<UserSettings>>(
                    new InvalidOperationException("refresh failed")));
        var mainViewModel = CreateMainViewModel(messenger, appData);
        var settings = CreateApplySettingsViewModel(mainViewModel, appData, messenger);
        await settings.BudgetTab.LoadAsync();
        await settings.PersonalizationTab.LoadAsync();
        settings.PersonalizationTab.ShouldRunAtStartup = true;

        var result = await settings.ApplyConfigurationAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(settings.PersonalizationTab.ShouldRunAtStartup);
        Assert.False(settings.HasPendingConfigurationChanges);
    }

    [Fact]
    public async Task ApplyConfigurationAsync_WhenSettingChangesDuringSave_CommitsOnlyPersistedRevision()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        var saveCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(saveCompletion.Task);
        var startupRegistration = Substitute.For<IStartupRegistrationService>();
        var settings = CreateApplySettingsViewModel(null!, appData, messenger, startupRegistration);
        await settings.BudgetTab.LoadAsync();
        await settings.PersonalizationTab.LoadAsync();
        settings.PersonalizationTab.ShouldRunAtStartup = true;

        var saveTask = settings.ApplyConfigurationAsync();
        _ = appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        settings.PersonalizationTab.ShouldRunAtStartup = false;
        saveCompletion.SetResult();

        var result = await saveTask;

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(settings.HasPendingConfigurationChanges);
        startupRegistration.Received(1).SetRunAtStartup(true);
    }

    [Fact]
    public async Task ResetAllSettingsAsync_WhenSaveFails_DoesNotChangeStartupRegistration()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("save failed")));
        var startupRegistration = Substitute.For<IStartupRegistrationService>();
        var settings = CreateApplySettingsViewModel(null!, appData, messenger, startupRegistration);

        var result = await settings.ResetAllSettingsAsync();

        Assert.False(result.IsSuccess);
        startupRegistration.DidNotReceive().SetRunAtStartup(Arg.Any<bool>());
    }

    [Fact]
    public async Task ResetAllSettingsAsync_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        var settings = CreateApplySettingsViewModel(null!, appData, messenger);

        var result = await settings.ResetAllSettingsAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteAllDataAsync_WhenSaveFails_DoesNotChangeStartupRegistration()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        ConfigureEmptyDeleteData(appData);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("save failed")));
        var startupRegistration = Substitute.For<IStartupRegistrationService>();
        var settings = CreateApplySettingsViewModel(null!, appData, messenger, startupRegistration);

        var result = await settings.DeleteAllDataAsync(keepSettings: false);

        Assert.False(result.IsSuccess);
        startupRegistration.DidNotReceive().SetRunAtStartup(Arg.Any<bool>());
    }

    [Fact]
    public async Task DeleteAllDataAsync_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateSettingsAppData();
        ConfigureEmptyDeleteData(appData);
        var settings = CreateApplySettingsViewModel(null!, appData, messenger);

        var result = await settings.DeleteAllDataAsync(keepSettings: true);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public void SettingsVMOrchestration_MessageContracts_AreAccessible()
    {
        var operation = new SettingsOperationCorrelation(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var loadRequested = new SettingsLoadRequestedMessage(operation);
        var tabLoaded = new SettingsTabLoadedMessage(new SettingsTabLoaded(
            operation,
            SettingsTabKey.Budget,
            IsSuccess: true,
            ErrorMessage: null));
        var pendingChanged = new SettingsPendingChangesChangedMessage(new SettingsPendingChangesChanged(
            SettingsTabKey.Tags,
            HasPendingChanges: true));
        var applyRequested = new SettingsApplyRequestedMessage(operation);
        var contribution = new SettingsApplyContributionMessage(new SettingsApplyContribution(
            operation,
            SettingsTabKey.Personalization,
            IsSuccess: true,
            ErrorMessage: null,
            SettingChanges:
            [
                new SettingsSettingChange("PreferredAppName", "Fluxo", "Fluxo Pro")
            ],
            MemoryActions:
            [
                new TestLogMemoryAction("Rename app")
            ],
            UsernameChange: new SettingsUsernameChange("Fluxo", "Fluxo Pro")));
        var revertRequested = new SettingsRevertRequestedMessage(operation);
        var dataChanged = new SettingsDataChangedMessage(SettingsDataChangedScope.Accounts | SettingsDataChangedScope.Tags);

        Assert.Equal(operation, loadRequested.Value);
        Assert.Equal(operation, tabLoaded.Value.Operation);
        Assert.Equal(SettingsTabKey.Budget, tabLoaded.Value.TabKey);
        Assert.True(tabLoaded.Value.IsSuccess);
        Assert.Equal(SettingsTabKey.Tags, pendingChanged.Value.TabKey);
        Assert.True(pendingChanged.Value.HasPendingChanges);
        Assert.Equal(operation, applyRequested.Value);
        Assert.Equal(SettingsTabKey.Personalization, contribution.Value.TabKey);
        Assert.Single(contribution.Value.SettingChanges);
        Assert.Single(contribution.Value.MemoryActions);
        Assert.Equal("Fluxo Pro", contribution.Value.UsernameChange?.CurrentValue);
        Assert.Equal(operation, revertRequested.Value);
        Assert.True(dataChanged.Value.HasFlag(SettingsDataChangedScope.Accounts));
        Assert.True(dataChanged.Value.HasFlag(SettingsDataChangedScope.Tags));
    }

    [Fact]
    public void SettingsVMOrchestration_AccountDataChanged_RefreshesSufficientFundsActionGateState()
    {
        RunInSta(() =>
        {
            var (settings, mainViewModel) = CreateSettingsViewModel();
            settings.RecurringTransactionsTab.IsDashboardSpendingAmountGateLocked = true;
            settings.GoalsTab.IsDashboardSpendingAmountGateLocked = true;

            var propertyChanges = new List<string?>();
            settings.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);

            Assert.True(settings.IsSufficientFundsActionGateLocked);

            mainViewModel.Dashboard.IsDashboardSpendingAmountGateLocked = false;
            mainViewModel.Dashboard.IsSufficientFundsActionGateLocked = false;
            settings.Receive(new SettingsDataChangedMessage(SettingsDataChangedScope.Accounts));

            Assert.False(settings.IsSufficientFundsActionGateLocked);
            Assert.False(settings.RecurringTransactionsTab.IsDashboardSpendingAmountGateLocked);
            Assert.False(settings.GoalsTab.IsDashboardSpendingAmountGateLocked);
            Assert.Contains(nameof(SettingsVM.IsSufficientFundsActionGateLocked), propertyChanges);
        });
    }

    private static (SettingsVM Settings, MainVM MainViewModel) CreateSettingsViewModel()
    {
        var messenger = new WeakReferenceMessenger();
        var unitOfWork = CreateUnitOfWork();
        var appData = new AppDataService(unitOfWork);
        var mainViewModel = CreateMainViewModel(messenger, unitOfWork);
        mainViewModel.Dashboard.IsDashboardSpendingAmountGateLocked = true;
        mainViewModel.Dashboard.IsSufficientFundsActionGateLocked = true;

        var settings = new SettingsVM(
            mainViewModel,
            appData,
            Substitute.For<IStartupRegistrationService>(),
            Substitute.For<IUiSettleAwaiter>(),
            new SettingsBudgetTabVM(() => mainViewModel.BudgetPanel.TotalIncomeAmount, appData, messenger),
            new SettingsAccountsTabVM(mainViewModel, appData, messenger),
            new SettingsRecurringTransactionsTabVM(appData, messenger),
            new SettingsGoalsTabVM(appData, messenger),
            new SettingsIoUsTabVM(appData, messenger),
            new SettingsTagsTabVM(appData, messenger),
            new SettingsPersonalizationTabVM(appData, messenger),
            messenger);

        return (settings, mainViewModel);
    }

    private static MainVM CreateMainViewModel(IMessenger messenger, IUnitOfWork unitOfWork)
    {
        var mapper = Substitute.For<IMapper>();
        var appData = new Fluxo.Services.Persistence.AppDataService(unitOfWork);

        return CreateMainViewModel(messenger, appData, mapper);
    }

    private static MainVM CreateMainViewModel(
        IMessenger messenger,
        IAppDataService appData,
        IMapper? mapper = null)
    {
        mapper ??= Substitute.For<IMapper>();

        var dashboard = new DashboardVM(
            new NotificationPanelVM(
                Substitute.For<ITransactionService>(),
                Substitute.For<IAccountService>(),
                appData,
                mapper,
                messenger: messenger),
            new BudgetAllocationPanelVM(
                Substitute.For<ITransactionService>(),
                Substitute.For<IAccountService>(),
                Substitute.For<ITagService>(),
                appData,
                mapper,
                messenger),
            new SpentAllowancePanelVM(
                Substitute.For<ITransactionService>(),
                Substitute.For<IAccountService>(),
                appData,
                mapper,
                messenger),
            new SavingGoalsPanelVM(appData, mapper, messenger),
            new UpcomingEventsPanelVM(appData, mapper, messenger: messenger),
            new MainViewModeToggleVM(messenger));

        return new MainVM(
            appData,
            dashboard,
            new DaySpinnerVM(messenger),
            null);
    }

    private static SettingsVM CreateApplySettingsViewModel(
        MainVM mainViewModel,
        IAppDataService appData,
        IMessenger messenger,
        IStartupRegistrationService? startupRegistration = null)
    {
        return new SettingsVM(
            mainViewModel,
            appData,
            startupRegistration ?? Substitute.For<IStartupRegistrationService>(),
            Substitute.For<IUiSettleAwaiter>(),
            new SettingsBudgetTabVM(() => 0m, appData, messenger),
            null!,
            null!,
            null!,
            null!,
            null!,
            new SettingsPersonalizationTabVM(
                appData,
                messenger,
                passwordProtector: new PassThroughUiLockPasswordProtector()),
            messenger);
    }

    private static IAppDataService CreateSettingsAppData()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation
        {
            NeedsThreshold = 50,
            WantsThreshold = 30,
            InvestThreshold = 20
        });
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>([]));
        appData.GetUserSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserSettings>>([]));
        appData.GetUserSettingByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UserSettings?>(null));
        appData.AddUserSettingAsync(Arg.Any<UserSettings>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return appData;
    }

    private static void ConfigureEmptyDeleteData(IAppDataService appData)
    {
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SavingGoal>>([]));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.GetRecurringTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringTransaction>>([]));
        appData.AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private static IUnitOfWork CreateUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var userSettings = Substitute.For<IUserSettingsRepository>();
        userSettings.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.Entities.UserSettings>>([]));
        unitOfWork.UserSettings.Returns(userSettings);

        var incomeLogs = Substitute.For<ITransactionRepository>();
        incomeLogs.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.Entities.Transaction>>([]));
        unitOfWork.Transactions.Returns(incomeLogs);

        return unitOfWork;
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
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

    private sealed class TestLogMemoryAction(string description) : ILogMemoryAction
    {
        public string Description { get; } = description;

        public Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

}
