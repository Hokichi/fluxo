using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups.Settings;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Settings;

public sealed class SettingsIoUsTabVMTests
{
    [Fact]
    public async Task SettingsIoUsTabVM_LoadAsync_ListsUnresolvedLendsAndDebts()
    {
        var account = new Account { Id = 10, Name = "Checking", AccountType = AccountType.Checking };
        var appData = CreateAppData(
            [
                new Transaction
                {
                    Id = 1,
                    Type = TransactionType.Expense,
                    Name = "Lunch lend",
                    Amount = 25m,
                    OccurredOn = new DateTime(2026, 6, 20),
                    IsIoU = true,
                    Account = account,
                    SourceAccountId = account.Id
                },
                new Transaction
                {
                    Id = 3,
                    Type = TransactionType.Income,
                    Name = "Advance",
                    Amount = 40m,
                    OccurredOn = new DateTime(2026, 6, 20),
                    IsIoU = true,
                    Account = account,
                    SourceAccountId = account.Id
                }
            ], [],
            [account]);
        var vm = CreateVm(appData);

        await vm.LoadAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Contains(vm.Items, item => item.Kind == IoUKind.Lend && item.TransactionId == 1);
        Assert.Contains(vm.Items, item => item.Kind == IoUKind.Debt && item.TransactionId == 3);
    }

    [Fact]
    public async Task SettingsIoUsTabVM_LoadAsync_UpdatesTotalAmountText()
    {
        var account = new Account { Id = 10, Name = "Checking", AccountType = AccountType.Checking };
        var appData = CreateAppData(
            [
                new Transaction
                {
                    Id = 1,
                    Type = TransactionType.Expense,
                    Name = "Lunch lend",
                    Amount = 25m,
                    OccurredOn = new DateTime(2026, 6, 20),
                    IsIoU = true,
                    Account = account,
                    SourceAccountId = account.Id
                },
                new Transaction
                {
                    Id = 3,
                    Type = TransactionType.Income,
                    Name = "Advance",
                    Amount = 40m,
                    OccurredOn = new DateTime(2026, 6, 20),
                    IsIoU = true,
                    Account = account,
                    SourceAccountId = account.Id
                }
            ], [],
            [account]);
        var vm = CreateVm(appData);

        await vm.LoadAsync();

        Assert.Equal("65", vm.TotalAmountText);
    }

    [Fact]
    public async Task SettingsIoUsTabVM_ResolveAsync_LendCreatesIncomeAndClearsFlags()
    {
        var account = new Account
        {
            Id = 10,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 100m
        };
        var transaction = new Transaction
        {
            Id = 1,
            Type = TransactionType.Expense,
            Name = "Lunch lend",
            Amount = 25m,
            IsIoU = true,
            ShouldAffectBalance = true,
            Account = account,
            SourceAccountId = account.Id,
            ExpenseCategory = ExpenseCategory.Needs,
            Tag = new Tag { Id = 20, Name = "Food", HexCode = "#22C55E" },
            Notes = string.Empty
        };
        var appData = CreateAppData([transaction], [], [account]);
        var messenger = new StrongReferenceMessenger();
        var historyMessages = 0;
        messenger.Register<RecordLogMemoryMessage>(this, (_, _) => historyMessages++);
        var vm = CreateVm(appData, messenger);
        await vm.LoadAsync();

        var result = await vm.ResolveAsync(vm.Items.Single());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(transaction.IsIoU);
        Assert.Equal(125m, account.Balance);
        _ = appData.Received(1).AddTransactionAsync(
            Arg.Is<Transaction>(income =>
                income.Type == TransactionType.Income &&
                income.Amount == 25m &&
                income.SourceAccountId == 10 &&
                income.ExpenseCategory == ExpenseCategory.Excluded &&
                !income.IsIoU &&
                income.Name == "Lunch lend - IOU resolved"),
            Arg.Any<CancellationToken>());
        appData.Received(1).UpdateTransaction(transaction);
        appData.Received(1).UpdateAccount(account);
        Assert.Equal(0, historyMessages);
    }

    [Fact]
    public async Task SettingsIoUsTabVM_ResolveAsync_WhenRefreshFailsAfterSave_ReturnsSuccess()
    {
        var account = new Account
        {
            Id = 10,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 100m
        };
        var transaction = new Transaction
        {
            Id = 1,
            Type = TransactionType.Expense,
            Name = "Lunch lend",
            Amount = 25m,
            IsIoU = true,
            ShouldAffectBalance = true,
            Account = account,
            SourceAccountId = account.Id
        };
        var appData = CreateAppData([transaction], [], [account]);
        var vm = CreateVm(appData);
        await vm.LoadAsync();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Transaction>>(
                new InvalidOperationException("refresh failed")));

