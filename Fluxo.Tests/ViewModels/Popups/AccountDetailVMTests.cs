using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class AccountDetailVMTests
{
    [Fact]
    public async Task AccountDetailVM_LoadAsync_WhenNoHistory_SetsEmptyHistoryState()
    {
        var source = CreateSource(1, "Checking", AccountType.Checking);
        var appData = CreateAppData([source], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();

        Assert.False(sut.HasRecentActivities);
    }

    [Fact]
    public async Task AccountDetailVM_LoadAsync_WhenHistoryExists_SetsNonEmptyHistoryState()
    {
        var source = CreateSource(1, "Checking", AccountType.Checking);
        var appData = CreateAppData(
            [source],
            [new Transaction { Id = 10, SourceAccountId = source.Id, Amount = 20m, OccurredOn = DateTime.Today }],
            []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();

        Assert.True(sut.HasRecentActivities);
    }

    [Fact]
    public async Task AccountDetailVM_LoadAsync_WhenNoTransferTarget_DisablesTransfer()
    {
        var source = CreateSource(1, "Checking", AccountType.Checking);
        var appData = CreateAppData([source], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();

        Assert.False(sut.CanTransfer);
    }

    [Fact]
    public async Task AccountDetailVM_LoadAsync_WhenTransferTargetExists_AllowsTransfer()
    {
        var source = CreateSource(1, "Checking", AccountType.Checking);
        var target = CreateSource(2, "Savings", AccountType.Saving);
        var appData = CreateAppData([source, target], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();

        Assert.True(sut.CanTransfer);
    }

    [Fact]
    public async Task AccountDetailVM_ShouldConfirmDisablingOnlyEnabledSourceAsync_WhenOnlyEnabledSource_ReturnsTrue()
    {
        var source = CreateSource(1, "Checking", AccountType.Checking);
        var disabled = CreateSource(2, "Cash", AccountType.Cash, isEnabled: false);
        var appData = CreateAppData([source, disabled], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();

        Assert.True(await sut.ShouldConfirmDisablingOnlyEnabledSourceAsync());
    }

    [Fact]
    public async Task AccountDetailVM_ShouldConfirmDisablingOnlyEnabledSourceAsync_WhenAnotherEnabledSourceExists_ReturnsFalse()
    {
        var source = CreateSource(1, "Checking", AccountType.Checking);
        var target = CreateSource(2, "Cash", AccountType.Cash);
        var appData = CreateAppData([source, target], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();

        Assert.False(await sut.ShouldConfirmDisablingOnlyEnabledSourceAsync());
    }

    [Fact]
    public async Task AccountDetailVM_BuildDeleteConfirmationMessageAsync_WhenOnlyFunctioningSource_ReturnsLockWarningMessage()
    {
        var source = CreateSource(1, "Main", AccountType.Checking, balance: 200m);
        var nonFunctioning = CreateSource(2, "Zero", AccountType.Cash, balance: 0m);
        var appData = CreateAppData([source, nonFunctioning], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();
        var message = await sut.BuildDeleteConfirmationMessageAsync();

        Assert.Equal(
            "Main is the only available source. Deleting it will lock the application. Proceed to delete Main and all of its data?\n\n**THIS ACTION CANNOT BE UNDONE**",
            message);
    }

    [Fact]
    public async Task AccountDetailVM_BuildDeleteConfirmationMessageAsync_WhenNotOnlyFunctioningSource_ReturnsStandardMessage()
    {
        var source = CreateSource(1, "Main", AccountType.Checking, balance: 200m);
        var another = CreateSource(2, "Backup", AccountType.Cash, balance: 100m);
        var appData = CreateAppData([source, another], [], []);
        var sut = new AccountDetailVM(null!, source.Id, appData);

        await sut.LoadAsync();
        var message = await sut.BuildDeleteConfirmationMessageAsync();

        Assert.Equal("Delete Main and all of its data?\n\n**THIS ACTION CANNOT BE UNDONE**", message);
    }

    private static IAppDataService CreateAppData(
        IReadOnlyList<Account> sources,
        IReadOnlyList<Transaction> expenseLogs,
        IReadOnlyList<Transaction> incomeLogs)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Account?>(
                sources.FirstOrDefault(source => source.Id == call.ArgAt<int>(0))));
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sources));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(expenseLogs.Concat(incomeLogs).ToList()));
        return appData;
    }

    private static Account CreateSource(
        int id,
        string name,
        AccountType type,
        bool isEnabled = true,
        decimal balance = 100m)
    {
        return new Account
        {
            Id = id,
            Name = name,
            AccountType = type,
            Balance = balance,
            IsEnabled = isEnabled,
            PinnedOnUI = true
        };
    }
}
