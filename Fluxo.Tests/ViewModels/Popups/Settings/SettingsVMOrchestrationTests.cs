using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
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
            new SettingsGoalsTabVM(mainViewModel, appData, messenger),
            new SettingsIoUsTabVM(mainViewModel, appData, messenger),
            new SettingsTagsTabVM(mainViewModel, appData, messenger),
            new SettingsPersonalizationTabVM(appData, messenger),
            messenger);

        return (settings, mainViewModel);
    }

    private static MainVM CreateMainViewModel(IMessenger messenger, IUnitOfWork unitOfWork)
    {
        var mapper = Substitute.For<IMapper>();
        var dataOperationRunner = new InlineDataOperationRunner(unitOfWork);

        var dashboard = new DashboardVM(
            new NotificationPanelVM(
                Substitute.For<ITransactionService>(),
                Substitute.For<IAccountService>(),
                dataOperationRunner,
                mapper,
                messenger: messenger),
            new BudgetAllocationPanelVM(
                Substitute.For<ITransactionService>(),
                Substitute.For<IAccountService>(),
                Substitute.For<ITagService>(),
                dataOperationRunner,
                mapper,
                messenger),
            new SpentAllowancePanelVM(
                Substitute.For<ITransactionService>(),
                Substitute.For<IAccountService>(),
                dataOperationRunner,
                mapper,
                messenger),
            new SavingGoalsPanelVM(dataOperationRunner, mapper, messenger),
            new UpcomingEventsPanelVM(dataOperationRunner, mapper, messenger: messenger),
            new MainViewModeToggleVM(messenger));

        return new MainVM(
            dataOperationRunner,
            dashboard,
            new DaySpinnerVM(messenger),
            null);
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

        public Task RevertAsync(Fluxo.Core.Interfaces.IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReapplyAsync(Fluxo.Core.Interfaces.IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
