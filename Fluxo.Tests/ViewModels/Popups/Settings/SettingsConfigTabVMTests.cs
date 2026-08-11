using CommunityToolkit.Mvvm.Messaging;
using System.Globalization;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Persistence;
using Fluxo.ViewModels.Popups.Settings;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Settings;

public sealed class SettingsConfigTabVMTests
{
    [Fact]
    public void SettingsConfigTabVM_BudgetTab_ChangingAllocation_PublishesPendingState()
    {
        var messenger = new WeakReferenceMessenger();
        var captured = new List<SettingsPendingChangesChangedMessage>();
        var recipient = new PendingRecipient(captured);
        messenger.Register<PendingRecipient, SettingsPendingChangesChangedMessage>(recipient,
            static (r, m) => r.Messages.Add(m));

        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()), messenger);
        vm.IncrementAllocation(BudgetAllocationSegment.Needs, 5);

        Assert.Contains(captured, m =>
            m.Value.TabKey == SettingsTabKey.Budget &&
            m.Value.HasPendingChanges);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_InvalidAllocation_BlocksConfigurationSave()
    {
        var unitOfWork = new NullUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                NeedsThreshold = 60,
                WantsThreshold = 30,
                InvestThreshold = 20
            }
        };
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork));

        await vm.LoadAsync();

        Assert.False(vm.CanSaveConfiguration);
        Assert.Equal(
            "Needs, Wants, and Invest must add up to 100%. Current total: 110%",
            vm.ConfigurationErrorMessage);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_SettingAllocation_BalancesOtherBuckets()
    {
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()));
        await vm.LoadAsync();

        vm.NeedsAllocationPercentage = 60;

        Assert.Equal(60, vm.NeedsAllocationPercentage);
        Assert.Equal(25, vm.WantsAllocationPercentage);
        Assert.Equal(15, vm.InvestAllocationPercentage);
        Assert.True(vm.CanSaveConfiguration);
    }

    [Fact]
    public void SettingsConfigTabVM_BudgetTab_SelectedPage_DefaultsToAllocation()
    {
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()));

        Assert.Equal(SettingsBudgetManagementPage.Allocation, vm.SelectedBudgetManagementPage);
        Assert.True(vm.IsAllocationPageSelected);
        Assert.False(vm.IsConfigurationPageSelected);
    }

    [Fact]
    public void SettingsConfigTabVM_BudgetTab_SelectedPageWhenConfigurationSelected_UpdatesVisibilityState()
    {
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()));

        vm.SelectedBudgetManagementPage = SettingsBudgetManagementPage.Configuration;

        Assert.False(vm.IsAllocationPageSelected);
        Assert.True(vm.IsConfigurationPageSelected);
    }

    [Fact]
    public void SettingsConfigTabVM_BudgetTab_RevertChanges_RestoresLastSavedAllocation()
    {
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()));

        vm.NeedsAllocationPercentage = 60;
        vm.WantsAllocationPercentage = 20;
        vm.InvestAllocationPercentage = 20;

        vm.RevertChanges();

        Assert.Equal(50, vm.NeedsAllocationPercentage);
        Assert.Equal(30, vm.WantsAllocationPercentage);
        Assert.Equal(20, vm.InvestAllocationPercentage);
        Assert.False(vm.HasPendingChanges);
        Assert.True(vm.CanSaveConfiguration);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_LoadAsync_LoadsTypedBudgetAllocation()
    {
        var unitOfWork = new NullUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                NeedsThreshold = 40,
                WantsThreshold = 35,
                InvestThreshold = 25,
                AllocationLimit = 1400m,
                AllocationPeriod = AllocationPeriod.Weekly,
                RolloverPolicy = RolloverPolicy.Matching,
                OverspendPolicy = OverspendPolicy.HardStop
            }
        };
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork));

        await vm.LoadAsync();

        Assert.Equal(40, vm.NeedsAllocationPercentage);
        Assert.Equal(35, vm.WantsAllocationPercentage);
        Assert.Equal(25, vm.InvestAllocationPercentage);
        Assert.Equal(1400m, vm.AllocationLimit);
        Assert.Equal(AllocationPeriod.Weekly, vm.AllocationPeriod);
        Assert.Equal(RolloverPolicy.Matching, vm.RolloverPolicy);
        Assert.Equal(OverspendPolicy.HardStop, vm.OverspendPolicy);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_LoadAsync_LoadsPeriodStart()
    {
        var unitOfWork = new NullUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                PeriodStart = 5,
                AllocationPeriod = AllocationPeriod.Monthly
            }
        };
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork));

        await vm.LoadAsync();

        Assert.Equal(5, vm.PeriodStart);
    }

    [Fact]
    public void SettingsConfigTabVM_BudgetTab_ChangingAllocationPeriod_ClampsPeriodStart()
    {
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()))
        {
            PeriodStart = 28
        };

        vm.AllocationPeriod = AllocationPeriod.Quarterly;

        Assert.Equal(3, vm.PeriodStart);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_AllocationAmountText_UsesAllocationLimit()
    {
        var unitOfWork = new NullUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                NeedsThreshold = 50,
                WantsThreshold = 30,
                InvestThreshold = 20,
                AllocationLimit = 2000m
            }
        };
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork));

        await vm.LoadAsync();

        Assert.Equal(1000m.ToString("N0", CultureInfo.CurrentCulture), vm.NeedsAllocationAmountText);
        Assert.Equal(600m.ToString("N0", CultureInfo.CurrentCulture), vm.WantsAllocationAmountText);
        Assert.Equal(400m.ToString("N0", CultureInfo.CurrentCulture), vm.InvestAllocationAmountText);

        vm.AllocationLimit = 3000m;

        Assert.Equal(1500m.ToString("N0", CultureInfo.CurrentCulture), vm.NeedsAllocationAmountText);
        Assert.Equal(900m.ToString("N0", CultureInfo.CurrentCulture), vm.WantsAllocationAmountText);
        Assert.Equal(600m.ToString("N0", CultureInfo.CurrentCulture), vm.InvestAllocationAmountText);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_LoadAsync_DoesNotPublishTransientPendingState()
    {
        var messenger = new WeakReferenceMessenger();
        var captured = new List<SettingsPendingChangesChangedMessage>();
        var recipient = new PendingRecipient(captured);
        messenger.Register<PendingRecipient, SettingsPendingChangesChangedMessage>(recipient,
            static (r, m) => r.Messages.Add(m));
        var unitOfWork = new NullUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                NeedsThreshold = 40,
                WantsThreshold = 35,
                InvestThreshold = 25,
                AllocationLimit = 1400m,
                AllocationPeriod = AllocationPeriod.Weekly,
                RolloverPolicy = RolloverPolicy.Matching,
                OverspendPolicy = OverspendPolicy.HardStop
            }
        };
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork), messenger);

        await vm.LoadAsync();

        var budgetMessages = captured.Where(message => message.Value.TabKey == SettingsTabKey.Budget).ToList();
        Assert.DoesNotContain(budgetMessages, message => message.Value.HasPendingChanges);
        var finalMessage = Assert.Single(budgetMessages);
        Assert.False(finalMessage.Value.HasPendingChanges);
        Assert.False(vm.HasPendingChanges);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_BuildApplyChangesAsync_UpdatesTypedBudgetAllocation()
    {
        var unitOfWork = new NullUnitOfWork();
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork));
        await vm.LoadAsync();

        vm.AllocationLimit = 2100m;
        vm.AllocationPeriod = AllocationPeriod.Yearly;
        vm.RolloverPolicy = RolloverPolicy.Pooled;
        vm.OverspendPolicy = OverspendPolicy.SoftDebt;

        var (result, actions) = await vm.BuildApplyChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(actions);
        Assert.NotNull(unitOfWork.BudgetAllocationEntity);
        Assert.Equal(2100m, unitOfWork.BudgetAllocationEntity.AllocationLimit);
        Assert.Equal(AllocationPeriod.Yearly, unitOfWork.BudgetAllocationEntity.AllocationPeriod);
        Assert.Equal(RolloverPolicy.Pooled, unitOfWork.BudgetAllocationEntity.RolloverPolicy);
        Assert.Equal(OverspendPolicy.SoftDebt, unitOfWork.BudgetAllocationEntity.OverspendPolicy);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_BuildApplyChangesAsync_UpdatesPeriodStart()
    {
        var unitOfWork = new NullUnitOfWork();
        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(unitOfWork));
        await vm.LoadAsync();

        vm.AllocationPeriod = AllocationPeriod.Yearly;
        vm.PeriodStart = 12;

        var (result, _) = await vm.BuildApplyChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(12, unitOfWork.BudgetAllocationEntity!.PeriodStart);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_BuildApplyChangesAsyncWhenRolloverPolicyChangesToEnabled_MarksCurrentPeriod()
    {
        var unitOfWork = new NullUnitOfWork
        {
            BudgetAllocationEntity = new BudgetAllocation
            {
                AllocationPeriod = AllocationPeriod.Monthly,
                PeriodStart = 10,
                RolloverPolicy = RolloverPolicy.None,
                LastRolloverPeriodStart = DateTime.MinValue
            }
        };
        var vm = new SettingsBudgetTabVM(
            () => 1000m,
            new AppDataService(unitOfWork),
            todayProvider: () => new DateTime(2026, 2, 15));
        await vm.LoadAsync();

        vm.RolloverPolicy = RolloverPolicy.Pooled;

        var (result, _) = await vm.BuildApplyChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 2, 10), unitOfWork.BudgetAllocationEntity!.LastRolloverPeriodStart);
    }

    [Fact]
    public void SettingsConfigTabVM_BudgetTab_ChangingConfiguration_PublishesPendingState()
    {
        var messenger = new WeakReferenceMessenger();
        var captured = new List<SettingsPendingChangesChangedMessage>();
        var recipient = new PendingRecipient(captured);
        messenger.Register<PendingRecipient, SettingsPendingChangesChangedMessage>(recipient,
            static (r, m) => r.Messages.Add(m));

        var vm = new SettingsBudgetTabVM(() => 1000m, new AppDataService(new NullUnitOfWork()), messenger);

        vm.AllocationLimit = 250m;
        Assert.Contains(captured, IsBudgetPendingMessage);

        captured.Clear();
        vm.AllocationPeriod = AllocationPeriod.Yearly;
        Assert.Contains(captured, IsBudgetPendingMessage);

        captured.Clear();
        vm.RolloverPolicy = RolloverPolicy.Pooled;
        Assert.Contains(captured, IsBudgetPendingMessage);

        captured.Clear();
        vm.OverspendPolicy = OverspendPolicy.SoftDebt;
        Assert.Contains(captured, IsBudgetPendingMessage);

        static bool IsBudgetPendingMessage(SettingsPendingChangesChangedMessage message)
        {
            return message.Value.TabKey == SettingsTabKey.Budget &&
                   message.Value.HasPendingChanges;
        }
    }

    [Fact]
    public void SettingsConfigTabVM_PersonalizationTab_ChangingStartupToggle_PublishesPendingState()
    {
        var messenger = new WeakReferenceMessenger();
        var captured = new List<SettingsPendingChangesChangedMessage>();
        var recipient = new PendingRecipient(captured);
        messenger.Register<PendingRecipient, SettingsPendingChangesChangedMessage>(recipient,
            static (r, m) => r.Messages.Add(m));

        var vm = new SettingsPersonalizationTabVM(new AppDataService(new NullUnitOfWork()), messenger);
        vm.ShouldRunAtStartup = true;

        Assert.Contains(captured, m =>
            m.Value.TabKey == SettingsTabKey.Personalization &&
            m.Value.HasPendingChanges);
    }

    [Fact]
    public void SettingsConfigTabVM_PersonalizationTab_ChangingLockUiWhenAway_PublishesPendingState()
    {
        var messenger = new WeakReferenceMessenger();
        var captured = new List<SettingsPendingChangesChangedMessage>();
        var recipient = new PendingRecipient(captured);
        messenger.Register<PendingRecipient, SettingsPendingChangesChangedMessage>(recipient,
            static (r, m) => r.Messages.Add(m));

        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(new NullUnitOfWork()),
            messenger,
            passwordProtector: new TestPasswordProtector());

        vm.IsAppAutoLocked = true;

        Assert.Contains(captured, m =>
            m.Value.TabKey == SettingsTabKey.Personalization &&
            m.Value.HasPendingChanges);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_LoadAsync_DefaultsAutoLockIntervalToThirtySecondPreset()
    {
        var unitOfWork = new TestSettingsUnitOfWork([]);
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(unitOfWork),
            passwordProtector: new TestPasswordProtector());

        await vm.LoadAsync();

        Assert.Equal(30, vm.AppAutoLockedInterval);
        Assert.Equal("30", vm.SelectedAppAutoLockPreset);
        Assert.False(vm.IsCustomAutoLockInterval);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_SelectingFixedAutoLockPreset_UpdatesIntervalAndPublishesPendingState()
    {
        var messenger = new WeakReferenceMessenger();
        var captured = new List<SettingsPendingChangesChangedMessage>();
        var recipient = new PendingRecipient(captured);
        messenger.Register<PendingRecipient, SettingsPendingChangesChangedMessage>(recipient,
            static (r, m) => r.Messages.Add(m));
        var unitOfWork = new TestSettingsUnitOfWork([]);
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(unitOfWork),
            messenger,
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.SelectedAppAutoLockPreset = "180";

        Assert.Equal(180, vm.AppAutoLockedInterval);
        Assert.False(vm.IsCustomAutoLockInterval);
        Assert.Contains(captured, m =>
            m.Value.TabKey == SettingsTabKey.Personalization &&
            m.Value.HasPendingChanges);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_LoadAsync_SelectsCustomPresetForNonPresetInterval()
    {
        var unitOfWork = new TestSettingsUnitOfWork(
        [
            new UserSettings { Name = UserSettingNames.AppAutoLockedInterval, Value = "45" }
        ]);
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(unitOfWork),
            passwordProtector: new TestPasswordProtector());

        await vm.LoadAsync();

        Assert.Equal(45, vm.AppAutoLockedInterval);
        Assert.Equal("Custom", vm.SelectedAppAutoLockPreset);
        Assert.True(vm.IsCustomAutoLockInterval);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_LoadAsync_DecryptsUiLockPassword()
    {
        var unitOfWork = new TestSettingsUnitOfWork(
        [
            new UserSettings { Name = UserSettingNames.UILockingPassword, Value = "protected:secret-pass" }
        ]);
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(unitOfWork),
            passwordProtector: new TestPasswordProtector());

        await vm.LoadAsync();

        Assert.Equal("secret-pass", vm.UiLockingPassword);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_BuildApplyChangesAsync_ProtectsPasswordBeforePersisting()
    {
        var unitOfWork = new TestSettingsUnitOfWork([]);
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(unitOfWork),
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.UiLockingPassword = "secret-pass";
        var (result, _, _, _, _) = await vm.BuildApplyChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("protected:secret-pass", unitOfWork.GetValue(UserSettingNames.UILockingPassword));
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_BuildApplyChangesAsync_RemovesBlankPassword()
    {
        var unitOfWork = new TestSettingsUnitOfWork(
        [
            new UserSettings { Name = UserSettingNames.UILockingPassword, Value = "protected:secret-pass" }
        ]);
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(unitOfWork),
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.UiLockingPassword = "";
        var (result, _, _, _, _) = await vm.BuildApplyChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(unitOfWork.GetValue(UserSettingNames.UILockingPassword));
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_ReportsPendingPasswordSeparately()
    {
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(new TestSettingsUnitOfWork([])),
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.UiLockingPassword = "new-password";

        Assert.True(vm.HasPendingPasswordChange);
        Assert.False(vm.HasPendingNotificationChanges);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_ReportsPendingNotificationsSeparately()
    {
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(new TestSettingsUnitOfWork([])),
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.NotificationSettings[0].IsEnabled = !vm.NotificationSettings[0].IsEnabled;

        Assert.True(vm.HasPendingNotificationChanges);
        Assert.False(vm.HasPendingPasswordChange);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_ReportsPendingAutoLockToggleSeparately()
    {
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(new TestSettingsUnitOfWork([])),
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.IsAppAutoLocked = !vm.IsAppAutoLocked;

        Assert.True(vm.HasPendingAutoLockEnabledChange);
        Assert.False(vm.HasPendingAutoLockIntervalChange);
    }

    [Fact]
    public async Task SettingsConfigTabVM_PersonalizationTab_ReportsPendingAutoLockIntervalSeparately()
    {
        var vm = new SettingsPersonalizationTabVM(
            new AppDataService(new TestSettingsUnitOfWork([])),
            passwordProtector: new TestPasswordProtector());
        await vm.LoadAsync();

        vm.AppAutoLockedInterval += 30;

        Assert.True(vm.HasPendingAutoLockIntervalChange);
        Assert.False(vm.HasPendingAutoLockEnabledChange);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_ReportsConfigurationChangesSeparately()
    {
        var vm = new SettingsBudgetTabVM(
            () => 1000m,
            new AppDataService(new TestSettingsUnitOfWork([])));
        await vm.LoadAsync();

        vm.AllocationLimit += 100m;

        Assert.True(vm.HasPendingConfigurationChanges);
        Assert.False(vm.HasPendingAllocationChanges);
    }

    [Fact]
    public async Task SettingsConfigTabVM_BudgetTab_ReportsAllocationChangesSeparately()
    {
        var vm = new SettingsBudgetTabVM(
            () => 1000m,
            new AppDataService(new TestSettingsUnitOfWork([])));
        await vm.LoadAsync();

        vm.NeedsAllocationPercentage += 1;

        Assert.True(vm.HasPendingAllocationChanges);
        Assert.False(vm.HasPendingConfigurationChanges);
    }

    private sealed class PendingRecipient(List<SettingsPendingChangesChangedMessage> messages)
    {
        public List<SettingsPendingChangesChangedMessage> Messages { get; } = messages;
    }

    private sealed class TestPasswordProtector : IUiLockPasswordProtector
    {
        public string Protect(string? password)
        {
            return string.IsNullOrWhiteSpace(password) ? string.Empty : "protected:" + password;
        }

        public string Unprotect(string? protectedPassword)
        {
            return string.IsNullOrWhiteSpace(protectedPassword)
                ? string.Empty
                : protectedPassword.StartsWith("protected:", StringComparison.Ordinal)
                    ? protectedPassword["protected:".Length..]
                    : string.Empty;
        }
    }

    private sealed class TestSettingsUnitOfWork(IReadOnlyList<UserSettings> settings) : IUnitOfWork
    {
        private readonly Dictionary<string, UserSettings> _settings = settings
            .ToDictionary(setting => setting.Name,
                setting => new UserSettings { Name = setting.Name, Value = setting.Value },
                StringComparer.Ordinal);

        public ITransactionRepository Expenses => throw new NotSupportedException();
        public ITransactionRepository Transactions => throw new NotSupportedException();
        public ITagRepository Tags => throw new NotSupportedException();
        public ISavingGoalRepository SavingGoals => throw new NotSupportedException();
        public IAccountRepository Accounts { get; } = new TestAccountRepository();
        public IRecurringTransactionRepository RecurringTransactions => throw new NotSupportedException();
        public IUserSettingsRepository UserSettings => new TestUserSettingsRepository(_settings);
        public IBudgetAllocationRepository BudgetAllocation => new TestBudgetAllocationRepository();

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

        public string? GetValue(string name)
        {
            return _settings.TryGetValue(name, out var setting) ? setting.Value : null;
        }
    }

    private sealed class TestUserSettingsRepository(Dictionary<string, UserSettings> settings)
        : IUserSettingsRepository
    {
        public Task<IReadOnlyList<UserSettings>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<UserSettings>>(settings.Values
                .Select(setting => new UserSettings { Name = setting.Name, Value = setting.Value })
                .ToList());
        }

        public Task<UserSettings?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(settings.TryGetValue(name, out var setting)
                ? new UserSettings { Name = setting.Name, Value = setting.Value }
                : null);
        }

        public Task AddAsync(UserSettings entity, CancellationToken cancellationToken = default)
        {
            settings[entity.Name] = new UserSettings { Name = entity.Name, Value = entity.Value };
            return Task.CompletedTask;
        }

        public void Update(UserSettings entity)
        {
            settings[entity.Name] = new UserSettings { Name = entity.Name, Value = entity.Value };
        }

        public void Remove(UserSettings entity)
        {
            settings.Remove(entity.Name);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class NullUnitOfWork : IUnitOfWork
    {
        public ITransactionRepository Expenses => throw new NotSupportedException();
        public ITransactionRepository Transactions => throw new NotSupportedException();
        public ITagRepository Tags => throw new NotSupportedException();
        public ISavingGoalRepository SavingGoals => throw new NotSupportedException();
        public IAccountRepository Accounts { get; } = new TestAccountRepository();
        public IRecurringTransactionRepository RecurringTransactions => throw new NotSupportedException();
        public IUserSettingsRepository UserSettings => throw new NotSupportedException();
        public IBudgetAllocationRepository BudgetAllocation => BudgetAllocationRepository;

        public BudgetAllocation? BudgetAllocationEntity
        {
            get => BudgetAllocationRepository.Entity;
            init => BudgetAllocationRepository.Entity = value;
        }

        private TestBudgetAllocationRepository BudgetAllocationRepository { get; } = new();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBudgetAllocationRepository : IBudgetAllocationRepository
    {
        public BudgetAllocation? Entity { get; set; }

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

    private sealed class TestAccountRepository : IAccountRepository
    {
        public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Account> sources = [];
            return Task.FromResult(sources);
        }

        public Task<Account?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Account?>(null);
        }

        public Task AddAsync(Account entity, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Update(Account entity)
        {
        }

        public void Remove(Account entity)
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<Account>> SearchAsync(
            AccountFilter filter,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Account> sources = [];
            return Task.FromResult(sources);
        }

        public Task<IReadOnlyList<Account>> GetMarkedForDeletionAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Account> sources = [];
            return Task.FromResult(sources);
        }
    }
}
