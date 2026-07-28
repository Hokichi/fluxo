using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class MainVMUserSettingsTests
{
    [Fact]
    public async Task ReloadUserSettingsAsync_RefreshesAutoLockState()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserSettingNames.IsAppAutoLocked] = bool.TrueString,
            [UserSettingNames.AppAutoLockedInterval] = "60",
            [UserSettingNames.UILockingPassword] = "protected:secret-pass"
        };
        var mainViewModel = CreateMainViewModel(new TestUserSettingsUnitOfWork(settings));

        await mainViewModel.ReloadUserSettingsAsync();
        settings[UserSettingNames.IsAppAutoLocked] = bool.FalseString;
        await mainViewModel.ReloadUserSettingsAsync();

        Assert.False(mainViewModel.IsAppAutoLocked);
        Assert.Equal(60, mainViewModel.AppAutoLockedInterval);
        Assert.True(mainViewModel.HasUiLockingPassword);
    }

    internal static MainVM CreateMainViewModel(IUnitOfWork unitOfWork)
    {
        var messenger = new WeakReferenceMessenger();
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
            passwordProtector: new TestPasswordProtector());
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

    internal sealed class TestUserSettingsUnitOfWork(Dictionary<string, string> settingsByName) : IUnitOfWork
    {
        public ITransactionRepository Expenses => throw new NotSupportedException();
        public ITransactionRepository Transactions => throw new NotSupportedException();
        public ITagRepository Tags => throw new NotSupportedException();
        public ISavingGoalRepository SavingGoals => throw new NotSupportedException();
        public IAccountRepository Accounts => throw new NotSupportedException();
        public IRecurringTransactionRepository RecurringTransactions => throw new NotSupportedException();
        public IUserSettingsRepository UserSettings { get; } = new TestUserSettingsRepository(settingsByName);
        public IBudgetAllocationRepository BudgetAllocation => throw new NotSupportedException();

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

    private sealed class TestUserSettingsRepository(Dictionary<string, string> settingsByName) : IUserSettingsRepository
    {
        public Task<IReadOnlyList<UserSettings>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<UserSettings> settings = settingsByName
                .Select(setting => new UserSettings { Name = setting.Key, Value = setting.Value })
                .ToList();
            return Task.FromResult(settings);
        }

        public Task<UserSettings?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(settingsByName.TryGetValue(name, out var value)
                ? new UserSettings { Name = name, Value = value }
                : null);
        }

        public Task AddAsync(UserSettings entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Update(UserSettings entity)
        {
            throw new NotSupportedException();
        }

        public void Remove(UserSettings entity)
        {
            throw new NotSupportedException();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
