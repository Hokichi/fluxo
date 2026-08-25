using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Popups.AccountReconciliation;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class AccountReconciliationVMTests
{
    [Fact]
    public void AccountReconciliationVM_Constructor_FiltersSavingSources_AndSelectsPromptSource()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();

        var vm = new AccountReconciliationVM(sources, sources[2], appData);

        Assert.Equal(
            [AccountType.Credit, AccountType.Checking, AccountType.Checking, AccountType.Cash],
            vm.Accounts.Select(source => source.AccountType).ToArray());
        Assert.Equal(3, vm.SelectedAccount?.Id);
    }

    [Fact]
    public void AccountReconciliationVM_SelectedAccount_ExposesTargetAndCurrentLabels()
    {
        var sources = CreateSourceViewModels();
        sources[0].SpentAmount = 80m;
        sources[2].Balance = 500m;
        sources[3].Balance = 75m;
        var vm = new AccountReconciliationVM(sources, sources[2], Substitute.For<IAppDataService>());

        Assert.Equal("New Balance", vm.NewAmountLabel);
        Assert.Equal("Current Balance", vm.CurrentAmountLabel);
        Assert.Equal(500m, vm.CurrentAmount);

        vm.SelectedAccount = sources[0];

        Assert.Equal("New Spent Credits", vm.NewAmountLabel);
        Assert.Equal("Current Spent Credits", vm.CurrentAmountLabel);
        Assert.Equal(80m, vm.CurrentAmount);

        vm.SelectedAccount = sources[3];

        Assert.Equal("New Balance", vm.NewAmountLabel);
        Assert.Equal("Current Balance", vm.CurrentAmountLabel);
        Assert.Equal(75m, vm.CurrentAmount);
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_WhenTargetDecreases_CreatesNeedExpenseWithBudgetReconciliationTag()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };
        var reconciliationTag = new Tag
        {
            Id = 9,
            Name = SystemTags.BudgetReconciliationName,
            HexCode = SystemTags.BudgetReconciliationHexCode,
            IsSystemTag = true
        };

        Transaction? savedTransaction = null;
        appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(persistedSource));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([reconciliationTag]));
        appData.AddTransactionAsync(Arg.Do<Transaction>(transaction => savedTransaction = transaction), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new AccountReconciliationVM(sources, sources[2], appData)
        {
            AmountText = 457.50m
        };

        var result = await vm.SaveAsync(shouldLogTransaction: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.CreatedTransaction);
        Assert.NotNull(savedTransaction);
        Assert.Equal(TransactionType.Expense, savedTransaction.Type);
        Assert.Equal("Checking - Budget Reconciliation", savedTransaction.Name);
        Assert.Equal(42.50m, savedTransaction.Amount);
        Assert.Equal(ExpenseCategory.Needs, savedTransaction.ExpenseCategory);
        Assert.Equal(3, savedTransaction.SourceAccountId);
        Assert.Equal(9, savedTransaction.TagId);
        Assert.Equal(DateTime.Today, savedTransaction.OccurredOn);
        Assert.False(savedTransaction.IsForDeletion);
        Assert.Equal(457.50m, persistedSource.Balance);
        appData.Received(1).UpdateAccount(persistedSource);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_WhenTargetIncreases_CreatesIncome()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };
        var reconciliationTag = new Tag
        {
            Id = 9,
            Name = SystemTags.BudgetReconciliationName,
            HexCode = SystemTags.BudgetReconciliationHexCode,
            IsSystemTag = true
        };
        Transaction? savedTransaction = null;

        appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(persistedSource));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([reconciliationTag]));
        appData.AddTransactionAsync(Arg.Do<Transaction>(transaction => savedTransaction = transaction), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new AccountReconciliationVM(sources, sources[2], appData)
        {
            AmountText = 525m
        };

        var result = await vm.SaveAsync(shouldLogTransaction: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(savedTransaction);
        Assert.Equal(TransactionType.Income, savedTransaction.Type);
        Assert.Equal(25m, savedTransaction.Amount);
        Assert.Equal(ExpenseCategory.Excluded, savedTransaction.ExpenseCategory);
        Assert.Equal(525m, persistedSource.Balance);
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_WhenCreditTargetDecreases_CreatesExpense()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 1,
            Name = "Credit",
            AccountType = AccountType.Credit,
            SpentAmount = 80m
        };
        var reconciliationTag = new Tag
        {
            Id = 9,
            Name = SystemTags.BudgetReconciliationName,
            HexCode = SystemTags.BudgetReconciliationHexCode,
            IsSystemTag = true
        };
        Transaction? savedTransaction = null;

        appData.GetAccountByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(persistedSource));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([reconciliationTag]));
        appData.AddTransactionAsync(Arg.Do<Transaction>(transaction => savedTransaction = transaction), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new AccountReconciliationVM(sources, sources[0], appData)
        {
            AmountText = 50m
        };

        var result = await vm.SaveAsync(shouldLogTransaction: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(savedTransaction);
        Assert.Equal(TransactionType.Expense, savedTransaction.Type);
        Assert.Equal(30m, savedTransaction.Amount);
        Assert.Equal(50m, persistedSource.SpentAmount);
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_WhenLoggingDeclined_UpdatesTargetWithoutTransaction()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };

        appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(persistedSource));
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new AccountReconciliationVM(sources, sources[2], appData)
        {
            AmountText = 525m
        };

        var result = await vm.SaveAsync(shouldLogTransaction: false);

        Assert.True(result.IsSuccess);
        Assert.Null(result.CreatedTransaction);
        Assert.Equal(525m, persistedSource.Balance);
        await appData.DidNotReceive().AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        await appData.DidNotReceive().AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_WhenInvalidationFailsAfterSave_ReturnsSuccess()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };
        appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>()).Returns(persistedSource);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var vm = new AccountReconciliationVM(sources, sources[2], appData)
        {
            AmountText = 525m
        };
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<DashboardDataInvalidatedMessage>(
            recipient,
            (_, _) => throw new InvalidOperationException("invalidation failed"));

        AccountReconciliationSaveResult result;
        try
        {
            result = await vm.SaveAsync(shouldLogTransaction: false);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(525m, persistedSource.Balance);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_WhenDifferenceIsZero_DoesNotCreateTransaction()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };

        appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(persistedSource));
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new AccountReconciliationVM(sources, sources[2], appData)
        {
            AmountText = 500m
        };

        var result = await vm.SaveAsync(shouldLogTransaction: true);

        Assert.True(result.IsSuccess);
        Assert.Null(result.CreatedTransaction);
        Assert.Equal(500m, persistedSource.Balance);
        await appData.DidNotReceive().AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        await appData.DidNotReceive().AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountReconciliationVM_SaveAsync_AddsMissingBudgetReconciliationSystemTag()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var persistedSource = new Account
        {
            Id = 3,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };
        Tag? addedTag = null;

        appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(persistedSource));
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.AddTagAsync(Arg.Do<Tag>(tag =>
            {
                tag.Id = 14;
                addedTag = tag;
            }), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new AccountReconciliationVM(sources, sources[2], appData)
        {
            AmountText = 12m
        };

        var result = await vm.SaveAsync(shouldLogTransaction: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(addedTag);
        Assert.Equal(SystemTags.BudgetReconciliationName, addedTag.Name);
        Assert.Equal(SystemTags.BudgetReconciliationHexCode, addedTag.HexCode);
        Assert.True(addedTag.IsSystemTag);
        await appData.Received(1).AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AccountReconciliationVM_CanSave_IsFalseUntilAmountIsGreaterThanZero()
    {
        var sources = CreateSourceViewModels();
        var appData = Substitute.For<IAppDataService>();
        var changedProperties = new List<string?>();
        var vm = new AccountReconciliationVM(sources, sources[2], appData);
        vm.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.False(vm.CanSave);

        vm.AmountText = 1m;

        Assert.True(vm.CanSave);
        Assert.Contains(nameof(AccountReconciliationVM.CanSave), changedProperties);

        vm.AmountText = 0m;

        Assert.False(vm.CanSave);
    }

    private static IReadOnlyList<AccountVM> CreateSourceViewModels()
    {
        return
        [
            new AccountVM { Id = 1, Name = "Credit", AccountType = AccountType.Credit },
            new AccountVM { Id = 2, Name = "Spare Checking", AccountType = AccountType.Checking },
            new AccountVM { Id = 3, Name = "Checking", AccountType = AccountType.Checking },
            new AccountVM { Id = 4, Name = "Cash", AccountType = AccountType.Cash },
            new AccountVM { Id = 5, Name = "Emergency", AccountType = AccountType.Saving }
        ];
    }
}
