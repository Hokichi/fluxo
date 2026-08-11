using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Helpers.Transaction;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Transactions;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMPersistenceTests
{
    [Fact]
    public void TransactionPopupVMPersistence_SaveAsync_WithoutInitializeSavesANewAdd_Transaction()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            var account = CreateAccount();
            var appData = CreateAppData(account, CreateTransaction(account));
            var added = new Transaction();
            appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    added = call.Arg<Transaction>();
                    added.Id = 77;
                });
            var peers = TransactionPopupVMFactory.CreatePeers(CreateMainViewModel([accountVm]), appData);
            var vm = peers.Popup;
            vm.NameText = "Standalone add";
            vm.AmountText = 25m;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(77, added.Id);
            appData.Received(1).AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_SaveAsync_EditReturnsConfirmationThenSucceedsWhenOverflowIs_Approved()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            accountVm.AccountType = AccountType.Credit;
            accountVm.SpentAmount = 90m;
            accountVm.MaximumSpending = 100m;
            accountVm.AccountLimit = 1000m;
            var account = CreateAccount();
            account.AccountType = AccountType.Credit;
            account.SpentAmount = 90m;
            account.MaximumSpending = 100m;
            account.AccountLimit = 1000m;
            var transaction = CreateTransaction(account);
            transaction.Amount = 20m;
            var appData = CreateAppData(account, transaction);
            var peers = TransactionPopupVMFactory.CreatePeers(CreateMainViewModel([accountVm]), appData);
            var vm = peers.Popup;
            vm.InitializeView(CreateTransactionVm(accountVm));
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();
            vm.AmountText = 40m;

            var confirmation = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.False(confirmation.IsSuccess);
            Assert.True(confirmation.RequiresConfirmation);
            Assert.Equal(90m, account.SpentAmount);
            appData.DidNotReceive().UpdateTransaction(transaction);

            var approved = vm.SaveAsync(false, allowMaximumSpendingOverflow: true).GetAwaiter().GetResult();

            Assert.True(approved.IsSuccess, approved.ErrorMessage);
            Assert.Equal(110m, account.SpentAmount);
            appData.Received(1).UpdateTransaction(transaction);
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_Processing_NextDoesNot_Persist()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            accountVm.AccountType = AccountType.Credit;
            accountVm.SpentAmount = 90m;
            accountVm.MaximumSpending = 200m;
            accountVm.AccountLimit = 1000m;
            var account = CreateAccount();
            account.AccountType = AccountType.Credit;
            account.SpentAmount = 90m;
            account.MaximumSpending = 1000m;
            account.AccountLimit = 1000m;
            var firstPersisted = CreateTransaction(account);
            firstPersisted.Id = 100;
            firstPersisted.Amount = 10m;
            var appData = CreateAppData(account, firstPersisted);
            var added = new List<Transaction>();
            var nextId = 100;
            appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var transaction = call.Arg<Transaction>();
                    transaction.Id = nextId++;
                    added.Add(transaction);
                });
            appData.GetTransactionByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Transaction?>(
                    call.Arg<int>() == firstPersisted.Id
                        ? firstPersisted
                        : added.FirstOrDefault(item => item.Id == call.Arg<int>())));
            appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Transaction>>(added));
            var messenger = new WeakReferenceMessenger();
            var vm = TransactionPopupVMFactory.Create(
                CreateMainViewModel([accountVm]), appData, messenger: messenger);
            vm.InitializeRecurringProcessing([
                new RecurringTransactionVM
                {
                    Id = 1, Name = "First", Amount = 10m, Type = RecurringTransactionType.Expense,
                    Category = ExpenseCategory.Needs, Source = accountVm,
                    Tag = new TagVM { Id = 1, Name = "General" }
                },
                new RecurringTransactionVM
                {
                    Id = 2, Name = "Second", Amount = 5m, Type = RecurringTransactionType.Expense,
                    Category = ExpenseCategory.Needs, Source = accountVm,
                    Tag = new TagVM { Id = 1, Name = "General" }
                }
            ]);

            Assert.True(vm.FinishQueuedTransactionsAsync().GetAwaiter().GetResult().IsSuccess);
            Assert.Equal(2, added.Count);
            appData.DidNotReceive().UpdateTransaction(firstPersisted);
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_SaveAsync_EditUpdatesTheLoadedTransaction_Id()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            var account = CreateAccount();
            var transaction = CreateTransaction(account);
            var appData = CreateAppData(account, transaction);
            var vm = TransactionPopupVMFactory.Create(CreateMainViewModel([accountVm]), appData);
            vm.InitializeView(CreateTransactionVm(accountVm));
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();
            vm.NameText = "Updated";
            vm.AmountText = 25m;

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            appData.Received(1).GetTransactionByIdAsync(42, Arg.Any<CancellationToken>());
            appData.Received(1).UpdateTransaction(transaction);
            _ = appData.DidNotReceive().AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task TransactionPopupVMPersistence_Edit_SameAccountAppliesBalanceDeltaOnceWhenAccountsAreSeparate_Instances()
    {
        var loadedAccount = CreateAccount();
        loadedAccount.Balance = 90m;
        var selectedAccount = CreateAccount();
        selectedAccount.Balance = 100m;
        var transaction = CreateTransaction(loadedAccount);
        var appData = CreateAppData(selectedAccount, transaction);
        var loaded = CreateTransactionVm(CreateAccountVm());
        var pending = TransactionMappingHelper.CreatePending(loaded);
        pending.Amount = 25m;

        var result = await new TransactionPersistenceHelper(appData, new WeakReferenceMessenger())
            .SaveAsync(loaded, pending, new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(75m, loadedAccount.Balance);
        Assert.Equal(100m, selectedAccount.Balance);
        appData.Received(1).UpdateAccount(loadedAccount);
        appData.DidNotReceive().UpdateAccount(selectedAccount);
    }

    [Fact]
    public async Task TransactionPopupVMPersistence_Edit_SameCreditAccountClampsSpentAmountWhenNewAmountIs_Smaller()
    {
        var loadedAccount = CreateAccount();
        loadedAccount.AccountType = AccountType.Credit;
        loadedAccount.SpentAmount = 5m;
        var selectedAccount = CreateAccount();
        selectedAccount.AccountType = AccountType.Credit;
        selectedAccount.SpentAmount = 5m;
        var transaction = CreateTransaction(loadedAccount);
        var appData = CreateAppData(selectedAccount, transaction);
        var loaded = CreateTransactionVm(CreateAccountVm());
        var pending = TransactionMappingHelper.CreatePending(loaded);
        pending.Amount = 2m;

        var result = await new TransactionPersistenceHelper(appData, new WeakReferenceMessenger())
            .SaveAsync(loaded, pending, new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2m, loadedAccount.SpentAmount);
        appData.Received(1).UpdateAccount(loadedAccount);
    }

    [Fact]
    public async Task TransactionPopupVMPersistence_Edit_SameCreditAccountExcludesExistingTransactionFromMaximumSpending_Check()
    {
        var loadedAccount = CreateAccount();
        loadedAccount.AccountType = AccountType.Credit;
        loadedAccount.SpentAmount = 90m;
        loadedAccount.MaximumSpending = 100m;
        var selectedAccount = CreateAccount();
        selectedAccount.AccountType = AccountType.Credit;
        selectedAccount.SpentAmount = 90m;
        selectedAccount.MaximumSpending = 100m;
        var transaction = CreateTransaction(loadedAccount);
        transaction.Amount = 20m;
        var appData = CreateAppData(selectedAccount, transaction);
        var loaded = CreateTransactionVm(CreateAccountVm());
        loaded.Amount = 20m;
        var pending = TransactionMappingHelper.CreatePending(loaded);
        pending.Amount = 30m;

        var result = await new TransactionPersistenceHelper(appData, new WeakReferenceMessenger())
            .SaveAsync(loaded, pending, new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal(100m, loadedAccount.SpentAmount);
    }

    [Fact]
    public async Task TransactionPopupVMPersistence_Edit_RepaymentComposesDetachedAccountInstancesByLogical_Id()
    {
        var source = CreateAccount();
        source.Balance = 480m;
        var target = new Account { Id = 2, Name = "Visa", AccountType = AccountType.Credit, SpentAmount = 80m };
        var detachedSource = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking, Balance = 999m };
        var detachedTarget = new Account { Id = 2, Name = "Visa", AccountType = AccountType.Credit, SpentAmount = 999m };
        var tag = new Tag { Id = 9, Name = "Balance Update", HexCode = "#fff", IsSystemTag = true };
        var expense = new Transaction
        {
            Id = 77, Type = TransactionType.Expense, SourceAccountId = 1, Account = detachedSource,
            RepaymentAccountId = 2, RepaymentAccount = detachedTarget, Name = "Repayment to Visa",
            Amount = 20m, OccurredOn = DateTime.Today, ExpenseCategory = ExpenseCategory.Savings,
            Tag = tag, TagId = tag.Id, IsExcludedFromBudget = true
        };
        var income = new Transaction
        {
            Id = 78, Type = TransactionType.Income, SourceAccountId = 2, Account = detachedTarget,
            RepaymentAccountId = 2, Name = "Repayment from Checking", Amount = 20m,
            OccurredOn = expense.OccurredOn, Tag = tag, TagId = tag.Id, IsExcludedFromBudget = true
        };
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(77, Arg.Any<CancellationToken>()).Returns(expense);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([expense, income]);
        appData.GetAccountByIdAsync(1, Arg.Any<CancellationToken>()).Returns(source);
        appData.GetAccountByIdAsync(2, Arg.Any<CancellationToken>()).Returns(target);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns([tag]);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var loaded = new TransactionVM
        {
            Id = 77, Type = TransactionType.Expense, SourceAccountId = 1, RepaymentAccountId = 2,
            Name = expense.Name, Amount = 20m, OccurredOn = expense.OccurredOn,
            ExpenseCategory = ExpenseCategory.Savings
        };
        var pending = TransactionMappingHelper.CreatePending(loaded);
        pending.Amount = 30m;
        var messenger = new WeakReferenceMessenger();
        var invalidations = new List<DashboardDataInvalidationScope>();
        var recipient = new object();
        messenger.Register<DashboardDataInvalidatedMessage>(recipient,
            (_, message) => invalidations.Add(message.Value));

        var result = await new TransactionPersistenceHelper(appData, messenger)
            .SaveAsync(loaded, pending, new(IsRepayment: true));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(470m, source.Balance);
        Assert.Equal(70m, target.SpentAmount);
        appData.Received(1).UpdateAccount(source);
        appData.Received(1).UpdateAccount(target);
        appData.DidNotReceive().UpdateAccount(detachedSource);
        appData.DidNotReceive().UpdateAccount(detachedTarget);
        appData.Received(1).UpdateTransaction(expense);
        appData.Received(1).UpdateTransaction(income);
        Assert.Contains(DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications,
            invalidations);
    }

    [Fact]
    public async Task TransactionPopupVMPersistence_Add_UpdatesAccountBalanceOnceAndKeepsGoalTagNon_System()
    {
        var account = CreateAccount();
        account.Balance = 100m;
        var appData = CreateAppData(account, CreateTransaction(account));
        appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
            .Do(call => call.Arg<Transaction>().Id = 77);
        appData.When(data => data.AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>()))
            .Do(call => call.Arg<Tag>().Id = 99);
        var pending = new TransactionVM
        {
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            Name = "Goal contribution",
            Amount = 10m,
            OccurredOn = DateTime.Today,
            ExpenseCategory = ExpenseCategory.Savings,
            GoalId = 1
        };
        var goal = new SavingGoal { Id = 1, Name = "Emergency", CurrentAmount = 100m };
        appData.GetSavingGoalByIdAsync(1, Arg.Any<CancellationToken>()).Returns(goal);
        var scopes = new List<DashboardDataInvalidationScope>();
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        messenger.Register<DashboardDataInvalidatedMessage>(recipient, (_, message) => scopes.Add(message.Value));

        var result = await new TransactionPersistenceHelper(appData, messenger)
            .SaveAsync(new(), pending, new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(90m, account.Balance);
        Assert.Equal(110m, goal.CurrentAmount);
        await appData.Received(1).AddTagAsync(Arg.Is<Tag>(tag => !tag.IsSystemTag), Arg.Any<CancellationToken>());
        Assert.Contains(DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.SavingGoals |
                        DashboardDataInvalidationScope.Notifications, scopes);
    }

    [Fact]
    public async Task TransactionPopupVMPersistence_Delete_RevertsBalanceAndPublishesHistoryAnd_Invalidation()
    {
        var account = CreateAccount();
        account.Balance = 90m;
        var transaction = CreateTransaction(account);
        var appData = CreateAppData(account, transaction);
        var messenger = new WeakReferenceMessenger();
        var invalidations = new List<DashboardDataInvalidationScope>();
        var histories = 0;
        var recipient = new object();
        messenger.Register<DashboardDataInvalidatedMessage>(recipient, (_, message) => invalidations.Add(message.Value));
        messenger.Register<RecordLogMemoryMessage>(recipient, (_, _) => histories++);

        var result = await new TransactionPersistenceHelper(appData, messenger)
            .DeleteAsync(new TransactionVM { Id = transaction.Id });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(100m, account.Balance);
        appData.Received(1).RemoveTransaction(transaction);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(1, histories);
        Assert.Contains(DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications,
            invalidations);
    }

    [Fact]
    public void TransactionPopupVMPersistence_Initialize_CloneAndDiscardProcessingAreWrite_Free()
    {
        RunInSta(() =>
        {
            var account = CreateAccountVm();
            var appData = CreateAppData(CreateAccount(), CreateTransaction(CreateAccount()));
            var vm = TransactionPopupVMFactory.Create(CreateMainViewModel([account]), appData);
            appData.ClearReceivedCalls();

            vm.InitializeAsync().GetAwaiter().GetResult();
            vm.SwitchToCloneAddMode();
            vm.InitializeRecurringProcessing([
                new RecurringTransactionVM
                {
                    Id = 9,
                    Name = "Recurring",
                    Amount = 10m,
                    Type = RecurringTransactionType.Expense,
                    Source = account,
                    Tag = new TagVM { Id = 1, Name = "General" }
                }
            ]);
            appData.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            appData.DidNotReceive().AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
            appData.DidNotReceive().RemoveTransaction(Arg.Any<Transaction>());
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_Clone_SaveCreatesANewTransaction_Id()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            var account = CreateAccount();
            var appData = CreateAppData(account, CreateTransaction(account));
            Transaction? added = null;
            appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    added = call.Arg<Transaction>();
                    added.Id = 88;
                });
            var vm = TransactionPopupVMFactory.Create(CreateMainViewModel([accountVm]), appData);
            vm.InitializeView(CreateTransactionVm(accountVm));
            vm.SwitchToCloneAddMode();

            var result = vm.SaveAsync(false).GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(88, added?.Id);
            _ = appData.DidNotReceive().GetTransactionByIdAsync(42, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_Processing_BackAfterNextRestoresTheQueued_Item()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            var account = CreateAccount();
            var firstPersisted = CreateTransaction(account);
            firstPersisted.Id = 100;
            var appData = CreateAppData(account, firstPersisted);
            var nextId = 100;
            var added = new List<Transaction>();
            appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var transaction = call.Arg<Transaction>();
                    transaction.Id = nextId++;
                    added.Add(transaction);
                });
            var messenger = new WeakReferenceMessenger();
            var scopes = new List<DashboardDataInvalidationScope>();
            var recipient = new object();
            messenger.Register<DashboardDataInvalidatedMessage>(recipient,
                (_, message) => scopes.Add(message.Value));
            try
            {
                var vm = TransactionPopupVMFactory.Create(
                    CreateMainViewModel([accountVm]), appData, messenger: messenger);
                vm.InitializeAsync().GetAwaiter().GetResult();
                var first = new RecurringTransactionVM
                {
                    Id = 1, Name = "First", Amount = 10m, Type = RecurringTransactionType.Expense,
                    Category = ExpenseCategory.Needs, Source = accountVm,
                    Tag = new TagVM { Id = 1, Name = "General" }
                };
                var second = new RecurringTransactionVM
                {
                    Id = 2, Name = "Second", Amount = 20m, Type = RecurringTransactionType.Expense,
                    Category = ExpenseCategory.Needs, Source = accountVm,
                    Tag = new TagVM { Id = 1, Name = "General" }
                };
                vm.InitializeRecurringProcessing([first, second]);
                scopes.Clear();

                Assert.True(vm.FinishQueuedTransactionsAsync().GetAwaiter().GetResult().IsSuccess);
                Assert.Equal(2, added.Count);
                appData.DidNotReceive().UpdateTransaction(firstPersisted);
            }
            finally
            {
                messenger.UnregisterAll(recipient);
            }
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_Processing_InitializesGoalRepaymentAndRecurringStateBeforeAsync_Initialization()
    {
        RunInSta(() =>
        {
            var source = CreateAccountVm();
            var credit = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                SpentAmount = 50m,
                DeductSource = source.Id,
                IsEnabled = true
            };
            var vm = TransactionPopupVMFactory.Create(CreateMainViewModel([source, credit]),
                CreateAppData(CreateAccount(), CreateTransaction(CreateAccount())));

            vm.InitializeGoalProcessing([new SavingGoalVM { Id = 1, Name = "Goal" }]);
            Assert.NotNull(vm.LoadedTransaction);
            vm.InitializeRepaymentProcessing([credit]);
            Assert.NotNull(vm.LoadedTransaction);
            vm.InitializeRecurringProcessing([
                new RecurringTransactionVM
                {
                    Id = 3, Name = "Recurring", Amount = 10m, Type = RecurringTransactionType.Expense,
                    Source = source, Category = ExpenseCategory.Needs,
                    Tag = new TagVM { Id = 1, Name = "General" }
                }
            ]);
            Assert.NotNull(vm.LoadedTransaction);
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_Goal_ProcessingPersistsQueuedContributionsOn_Finish()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            var account = CreateAccount();
            var initialTransaction = CreateTransaction(account);
            var appData = CreateAppData(account, initialTransaction);
            var goal = new SavingGoal { Id = 1, Name = "Emergency", CurrentAmount = 100m };
            var added = new List<Transaction>();
            var nextId = 300;
            appData.GetSavingGoalByIdAsync(1, Arg.Any<CancellationToken>()).Returns(goal);
            appData.GetTransactionByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Transaction?>(added.FirstOrDefault(item => item.Id == call.Arg<int>())));
            appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var transaction = call.Arg<Transaction>();
                    transaction.Id = nextId++;
                    added.Add(transaction);
                });
            appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Transaction>>(added));

            var peers = TransactionPopupVMFactory.CreatePeers(CreateMainViewModel([accountVm]), appData);
            var vm = peers.Popup;
            vm.InitializeGoalProcessing([
                new SavingGoalVM { Id = 1, Name = "Emergency" },
                new SavingGoalVM { Id = 1, Name = "Emergency" }
            ]);

            vm.AmountText = 10m;
            peers.Bulk.SelectedQueuedTransaction = peers.Bulk.QueuedTransactions[1];
            vm.AmountText = 20m;
            peers.Bulk.SelectedQueuedTransaction = peers.Bulk.QueuedTransactions[0];
            vm.AmountText = 15m;
            Assert.Empty(added);
            Assert.Equal([15m, 20m], peers.Bulk.QueuedTransactions.Select(item => item.Amount));
            var result = vm.FinishQueuedTransactionsAsync().GetAwaiter().GetResult();
            Assert.True(result.IsSuccess,
                $"{result.ErrorMessage}; added={added.Count}; form={vm.AmountText}; queue={string.Join(',', peers.Bulk.QueuedTransactions.Select(item => item.Amount))}");

            Assert.Equal(2, added.Count);
            appData.DidNotReceive().UpdateTransaction(Arg.Any<Transaction>());
            Assert.Equal(135m, goal.CurrentAmount);
        });
    }

    [Fact]
    public void TransactionPopupVMPersistence_Repayment_ProcessingPersistsQueuedPairsOn_Finish()
    {
        RunInSta(() =>
        {
            var checkingVm = CreateAccountVm();
            var creditOneVm = new AccountVM
            {
                Id = 2, Name = "Visa", AccountType = AccountType.Credit, SpentAmount = 100m,
                DeductSource = checkingVm.Id, IsEnabled = true
            };
            var creditTwoVm = new AccountVM
            {
                Id = 3, Name = "Mastercard", AccountType = AccountType.Credit, SpentAmount = 100m,
                DeductSource = checkingVm.Id, IsEnabled = true
            };
            var checking = CreateAccount();
            var creditOne = new Account { Id = 2, Name = "Visa", AccountType = AccountType.Credit, SpentAmount = 100m };
            var creditTwo = new Account { Id = 3, Name = "Mastercard", AccountType = AccountType.Credit, SpentAmount = 100m };
            var appData = CreateAppData(checking, CreateTransaction(checking));
            appData.GetAccountByIdAsync(2, Arg.Any<CancellationToken>()).Returns(creditOne);
            appData.GetAccountByIdAsync(3, Arg.Any<CancellationToken>()).Returns(creditTwo);
            var added = new List<Transaction>();
            var nextId = 400;
            appData.GetTransactionByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Transaction?>(added.FirstOrDefault(item => item.Id == call.Arg<int>())));
            appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Transaction>>(added));
            appData.When(data => data.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var transaction = call.Arg<Transaction>();
                    transaction.Id = nextId++;
                    added.Add(transaction);
                });

            var peers = TransactionPopupVMFactory.CreatePeers(
                CreateMainViewModel([checkingVm, creditOneVm, creditTwoVm]),
                appData,
                [checkingVm, creditOneVm, creditTwoVm]);
            var vm = peers.Popup;
            vm.InitializeRepaymentProcessing([creditOneVm, creditTwoVm]);

            peers.Bulk.SelectedQueuedTransaction = peers.Bulk.QueuedTransactions[0];
            vm.AmountText = 50m;
            Assert.Empty(added);
            Assert.Equal(50m, vm.AmountText);
            Assert.True(vm.FinishQueuedTransactionsAsync().GetAwaiter().GetResult().IsSuccess);

            Assert.Equal(4, added.Count);
            Assert.Equal(350m, checking.Balance);
            Assert.Equal(50m, creditOne.SpentAmount);
            Assert.Equal(0m, creditTwo.SpentAmount);
            appData.DidNotReceive().UpdateTransaction(Arg.Any<Transaction>());
        });
    }

    private static IAppDataService CreateAppData(Account account, Transaction transaction)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);
        appData.GetAccountByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        appData.GetTagByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Tag
        {
            Id = 1,
            Name = "General",
            HexCode = "#22C55E"
        });
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>
        ([
            new Tag { Id = 1, Name = "General", HexCode = "#22C55E" }
        ]));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation());
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return appData;
    }

    private static TransactionVM CreateTransactionVm(AccountVM account) => new()
    {
        Id = 42,
        Type = TransactionType.Expense,
        SourceAccountId = account.Id,
        Account = account,
        Name = "Original",
        Amount = 10m,
        OccurredOn = new DateTime(2026, 7, 19),
        ExpenseCategory = ExpenseCategory.Needs,
        Tag = new TagVM { Id = 1, Name = "General", HexCode = "#22C55E" }
    };

    private static Transaction CreateTransaction(Account account) => new()
    {
        Id = 42,
        Type = TransactionType.Expense,
        SourceAccountId = account.Id,
        Account = account,
        Name = "Original",
        Amount = 10m,
        OccurredOn = new DateTime(2026, 7, 19),
        ExpenseCategory = ExpenseCategory.Needs,
        TagId = 1,
        Tag = new Tag { Id = 1, Name = "General", HexCode = "#22C55E" }
    };

    private static AccountVM CreateAccountVm() => new()
    {
        Id = 1,
        Name = "Checking",
        AccountType = AccountType.Checking,
        Balance = 500m,
        IsEnabled = true,
        IsDefault = true
    };

    private static Account CreateAccount() => new()
    {
        Id = 1,
        Name = "Checking",
        AccountType = AccountType.Checking,
        Balance = 500m,
        IsEnabled = true,
        IsDefault = true
    };

    private static MainVM CreateMainViewModel(IReadOnlyList<AccountVM> accounts)
    {
        var messenger = new WeakReferenceMessenger();
        var mapper = Substitute.For<IMapper>();
        var unitOfWork = CreateUnitOfWork();
        var dataOperationRunner = new InlineDataOperationRunner(unitOfWork);
        mapper.Map<IReadOnlyList<TransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<AccountVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<TagVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<Fluxo.Core.DTO.RecurringTransactionDto>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<RecurringTransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<Fluxo.Core.DTO.SavingGoalDto>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<SavingGoalVM>>(Arg.Any<object>()).Returns([]);

        var transactionService = Substitute.For<ITransactionService>();
        transactionService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TransactionDto>>([]));
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.AccountDto>>([]));
        var tagService = Substitute.For<ITagService>();
        tagService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TagDto>>([]));

        var dashboard = new DashboardVM(
            new NotificationPanelVM(transactionService, accountService, dataOperationRunner, mapper, messenger: messenger),
            new BudgetAllocationPanelVM(transactionService, accountService, tagService, dataOperationRunner, mapper, messenger),
            new SpentAllowancePanelVM(transactionService, accountService, dataOperationRunner, mapper, messenger),
            new SavingGoalsPanelVM(dataOperationRunner, mapper, messenger),
            new UpcomingEventsPanelVM(dataOperationRunner, mapper, messenger: messenger),
            new MainViewModeToggleVM(messenger));
        var main = new MainVM(dataOperationRunner, dashboard, new DaySpinnerVM(messenger), null);

        foreach (var account in accounts)
            main.BudgetPanel.Accounts.Add(account);

        main.BudgetPanel.Tags =
        [
            new TagVM { Id = 1, Name = "General", HexCode = "#22C55E" }
        ];
        main.BudgetPanel.OtherTags = [];
        main.SavingGoalsPanel.SavingGoals.Add(new SavingGoalVM
        {
            Id = 1,
            Name = "Goal",
            TargetAmount = 500m,
            CurrentAmount = 100m
        });
        return main;
    }

    private static IUnitOfWork CreateUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var userSettings = Substitute.For<IUserSettingsRepository>();
        userSettings.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<UserSettings>>([]));
        unitOfWork.UserSettings.Returns(userSettings);
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        unitOfWork.Transactions.Returns(transactions);
        var allocation = Substitute.For<IBudgetAllocationRepository>();
        allocation.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<BudgetAllocation?>(new BudgetAllocation()));
        unitOfWork.BudgetAllocation.Returns(allocation);
        return unitOfWork;
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
