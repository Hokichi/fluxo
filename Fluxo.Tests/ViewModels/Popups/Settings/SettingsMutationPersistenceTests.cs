using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups.Settings;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Settings;

public sealed class SettingsMutationPersistenceTests
{
    [Fact]
    public void Accounts_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        RunInSta(() =>
        {
            var account = new Account { Id = 1, Name = "Checking", IsEnabled = true };
            var appData = Substitute.For<IAppDataService>();
            appData.GetAccountByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
            appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            appData.GetAccountsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<Account>>(new InvalidOperationException("refresh failed")));
            var vm = new SettingsAccountsTabVM(null!, appData, new WeakReferenceMessenger());
            vm.Accounts.Add(new SettingsAccountItemVM(account));

            var result = vm.ExecuteActionAsync(SettingsBatchAction.Delete, [account.Id])
                .GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
        });
    }

    [Fact]
    public void Goals_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        RunInSta(() =>
        {
            var goal = new SavingGoal { Id = 1, Name = "Emergency", TargetAmount = 100m };
            var appData = Substitute.For<IAppDataService>();
            appData.GetSavingGoalByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(goal);
            appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            appData.GetUserSettingsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<UserSettings>>(new InvalidOperationException("refresh failed")));
            var vm = new SettingsGoalsTabVM(appData, new WeakReferenceMessenger());
            vm.SavingGoals.Add(new SettingsSavingGoalItemVM(goal, isEnabled: true));

            var result = vm.ExecuteActionAsync(SettingsBatchAction.Delete, [goal.Id])
                .GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
        });
    }

    [Fact]
    public void RecurringTransactions_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        RunInSta(() =>
        {
            var recurring = new RecurringTransaction { Id = 1, Name = "Rent", IsEnabled = true };
            var appData = Substitute.For<IAppDataService>();
            appData.GetRecurringTransactionByIdAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);
            appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            appData.GetRecurringTransactionsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<RecurringTransaction>>(
                    new InvalidOperationException("refresh failed")));
            var vm = new SettingsRecurringTransactionsTabVM(appData, new WeakReferenceMessenger());
            vm.RecurringTransactions.Add(new SettingsRecurringTransactionItemVM(recurring));

            var result = vm.ExecuteActionAsync(SettingsBatchAction.Delete, [recurring.Id])
                .GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
        });
    }

    [Fact]
    public async Task Tags_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        appData.GetTagsByCountDescendingAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<(Tag Tag, int Count)>>(
                new InvalidOperationException("refresh failed")));
        var vm = new SettingsTagsTabVM(appData, new WeakReferenceMessenger());

        var result = await vm.CreateTagAsync("Food", "#22C55E");

        Assert.True(result.IsSuccess, result.ErrorMessage);
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
}
