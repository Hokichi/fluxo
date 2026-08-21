using Fluxo.Core.Constants;
using System.Globalization;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Persistence;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.QuickSetupWizard;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class QuickSetupWizardVMTests
{
    [Fact]
    public void QuickSetupWizardVM_CurrentStepTitle_AtStep1_AsksForPreferredName()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentStepIndex = 1;

        Assert.Equal("What should fluxo call you?", viewModel.NamePage.CurrentStepTitle);
    }

    [Fact]
    public void QuickSetupWizardVM_CurrentStepDescription_AtStep1_DoesNotMentionSalary()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentStepIndex = 1;

        Assert.DoesNotContain("salary", viewModel.NamePage.CurrentStepDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuickSetupWizardVM_GoNextAsync_OnStep1_DoesNotOverwriteExistingSalarySetting()
    {
        var userSettingsRepository = new TestUserSettingsRepository(
        [
            new UserSettings { Name = UserSettingNames.PreferredDisplayName, Value = "Existing Name" },
            new UserSettings { Name = UserSettingNames.Salary, Value = "6000" }
        ]);

        var unitOfWork = new TestUnitOfWork(userSettingsRepository);
        var viewModel = CreateViewModel(unitOfWork);
        viewModel.CurrentStepIndex = 1;
        viewModel.NamePage.UsernameText = "Alex";

        var result = await viewModel.GoNextAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, viewModel.CurrentStepIndex);
        Assert.Equal("6000", userSettingsRepository.GetValue(UserSettingNames.Salary));
        Assert.DoesNotContain(UserSettingNames.Salary, userSettingsRepository.AddedNames);
        Assert.DoesNotContain(UserSettingNames.Salary, userSettingsRepository.UpdatedNames);
        Assert.DoesNotContain(UserSettingNames.Salary, userSettingsRepository.RemovedNames);
    }

    [Fact]
    public async Task QuickSetupWizardVM_ExecuteLoadingFlowAsync_UserDeclinesAfterFiveFailures_ReturnsAbandoned()
    {
        var viewModel = CreateViewModel();
        var attempts = 0;
        var prompts = 0;

        var outcome = await viewModel.ExecuteLoadingFlowAsync(
            tryStageAsyncOverride: () =>
            {
                attempts++;
                return Task.FromResult(false);
            },
            confirmRetryCycleAsync: () =>
            {
                prompts++;
                return Task.FromResult(false);
            },
            delayAsync: _ => Task.CompletedTask);

        Assert.Equal(QuickSetupWizardLoadingOutcome.Abandoned, outcome);
        Assert.Equal(5, attempts);
        Assert.Equal(1, prompts);
    }

    [Fact]
    public void QuickSetupWizardVM_GoBack_FromPreferencesWithoutAccounts_ReturnsToAccounts()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentStepIndex = 6;
        viewModel.HasAccounts = false;

        viewModel.GoBack();

        Assert.Equal(2, viewModel.CurrentStepIndex);
    }

    [Fact]
    public async Task QuickSetupWizardVM_BudgetAllocation_LoadAndApply_PersistsConfigurationFields()
    {
        var unitOfWork = new TestUnitOfWork(
            new TestUserSettingsRepository([]),
            new TestBudgetAllocationRepository(new BudgetAllocation
            {
                NeedsThreshold = 45,
                WantsThreshold = 35,
                InvestThreshold = 20,
                AllocationLimit = 500m,
                AllocationPeriod = AllocationPeriod.Biweekly,
                RolloverPolicy = RolloverPolicy.Matching,
                OverspendPolicy = OverspendPolicy.SoftDebt
            }));
        var appData = new AppDataService(unitOfWork);
        var viewModel = new QuickSetupWizardBudgetAllocationVM(appData, new WeakReferenceMessenger());

        await viewModel.LoadAsync();

        Assert.Equal(45, viewModel.NeedsAllocationPercentage);
        Assert.Equal(35, viewModel.WantsAllocationPercentage);
        Assert.Equal(20, viewModel.InvestAllocationPercentage);
        Assert.Equal(500m, viewModel.AllocationLimit);
        Assert.Equal(AllocationPeriod.Biweekly, viewModel.AllocationPeriod);
        Assert.Equal(RolloverPolicy.Matching, viewModel.RolloverPolicy);
        Assert.Equal(OverspendPolicy.SoftDebt, viewModel.OverspendPolicy);

        viewModel.AllocationLimit = 1200m;
        viewModel.AllocationPeriod = AllocationPeriod.Monthly;
        viewModel.RolloverPolicy = RolloverPolicy.Pooled;
        viewModel.OverspendPolicy = OverspendPolicy.HardStop;

        var result = await viewModel.ApplyAsync(appData);

        Assert.True(result.IsSuccess);
        Assert.Equal(45, unitOfWork.BudgetAllocationEntity!.NeedsThreshold);
        Assert.Equal(35, unitOfWork.BudgetAllocationEntity.WantsThreshold);
        Assert.Equal(20, unitOfWork.BudgetAllocationEntity.InvestThreshold);
        Assert.Equal(1200m, unitOfWork.BudgetAllocationEntity.AllocationLimit);
        Assert.Equal(AllocationPeriod.Monthly, unitOfWork.BudgetAllocationEntity.AllocationPeriod);
        Assert.Equal(RolloverPolicy.Pooled, unitOfWork.BudgetAllocationEntity.RolloverPolicy);
        Assert.Equal(OverspendPolicy.HardStop, unitOfWork.BudgetAllocationEntity.OverspendPolicy);
    }

    [Fact]
    public async Task QuickSetupWizardVM_BudgetAllocation_AllocationAmountText_UsesAllocationLimit()
    {
        var unitOfWork = new TestUnitOfWork(
            new TestUserSettingsRepository([]),
            new TestBudgetAllocationRepository(new BudgetAllocation
            {
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                AllocationLimit = 2000m
            }));
        var viewModel = new QuickSetupWizardBudgetAllocationVM(
            new AppDataService(unitOfWork),
            new WeakReferenceMessenger());
        viewModel.Receive(new QuickSetupWizardAccountsChangedMessage(
            new QuickSetupWizardAccountsChanged(1, true, 1000m)));

        await viewModel.LoadAsync();

        Assert.Equal(1000m.ToString("N2", CultureInfo.CurrentCulture), viewModel.NeedsAllocationAmountText);
        Assert.Equal(600m.ToString("N2", CultureInfo.CurrentCulture), viewModel.WantsAllocationAmountText);
        Assert.Equal(400m.ToString("N2", CultureInfo.CurrentCulture), viewModel.InvestAllocationAmountText);

        viewModel.AllocationLimit = 3000m;

        Assert.Equal(1500m.ToString("N2", CultureInfo.CurrentCulture), viewModel.NeedsAllocationAmountText);
        Assert.Equal(900m.ToString("N2", CultureInfo.CurrentCulture), viewModel.WantsAllocationAmountText);
        Assert.Equal(600m.ToString("N2", CultureInfo.CurrentCulture), viewModel.InvestAllocationAmountText);
    }

    [Fact]
    public async Task QuickSetupWizardVM_BudgetAllocation_ApplyAsyncInvalidTotal_ReturnsFailureWithoutPersisting()
    {
        var allocation = new BudgetAllocation
        {
            NeedsThreshold = 60,
            WantsThreshold = 30,
            InvestThreshold = 20,
            AllocationLimit = 500m,
            AllocationPeriod = AllocationPeriod.Biweekly,
            RolloverPolicy = RolloverPolicy.Matching,
            OverspendPolicy = OverspendPolicy.SoftDebt
        };
        var unitOfWork = new TestUnitOfWork(
            new TestUserSettingsRepository([]),
            new TestBudgetAllocationRepository(allocation));
        var appData = new AppDataService(unitOfWork);
        var viewModel = new QuickSetupWizardBudgetAllocationVM(appData, new WeakReferenceMessenger());

        await viewModel.LoadAsync();
        viewModel.AllocationLimit = 1200m;
        viewModel.AllocationPeriod = AllocationPeriod.Monthly;
        viewModel.RolloverPolicy = RolloverPolicy.Pooled;
        viewModel.OverspendPolicy = OverspendPolicy.HardStop;

        var result = await viewModel.ApplyAsync(appData);

        Assert.False(result.IsSuccess);
        Assert.Equal("Needs, Wants, and Invest must add up to 100%. Current total: 110%", result.ErrorMessage);
        Assert.Equal(60, unitOfWork.BudgetAllocationEntity!.NeedsThreshold);
        Assert.Equal(30, unitOfWork.BudgetAllocationEntity.WantsThreshold);
        Assert.Equal(20, unitOfWork.BudgetAllocationEntity.InvestThreshold);
        Assert.Equal(500m, unitOfWork.BudgetAllocationEntity.AllocationLimit);
        Assert.Equal(AllocationPeriod.Biweekly, unitOfWork.BudgetAllocationEntity.AllocationPeriod);
        Assert.Equal(RolloverPolicy.Matching, unitOfWork.BudgetAllocationEntity.RolloverPolicy);
        Assert.Equal(OverspendPolicy.SoftDebt, unitOfWork.BudgetAllocationEntity.OverspendPolicy);
    }

    [Fact]
    public async Task QuickSetupWizardVM_BudgetAllocation_SettingAllocation_BalancesOtherBuckets()
    {
        var unitOfWork = new TestUnitOfWork(new TestUserSettingsRepository([]));
        var viewModel = new QuickSetupWizardBudgetAllocationVM(
            new AppDataService(unitOfWork),
            new WeakReferenceMessenger());

        await viewModel.LoadAsync();
        viewModel.IncrementAllocation(BudgetAllocationSegment.Invest, 1);

        Assert.Equal(50, viewModel.NeedsAllocationPercentage);
        Assert.Equal(29, viewModel.WantsAllocationPercentage);
        Assert.Equal(21, viewModel.InvestAllocationPercentage);
        Assert.False(viewModel.HasBudgetAllocationError);
    }

    [Fact]
    public async Task QuickSetupWizardVM_Personalization_LoadAsync_DefaultsAutoLockIntervalToThirtySecondPreset()
    {
        var unitOfWork = new TestUnitOfWork(new TestUserSettingsRepository([]));
        var appData = new AppDataService(unitOfWork);
        var vm = new QuickSetupWizardPersonalizationVM(appData, new TestPasswordProtector());

        await vm.LoadAsync();

        Assert.Equal(30, vm.AppAutoLockedInterval);
        Assert.Equal("30", vm.SelectedAppAutoLockPreset);
        Assert.False(vm.IsCustomAutoLockInterval);
    }

    [Fact]
    public void QuickSetupWizardVM_Personalization_SelectingFixedAutoLockPreset_UpdatesInterval()
    {
        var unitOfWork = new TestUnitOfWork(new TestUserSettingsRepository([]));
        var appData = new AppDataService(unitOfWork);
        var vm = new QuickSetupWizardPersonalizationVM(appData, new TestPasswordProtector());

        vm.SelectedAppAutoLockPreset = "300";

        Assert.Equal(300, vm.AppAutoLockedInterval);
        Assert.False(vm.IsCustomAutoLockInterval);
    }

    [Fact]
    public async Task QuickSetupWizardVM_Personalization_LoadAsync_SelectsCustomPresetForNonPresetInterval()
    {
        var unitOfWork = new TestUnitOfWork(new TestUserSettingsRepository(
        [
            new UserSettings { Name = UserSettingNames.AppAutoLockedInterval, Value = "45" }
        ]));
        var appData = new AppDataService(unitOfWork);
        var vm = new QuickSetupWizardPersonalizationVM(appData, new TestPasswordProtector());

        await vm.LoadAsync();

        Assert.Equal(45, vm.AppAutoLockedInterval);
        Assert.Equal("Custom", vm.SelectedAppAutoLockPreset);
        Assert.True(vm.IsCustomAutoLockInterval);
    }

    private static QuickSetupWizardVM CreateViewModel(TestUnitOfWork? unitOfWork = null)
    {
        unitOfWork ??= new TestUnitOfWork(new TestUserSettingsRepository([]));
        var appData = new AppDataService(unitOfWork);
        var messenger = new WeakReferenceMessenger();
        var greeting = new QuickSetupWizardGreetingPageVM();
        var name = new QuickSetupWizardNamePageVM(appData, messenger);
        var accounts = new QuickSetupWizardAccountsVM(null!, appData, messenger);
        var fixedExpenses = new QuickSetupWizardRecurringTransactionsVM(appData, messenger);
        var savingGoals = new QuickSetupWizardSavingGoalsVM(null!, appData, messenger);
        var budget = new QuickSetupWizardBudgetAllocationVM(appData, messenger);
        var personalization = new QuickSetupWizardPersonalizationVM(appData, new TestPasswordProtector());
        var notification = new QuickSetupWizardNotificationVM(appData, messenger);
        var summary = new QuickSetupWizardSummaryVM(messenger);
        var middle = new QuickSetupWizardMiddlePageVM(
            accounts,
            fixedExpenses,
            savingGoals,
            budget,
            personalization,
            notification,
            summary,
            messenger);
        var loading = new QuickSetupWizardLoadingPageVM();
        var final = new QuickSetupWizardFinalPageVM(messenger);
        return new QuickSetupWizardVM(
            null!,
            appData,
            Substitute.For<IStartupRegistrationService>(),
            new TestDataOperationScopeFactory(),
            null!,
            greeting,
            name,
            middle,
            loading,
            final,
            messenger);
    }

    private sealed class TestDataOperationScopeFactory : IDataOperationScopeFactory
    {
        public ValueTask<IDataOperationScope> CreateAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestPasswordProtector : IUiLockPasswordProtector
    {
        public string Protect(string? password)
        {
            return string.IsNullOrWhiteSpace(password) ? string.Empty : "protected:" + password;
        }

        public string Unprotect(string? protectedPassword)
        {
            if (string.IsNullOrWhiteSpace(protectedPassword))
                return string.Empty;

            return protectedPassword.StartsWith("protected:", StringComparison.Ordinal)
                ? protectedPassword["protected:".Length..]
                : string.Empty;
        }
    }

    private sealed class TestUnitOfWork(
        TestUserSettingsRepository userSettingsRepository,
        TestBudgetAllocationRepository? budgetAllocationRepository = null) : IUnitOfWork
    {
        private readonly TestBudgetAllocationRepository _budgetAllocationRepository =
            budgetAllocationRepository ?? new TestBudgetAllocationRepository();

        public ITransactionRepository Expenses => throw new NotSupportedException();
        public ITransactionRepository Transactions => throw new NotSupportedException();
        public ITagRepository Tags => throw new NotSupportedException();
        public ISavingGoalRepository SavingGoals => throw new NotSupportedException();
        public IAccountRepository Accounts => throw new NotSupportedException();
        public IRecurringTransactionRepository RecurringTransactions => throw new NotSupportedException();
        public IUserSettingsRepository UserSettings => userSettingsRepository;
        public IBudgetAllocationRepository BudgetAllocation => _budgetAllocationRepository;

        public BudgetAllocation? BudgetAllocationEntity => _budgetAllocationRepository.Entity;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBudgetAllocationRepository(BudgetAllocation? initialAllocation = null)
        : IBudgetAllocationRepository
    {
        public BudgetAllocation? Entity { get; private set; } = initialAllocation;

        public Task<BudgetAllocation?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Entity);
        }

        public Task AddAsync(BudgetAllocation entity, CancellationToken cancellationToken = default)
        {
            Entity = entity;
            return Task.CompletedTask;
        }

        public void Update(BudgetAllocation entity)
        {
            Entity = entity;
        }
    }

    private sealed class TestUserSettingsRepository(IReadOnlyList<UserSettings> initialSettings) : IUserSettingsRepository
    {
        private readonly Dictionary<string, UserSettings> _settings = initialSettings
            .ToDictionary(setting => setting.Name, setting => new UserSettings { Name = setting.Name, Value = setting.Value }, StringComparer.Ordinal);

        public HashSet<string> AddedNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> UpdatedNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RemovedNames { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<UserSettings>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<UserSettings>>(_settings.Values
                .Select(setting => new UserSettings { Name = setting.Name, Value = setting.Value })
                .ToList());
        }

        public Task<UserSettings?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (_settings.TryGetValue(name, out var setting))
                return Task.FromResult<UserSettings?>(new UserSettings { Name = setting.Name, Value = setting.Value });

            return Task.FromResult<UserSettings?>(null);
        }

        public Task AddAsync(UserSettings entity, CancellationToken cancellationToken = default)
        {
            _settings[entity.Name] = new UserSettings { Name = entity.Name, Value = entity.Value };
            AddedNames.Add(entity.Name);
            return Task.CompletedTask;
        }

        public void Update(UserSettings entity)
        {
            _settings[entity.Name] = new UserSettings { Name = entity.Name, Value = entity.Value };
            UpdatedNames.Add(entity.Name);
        }

        public void Remove(UserSettings entity)
        {
            _settings.Remove(entity.Name);
            RemovedNames.Add(entity.Name);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public string? GetValue(string name)
        {
            return _settings.TryGetValue(name, out var setting) ? setting.Value : null;
        }
    }
}