        var result = await vm.ResolveAsync(vm.Items.Single());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(transaction.IsIoU);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettingsIoUsTabVM_ResolveAsync_DebtCreatesBudgetReconciliationExpenseAndClearsFlag()
    {
        var account = new Account
        {
            Id = 10,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 100m
        };
        var income = new Transaction
        {
            Id = 3,
            Type = TransactionType.Income,
            Name = "Advance",
            Amount = 40m,
            IsIoU = true,
            ShouldAffectBalance = true,
            Account = account,
            SourceAccountId = account.Id,
            Notes = string.Empty
        };
        var tags = new List<Tag>();
        var appData = CreateAppData([income], tags, [account]);
        var vm = CreateVm(appData);
        await vm.LoadAsync();

        var result = await vm.ResolveAsync(vm.Items.Single());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(income.IsIoU);
        Assert.Equal(60m, account.Balance);
        _ = appData.Received(1).AddTagAsync(
            Arg.Is<Tag>(tag =>
                tag.Name == SystemTags.BudgetReconciliationName &&
                tag.HexCode == SystemTags.BudgetReconciliationHexCode &&
                tag.IsSystemTag),
            Arg.Any<CancellationToken>());
        _ = appData.Received(1).AddTransactionAsync(
            Arg.Is<Transaction>(expense =>
                expense.Type == TransactionType.Expense &&
                expense.Amount == 40m &&
                expense.ExpenseCategory == ExpenseCategory.Needs &&
                expense.Tag!.Name == SystemTags.BudgetReconciliationName &&
                expense.SourceAccountId == 10),
            Arg.Any<CancellationToken>());
        appData.Received(1).UpdateTransaction(income);
        appData.Received(1).UpdateAccount(account);
    }

    [Fact]
    public async Task SettingsIoUsTabVM_ResolveAsync_UnpostedWithoutSelectedAccountFailsWithoutWrites()
    {
        var account = new Account { Id = 10, Name = "Checking", Balance = 100m, IsEnabled = true };
        var transaction = new Transaction
        {
            Id = 1,
            Type = TransactionType.Expense,
            Name = "Unposted lend",
            Amount = 25m,
            IsIoU = true,
            ShouldAffectBalance = false,
            Account = account,
            SourceAccountId = account.Id
        };
        var appData = CreateAppData([transaction], [], [account]);
        var vm = CreateVm(appData);
        await vm.LoadAsync();

        var result = await vm.ResolveAsync(vm.Items.Single());

        Assert.False(result.IsSuccess);
        Assert.Equal(100m, account.Balance);
        await appData.DidNotReceive().AddTransactionAsync(
            Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettingsIoUsTabVM_ResolveAsync_UnpostedLendPostsAndSettlesSelectedAccount()
    {
        var originalAccount = new Account { Id = 10, Name = "Original", Balance = 100m, IsEnabled = true };
        var selectedAccount = new Account { Id = 20, Name = "Selected", Balance = 300m, IsEnabled = true };
        var transaction = new Transaction
        {
            Id = 1,
            Type = TransactionType.Expense,
            Name = "Unposted lend",
            Amount = 25m,
            IsIoU = true,
            ShouldAffectBalance = false,
            Account = originalAccount,
            SourceAccountId = originalAccount.Id
        };
        var appData = CreateAppData([transaction], [], [originalAccount, selectedAccount]);
        var vm = CreateVm(appData);
        await vm.LoadAsync();

        var result = await vm.ResolveAsync(vm.Items.Single(), selectedAccount.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(100m, originalAccount.Balance);
        Assert.Equal(300m, selectedAccount.Balance);
        Assert.Equal(selectedAccount.Id, transaction.SourceAccountId);
        Assert.Same(selectedAccount, transaction.Account);
        Assert.False(transaction.IsIoU);
        Assert.False(transaction.ShouldAffectBalance);
        _ = appData.Received(1).AddTransactionAsync(
            Arg.Is<Transaction>(settlement =>
                settlement.Type == TransactionType.Income &&
                settlement.SourceAccountId == selectedAccount.Id),
            Arg.Any<CancellationToken>());
    }

    private static SettingsIoUsTabVM CreateVm(IAppDataService appData, IMessenger? messenger = null)
    {
        messenger ??= new WeakReferenceMessenger();
        return new SettingsIoUsTabVM(
            appData,
            messenger,
            () => new DateTime(2026, 6, 20));
    }

    private static IAppDataService CreateAppData(
        List<Transaction> transactions,
        List<Tag> tags,
        List<Account> accounts)
    {
        var appData = Substitute.For<IAppDataService>();
        var nextId = 1000;

        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Transaction>>(transactions));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Tag>>(tags));
        appData.GetTransactionByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Transaction?>(transactions.SingleOrDefault(transaction => transaction.Id == call.ArgAt<int>(0))));
        appData.GetAccountByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Account?>(accounts.SingleOrDefault(account => account.Id == call.ArgAt<int>(0))));

        appData.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var transaction = call.ArgAt<Transaction>(0);
                if (transaction.Id <= 0)
                    transaction.Id = nextId++;
                transactions.Add(transaction);
                return Task.CompletedTask;
            });
        appData.AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var tag = call.ArgAt<Tag>(0);
                if (tag.Id <= 0)
                    tag.Id = nextId++;
                tags.Add(tag);
                return Task.CompletedTask;
            });
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return appData;
    }
}
