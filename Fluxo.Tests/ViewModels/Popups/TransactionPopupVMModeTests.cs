using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Persistence;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using NSubstitute.Extensions;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMModeTests
{
    [Fact]
    public void InitializeAsync_add_mode_creates_two_distinct_equal_entities_once()
    {
        RunInSta(() =>
        {
            var (vm, appData) = CreateVm();
            appData.ClearReceivedCalls();

            vm.InitializeAsync().GetAwaiter().GetResult();
            var loaded = vm.LoadedTransaction;
            var pending = vm.PendingTransaction;
            vm.InitializeAsync().GetAwaiter().GetResult();

            Assert.NotSame(loaded, pending);
            Assert.Same(loaded, vm.LoadedTransaction);
            Assert.Same(pending, vm.PendingTransaction);
            Assert.Equal(loaded, pending);
            Assert.Equal(0, pending.Id);
            Assert.Empty(appData.ReceivedCalls());
        });
    }

    [Fact]
    public void InitializeView_retains_loaded_identity_and_clears_pending_identity()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            var transaction = CreateTransaction();

            vm.InitializeView(transaction);
            vm.InitializeAsync().GetAwaiter().GetResult();

            Assert.Equal(42, vm.LoadedTransaction.Id);
            Assert.Equal(transaction.LoggedOn, vm.LoadedTransaction.LoggedOn);
            Assert.Equal(transaction.ParentTransactionId, vm.LoadedTransaction.ParentTransactionId);
            Assert.True(vm.LoadedTransaction.IsForDeletion);
            Assert.Equal(0, vm.PendingTransaction.Id);
            Assert.Equal(default, vm.PendingTransaction.LoggedOn);
            Assert.Null(vm.PendingTransaction.ParentTransactionId);
            Assert.False(vm.PendingTransaction.IsForDeletion);
            Assert.Equal(vm.LoadedTransaction, vm.PendingTransaction);
        });
    }

    [Fact]
    public void View_mapping_is_isolated_from_the_supplied_entity()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            var transaction = CreateTransaction();

            vm.InitializeView(transaction);
            transaction.Name = "Changed outside";
            transaction.Account.Name = "Changed account";
            transaction.Tag!.Name = "Changed tag";

            Assert.Equal("Coffee", vm.LoadedTransaction.Name);
            Assert.Equal("Checking", vm.LoadedTransaction.Account.Name);
            Assert.Equal("General", vm.LoadedTransaction.Tag!.Name);
            Assert.Equal("Coffee", vm.PendingTransaction.Name);
        });
    }

    [Fact]
    public void Clone_reuses_pending_entity_and_keeps_loaded_baseline_unchanged()
    {
        RunInSta(() =>
        {
            var (vm, appData) = CreateVm();
            vm.InitializeView(CreateTransaction());
            vm.InitializeAsync().GetAwaiter().GetResult();
            var loaded = vm.LoadedTransaction;
            var pending = vm.PendingTransaction;
            var name = vm.NameText;
            var amount = vm.AmountText;
            appData.ClearReceivedCalls();

            vm.SwitchToCloneAddMode();

            Assert.Same(loaded, vm.LoadedTransaction);
            Assert.Same(pending, vm.PendingTransaction);
            Assert.Equal(0, vm.PendingTransaction.Id);
            Assert.Equal(default, vm.PendingTransaction.LoggedOn);
            Assert.Null(vm.PendingTransaction.ParentTransactionId);
            Assert.False(vm.PendingTransaction.IsForDeletion);
            Assert.Equal(vm.LoadedTransaction, vm.PendingTransaction);
            Assert.Equal("Add New Transaction", vm.PopupTitle);
            Assert.True(vm.CanContinue);
            Assert.Equal(name, vm.NameText);
            Assert.Equal(amount, vm.AmountText);
            Assert.Empty(appData.ReceivedCalls());
        });
    }

    private static TransactionVM CreateTransaction()
    {
        var account = CreateCheckingAccount();
        return new TransactionVM
        {
            Id = 42,
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            Account = account,
            Name = "Coffee",
            Amount = 5m,
            OccurredOn = new DateTime(2026, 7, 19),
            LoggedOn = new DateTime(2026, 7, 19, 10, 30, 0),
            Notes = "Morning",
            ExpenseCategory = ExpenseCategory.Needs,
            Tag = new TagVM { Id = 1, Name = "General", HexCode = "#22C55E" },
            ParentTransactionId = 7,
            IsForDeletion = true
        };
    }

    private static (TransactionPopupVM ViewModel, IAppDataService AppData) CreateVm(IReadOnlyList<AccountVM>? accounts = null)
    {
        accounts ??= [CreateCheckingAccount()];
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new BudgetAllocation()));
        return (new TransactionPopupVM(CreateMainViewModel(accounts), appData), appData);
    }

    private static AccountVM CreateCheckingAccount() => new()
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
        transactionService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TransactionDto>>([]));
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.AccountDto>>([]));
        var tagService = Substitute.For<ITagService>();
        tagService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TagDto>>([]));

        var dashboard = new DashboardVM(
            new NotificationPanelVM(
                transactionService,
                accountService,
                dataOperationRunner,
                mapper,
                messenger: messenger),
            new BudgetAllocationPanelVM(
                transactionService,
                accountService,
                tagService,
                dataOperationRunner,
                mapper,
                messenger),
            new SpentAllowancePanelVM(
                transactionService,
                accountService,
                dataOperationRunner,
                mapper,
                messenger),
            new SavingGoalsPanelVM(dataOperationRunner, mapper, messenger),
            new UpcomingEventsPanelVM(dataOperationRunner, mapper, messenger: messenger),
            new MainViewModeToggleVM(messenger));
        var main = new MainVM(dataOperationRunner, dashboard, new DaySpinnerVM(messenger), null);

        foreach (var account in accounts)
            main.BudgetPanel.Accounts.Add(account);

        main.BudgetPanel.Tags = new ObservableCollection<TagVM>
        {
            new() { Id = 1, Name = "General", HexCode = "#22C55E" }
        };
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
        userSettings.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserSettings>>([]));
        unitOfWork.UserSettings.Returns(userSettings);

        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        unitOfWork.Transactions.Returns(transactions);

        var budgetAllocation = Substitute.For<IBudgetAllocationRepository>();
        budgetAllocation.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetAllocation?>(new BudgetAllocation()));
        unitOfWork.BudgetAllocation.Returns(budgetAllocation);
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
