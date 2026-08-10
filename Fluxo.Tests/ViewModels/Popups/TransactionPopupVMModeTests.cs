using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
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
    public void InitializeAsync_add_mode_defaults_selected_time_to_current_time()
    {
        RunInSta(() =>
        {
            var before = DateTime.Now;
            var (vm, _) = CreateProductionVm(TransactionPopupRequest.Add());

            vm.InitializeAsync().GetAwaiter().GetResult();

            var after = DateTime.Now;
            Assert.Equal(before.Date, vm.SelectedDate.Date);
            Assert.InRange(vm.SelectedTime, before.TimeOfDay, after.TimeOfDay);
        });
    }

    [Fact]
    public void InitializeView_separates_transaction_date_and_time()
    {
        RunInSta(() =>
        {
            var transaction = CreateTransaction();
            transaction.OccurredOn = new DateTime(2026, 8, 10, 14, 30, 0);
            var (vm, _) = CreateProductionVm(TransactionPopupRequest.View(transaction));

            vm.InitializeAsync().GetAwaiter().GetResult();

            Assert.Equal(transaction.OccurredOn.Date, vm.SelectedDate);
            Assert.Equal(transaction.OccurredOn.TimeOfDay, vm.SelectedTime);
        });
    }

    [Fact]
    public void Editing_viewed_transaction_combines_selected_date_and_time()
    {
        RunInSta(() =>
        {
            var transaction = CreateTransaction();
            var (vm, _) = CreateProductionVm(TransactionPopupRequest.View(transaction));
            vm.InitializeAsync().GetAwaiter().GetResult();
            vm.SelectedDate = new DateTime(2026, 8, 10);
            vm.SelectedTime = new TimeSpan(14, 30, 0);

            var input = vm.CreateTransactionEditInput();

            Assert.Equal(new DateTime(2026, 8, 10, 14, 30, 0), input.Date);
        });
    }

    [Fact]
    public void InitializeAsync_add_mode_creates_two_distinct_equal_entities_once()
    {
        RunInSta(() =>
        {
            var (vm, appData) = CreateProductionVm(TransactionPopupRequest.Add());
            appData.ClearReceivedCalls();

            vm.InitializeAsync().GetAwaiter().GetResult();
            var loaded = vm.LoadedTransaction;
            var pending = vm.PendingTransaction;
            vm.InitializeAsync().GetAwaiter().GetResult();

            Assert.NotSame(loaded, pending);
            Assert.Same(loaded, vm.LoadedTransaction);
            Assert.Same(pending, vm.PendingTransaction);
            Assert.True(loaded.HasSameValues(pending));
            Assert.Equal(0, pending.Id);
            var callsAfterFirstInitialization = appData.ReceivedCalls().Count();
            Assert.True(callsAfterFirstInitialization > 0);
            vm.InitializeAsync().GetAwaiter().GetResult();
            Assert.Equal(callsAfterFirstInitialization, appData.ReceivedCalls().Count());
        });
    }

    [Fact]
    public void InitializeView_retains_loaded_identity_and_clears_pending_identity()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateProductionVm(TransactionPopupRequest.View(CreateTransaction()));
            var transaction = CreateTransaction();
            vm.InitializeAsync().GetAwaiter().GetResult();

            Assert.Equal(42, vm.LoadedTransaction.Id);
            Assert.Equal(transaction.LoggedOn, vm.LoadedTransaction.LoggedOn);
            Assert.Equal(transaction.ParentTransactionId, vm.LoadedTransaction.ParentTransactionId);
            Assert.True(vm.LoadedTransaction.IsForDeletion);
            Assert.Equal(0, vm.PendingTransaction.Id);
            Assert.Equal(default, vm.PendingTransaction.LoggedOn);
            Assert.Null(vm.PendingTransaction.ParentTransactionId);
            Assert.False(vm.PendingTransaction.IsForDeletion);
            Assert.True(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
        });
    }

    [Fact]
    public void Discarding_viewed_transaction_notifies_split_tree_is_read_only()
    {
        RunInSta(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var appData = Substitute.For<IAppDataService>();
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
            appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
            appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new BudgetAllocation()));
            var (vm, _, splits) = TransactionPopupVMFactory.CreatePeers(
                CreateMainViewModel([CreateCheckingAccount()]), appData, messenger: messenger);
            vm.InitializeView(CreateTransaction());
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();
            var changes = new List<string?>();
            splits.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

            vm.DiscardEditingViewedTransaction();

            Assert.True(vm.IsViewOnly);
            Assert.False(splits.CanModifySplitTree);
            Assert.Contains(nameof(splits.CanModifySplitTree), changes);
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
            var (vm, appData) = CreateProductionVm(TransactionPopupRequest.View(CreateTransaction()));
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
            Assert.True(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
            Assert.Equal("Add New Transaction", vm.PopupTitle);
            Assert.Equal(name, vm.NameText);
            Assert.Equal(amount, vm.AmountText);
            Assert.Empty(appData.ReceivedCalls());
        });
    }

    [Fact]
    public void Goal_update_add_mode_seeds_generated_baseline_and_later_goal_change_updates_only_pending()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            vm.InitializeAsync().GetAwaiter().GetResult();

            vm.IsGoal = true;

            var loaded = vm.LoadedTransaction;
            var pending = vm.PendingTransaction;
            Assert.Equal("Goal Update for Goal", loaded.Name);
            Assert.Equal(1, loaded.SourceAccountId);
            Assert.Equal(1, loaded.GoalId);
            Assert.True(loaded.HasSameValues(pending));

            vm.SelectedGoal = new SavingGoalVM { Id = 2, Name = "Emergency Fund" };

            Assert.Same(loaded, vm.LoadedTransaction);
            Assert.Same(pending, vm.PendingTransaction);
            Assert.Equal("Goal Update for Goal", loaded.Name);
            Assert.Equal(1, loaded.GoalId);
            Assert.Equal("Goal Update for Emergency Fund", pending.Name);
            Assert.Equal(2, pending.GoalId);
            Assert.False(loaded.HasSameValues(pending));
        });
    }

    [Fact]
    public void Repayment_add_mode_seeds_generated_baseline_and_later_account_change_updates_only_pending()
    {
        RunInSta(() =>
        {
            var checking = CreateCheckingAccount();
            var visa = new AccountVM
            {
                Id = 2,
                Name = "Visa",
                AccountType = AccountType.Credit,
                IsEnabled = true,
                SpentAmount = 80m
            };
            var mastercard = new AccountVM
            {
                Id = 3,
                Name = "Mastercard",
                AccountType = AccountType.Credit,
                IsEnabled = true,
                SpentAmount = 120m
            };
            var (vm, _) = CreateVm([checking, visa, mastercard]);
            vm.InitializeAsync().GetAwaiter().GetResult();
            vm.SelectedRepaymentAccount = visa;

            vm.IsRepayment = true;

            var loaded = vm.LoadedTransaction;
            var pending = vm.PendingTransaction;
            Assert.Equal("Repayment to Visa", loaded.Name);
            Assert.Equal(80m, loaded.Amount);
            Assert.Equal(checking.Id, loaded.SourceAccountId);
            Assert.Equal(visa.Id, loaded.RepaymentAccountId);
            Assert.True(loaded.HasSameValues(pending));

            vm.SelectedRepaymentAccount = mastercard;

            Assert.Same(loaded, vm.LoadedTransaction);
            Assert.Same(pending, vm.PendingTransaction);
            Assert.Equal("Repayment to Visa", loaded.Name);
            Assert.Equal(80m, loaded.Amount);
            Assert.Equal(visa.Id, loaded.RepaymentAccountId);
            Assert.Equal("Repayment to Mastercard", pending.Name);
            Assert.Equal(120m, pending.Amount);
            Assert.Equal(mastercard.Id, pending.RepaymentAccountId);
            Assert.False(loaded.HasSameValues(pending));
        });
    }

    [Fact]
    public void View_edit_mode_starts_with_equal_loaded_and_pending_transactions()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();

            vm.InitializeView(CreateTransaction());
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.True(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
        });
    }

    [Fact]
    public void View_edit_mode_without_form_changes_has_no_pending_transaction_changes()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            vm.InitializeView(CreateTransaction());
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.False(vm.HasPendingTransactionChanges);
        });
    }

    [Fact]
    public void View_without_subtransactions_hides_side_panel()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            vm.InitializeView(CreateTransaction());

            Assert.False(vm.ShowSidePanel);
        });
    }

    [Fact]
    public void Edit_mode_shows_split_without_history_pinned_or_toggle()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            vm.InitializeView(CreateTransaction());
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.False(vm.ShowSidePanelToggle);
            Assert.True(vm.ShowSplitPanel);
            Assert.False(vm.ShowHistoryPanel);
            Assert.False(vm.ShowPinnedPanel);
            Assert.True(vm.CanEditTransactionName);

            vm.NameText = "Lunch";
            vm.AmountText = 20m;

            Assert.Equal("Lunch", vm.PendingTransaction.Name);
            Assert.Equal(20m, vm.PendingTransaction.Amount);
        });
    }

    [Fact]
    public void View_mode_keeps_pending_equal_when_the_loaded_goal_is_not_available()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            var transaction = CreateTransaction();
            transaction.GoalId = 99;
            transaction.Tag = null;
            transaction.ExpenseCategory = ExpenseCategory.Savings;
            transaction.Name = "Goal Update for Legacy Goal";

            vm.InitializeView(transaction);

            Assert.True(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
        });
    }

    [Fact]
    public void Begin_editing_goal_with_unavailable_generated_tag_keeps_loaded_relationships_unchanged()
    {
        RunInSta(() =>
        {
            var (vm, appData) = CreateVm();
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>(
            [
                new Tag { Id = 2, Name = "General", HexCode = "#22C55E" }
            ]));
            var transaction = CreateTransaction();
            transaction.Name = "Goal Update for Legacy Goal";
            transaction.GoalId = 99;
            transaction.ExpenseCategory = ExpenseCategory.Savings;
            transaction.Tag = new TagVM { Id = 10, Name = "Goal Update", HexCode = "#000000" };

            vm.InitializeView(transaction);
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.Equal("Modify Transaction", vm.PopupTitle);
            Assert.False(vm.HasChanges);
            Assert.Equal(99, vm.PendingTransaction.GoalId);
            Assert.Equal(10, vm.PendingTransaction.Tag?.Id);
        });
    }

    [Fact]
    public void Begin_editing_repayment_with_unavailable_generated_tag_keeps_loaded_relationships_unchanged()
    {
        RunInSta(() =>
        {
            var (vm, appData) = CreateVm();
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>(
            [
                new Tag { Id = 2, Name = "General", HexCode = "#22C55E" }
            ]));
            var transaction = CreateTransaction();
            transaction.Name = "Repayment to Legacy Card";
            transaction.RepaymentAccountId = 99;
            transaction.Tag = new TagVM { Id = 11, Name = "Balance Update", HexCode = "#000000" };

            vm.InitializeView(transaction);
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.Equal("Modify Transaction", vm.PopupTitle);
            Assert.False(vm.HasChanges);
            Assert.Equal(99, vm.PendingTransaction.RepaymentAccountId);
            Assert.Equal(11, vm.PendingTransaction.Tag?.Id);
        });
    }

    [Fact]
    public void Form_edits_update_pending_without_mutating_loaded_transaction()
    {
        RunInSta(() =>
        {
            var checking = CreateCheckingAccount();
            var savings = new AccountVM
            {
                Id = 2,
                Name = "Savings",
                AccountType = AccountType.Checking,
                IsEnabled = true
            };
            var (vm, _) = CreateVm([checking, savings]);
            vm.InitializeView(CreateTransaction());
            var loaded = vm.LoadedTransaction;

            vm.AmountText = 20m;
            vm.NameText = "Lunch";
            vm.SelectedAccount = savings;

            Assert.Equal(5m, loaded.Amount);
            Assert.Equal("Coffee", loaded.Name);
            Assert.Equal(checking.Id, loaded.SourceAccountId);
            Assert.Equal(20m, vm.PendingTransaction.Amount);
            Assert.Equal("Lunch", vm.PendingTransaction.Name);
            Assert.Equal(savings.Id, vm.PendingTransaction.SourceAccountId);
        });
    }

    [Fact]
    public void HasChanges_uses_pending_entity_equality_after_form_synchronization()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            vm.InitializeView(CreateTransaction());
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();

            Assert.False(vm.HasChanges);

            vm.NoteText = "Afternoon";

            Assert.False(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
            Assert.True(vm.HasChanges);
        });
    }

    [Fact]
    public void Add_mode_starts_unchanged_and_tracks_a_real_form_edit()
    {
        RunInSta(() =>
        {
            var (vm, _) = CreateVm();
            vm.InitializeAsync().GetAwaiter().GetResult();
            vm.BeginChangeTracking();

            Assert.True(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
            Assert.False(vm.HasChanges);

            vm.NoteText = "Memo";

            Assert.False(vm.LoadedTransaction.HasSameValues(vm.PendingTransaction));
            Assert.True(vm.HasChanges);
        });
    }

    [Fact]
    public void InitializeAsync_add_mode_overrides_draft_date_with_dashboard_daily_date()
    {
        RunInSta(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var selectedDate = new DateTime(2026, 7, 20);
            var recipient = new object();
            messenger.Register<DashboardDailyDateRequestedMessage>(recipient,
                (_, message) => message.Reply(selectedDate));
            var (vm, _) = CreateVm(messenger: messenger);
            vm.Configure(TransactionPopupRequest.Add(new TransactionPopupDraft(
                true, string.Empty, 0m, null, new DateTime(2026, 7, 19), string.Empty, null, null)));

            vm.InitializeAsync().GetAwaiter().GetResult();

            Assert.Equal(selectedDate, vm.SelectedDate);
            Assert.Equal(selectedDate, vm.PendingTransaction.OccurredOn);
        });
    }

    [Fact]
    public void Form_synchronization_does_not_call_app_data()
    {
        RunInSta(() =>
        {
            var (vm, appData) = CreateVm();
            vm.InitializeAsync().GetAwaiter().GetResult();
            appData.ClearReceivedCalls();

            vm.IsPinned = true;

            Assert.True(vm.PendingTransaction.IsPinned);
            Assert.Empty(appData.ReceivedCalls());
        });
    }

    [Fact]
    public void Removing_queued_transaction_sends_the_exact_transaction_instance()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        TransactionVM? removed = null;
        messenger.Register<object, TransactionBulkQueueRemoveRequestedMessage, TransactionPopupMessageToken>(
            recipient,
            TransactionPopupMessageToken.Default,
            (_, message) => removed = message.Value);
        var vm = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
        var transaction = CreateTransaction();

        vm.RemoveQueuedTransaction(transaction);

        Assert.Same(transaction, removed);
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

    private static (TransactionPopupVM ViewModel, IAppDataService AppData) CreateVm(
        IReadOnlyList<AccountVM>? accounts = null,
        IMessenger? messenger = null)
    {
        accounts ??= [CreateCheckingAccount()];
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new BudgetAllocation()));
        return (TransactionPopupVMFactory.Create(CreateMainViewModel(accounts), appData, messenger: messenger), appData);
    }

    private static (TransactionPopupVM ViewModel, IAppDataService AppData) CreateProductionVm(
        TransactionPopupRequest request)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns(
            [new Account
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                Balance = 500m,
                IsEnabled = true
            }]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(
            [new Tag { Id = 1, Name = "General", HexCode = "#22C55E" }]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>()).Returns([]);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation());

        var viewModel = new TransactionPopupVM(appData, new WeakReferenceMessenger());
        viewModel.Configure(request);
        return (viewModel, appData);
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
