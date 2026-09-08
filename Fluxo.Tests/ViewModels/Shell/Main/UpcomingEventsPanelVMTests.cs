using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public class UpcomingEventsPanelVMTests
{
    [Fact]
    public void UpcomingEventsPanelVM_LoadAsync_KeepsBoundCollectionUpdatesOnDispatcher()
    {
        RunInSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var recurringCompletion = new TaskCompletionSource<IReadOnlyList<RecurringTransaction>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var appData = Substitute.For<Fluxo.Core.Interfaces.Services.IAppDataService>();
            appData.GetRecurringTransactionsAsync(Arg.Any<CancellationToken>())
                .Returns(recurringCompletion.Task);
            appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SavingGoal>>([]));
            appData.GetAccountsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Account>>([]));
            var mapper = Substitute.For<IMapper>();
            mapper.Map<IReadOnlyList<RecurringTransactionVM>>(Arg.Any<object>()).Returns([]);
            var vm = new UpcomingEventsPanelVM(appData, mapper, messenger: new WeakReferenceMessenger());
            _ = CollectionViewSource.GetDefaultView(vm.Events);

            var load = vm.LoadAsync();
            _ = Task.Run(() => recurringCompletion.SetResult([]));
            var frame = new DispatcherFrame();
            _ = load.ContinueWith(
                _ => dispatcher.BeginInvoke(() => frame.Continue = false),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);

            load.GetAwaiter().GetResult();
            Assert.Empty(vm.Events);
        });
    }

    [Fact]
    public void UpcomingEventsPanelVM_UpcomingEventItemVM_FormatsMonthAndDay()
    {
        var item = new UpcomingEventItemVM(
            new DateOnly(2026, 6, 5),
            "Rent",
            "12,000",
            "Expense");

        Assert.Equal("JUN", item.MonthText);
        Assert.Equal("05", item.DayText);
        Assert.Equal("Rent", item.Title);
        Assert.Equal("12,000", item.AmountText);
        Assert.Equal("Expense", item.EventTypeText);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_IncludesRecurringTransactionsDueWithinNext14Days()
    {
        var today = new DateTime(2026, 6, 14);
        var dueDate = today.AddDays(3);
        var vm = CreateVm(
            today,
            recurringTransactions:
            [
                new RecurringTransaction
                {
                    Id = 1,
                    Name = "Rent",
                    Amount = 12000m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = dueDate.Day,
                    Type = RecurringTransactionType.Expense,
                    IsEnabled = true
                }
            ],
            savingGoals: []);

        await vm.LoadAsync();

        var item = Assert.Single(vm.Events);
        Assert.True(vm.HasEvents);
        Assert.Equal(DateOnly.FromDateTime(dueDate), item.Date);
        Assert.Equal("Rent", item.Title);
        Assert.Equal("12,000", item.AmountText);
        Assert.Equal("Expense", item.EventTypeText);
    }

    [Theory]
    [InlineData(RecurringTransactionType.Income, "Income")]
    [InlineData(RecurringTransactionType.Expense, "Expense")]
    [InlineData(RecurringTransactionType.GoalUpdate, "Goal")]
    public async Task UpcomingEventsPanelVM_LoadAsync_RecurringTransactionTypeTextUsesTransactionType(
        RecurringTransactionType transactionType,
        string expectedTypeText)
    {
        var today = new DateTime(2026, 6, 14);
        var vm = CreateVm(
            today,
            recurringTransactions:
            [
                new RecurringTransaction
                {
                    Id = 1,
                    Name = "Event",
                    Amount = 100m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = today.Day,
                    Type = transactionType,
                    IsEnabled = true
                }
            ],
            savingGoals: []);

        await vm.LoadAsync();

        var item = Assert.Single(vm.Events);
        Assert.Equal(expectedTypeText, item.EventTypeText);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_CreditRecurringExpenseTypeText_UsesPayment()
    {
        var today = new DateTime(2026, 6, 14);
        var vm = CreateVm(
            today,
            recurringTransactions:
            [
                new RecurringTransaction
                {
                    Id = 1,
                    Name = "Card payment",
                    Amount = 500m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = today.Day,
                    Type = RecurringTransactionType.Expense,
                    Source = new Account { Id = 3, Name = "Visa", AccountType = AccountType.Credit },
                    IsEnabled = true
                }
            ],
            savingGoals: []);

        await vm.LoadAsync();

        var item = Assert.Single(vm.Events);
        Assert.Equal("Payment", item.EventTypeText);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_ExcludesDisabledAndOutOfWindowRecurringTransactions()
    {
        var today = new DateTime(2026, 6, 14);
        var vm = CreateVm(
            today,
            recurringTransactions:
            [
                new RecurringTransaction
                {
                    Id = 1,
                    Name = "Disabled",
                    Amount = 100m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = today.Day,
                    Type = RecurringTransactionType.Expense,
                    IsEnabled = false
                },
                new RecurringTransaction
                {
                    Id = 2,
                    Name = "Outside",
                    Amount = 200m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = today.AddDays(-1).Day,
                    Type = RecurringTransactionType.Expense,
                    IsEnabled = true
                }
            ],
            savingGoals: []);

        await vm.LoadAsync();

        Assert.Empty(vm.Events);
        Assert.False(vm.HasEvents);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_IncludesCreditPaymentDueWithinNext14Days()
    {
        var today = new DateTime(2026, 6, 14);
        var vm = CreateVm(
            today,
            recurringTransactions: [],
            savingGoals: [],
            accounts:
            [
                new Account
                {
                    Id = 1,
                    Name = "Visa",
                    AccountType = AccountType.Credit,
                    SpentAmount = 12500m,
                    MonthlyDueDate = 18,
                    IsEnabled = true
                }
            ]);

        await vm.LoadAsync();

        var item = Assert.Single(vm.Events);
        Assert.True(vm.HasEvents);
        Assert.Equal(new DateOnly(2026, 6, 18), item.Date);
        Assert.Equal("Visa", item.Title);
        Assert.Equal("12,500", item.AmountText);
        Assert.Equal("Payment", item.EventTypeText);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_ExcludesIneligibleCreditPayments()
    {
        var today = new DateTime(2026, 6, 14);
        var vm = CreateVm(
            today,
            recurringTransactions: [],
            savingGoals: [],
            accounts:
            [
                new Account { Id = 1, Name = "Disabled", AccountType = AccountType.Credit, SpentAmount = 100m, MonthlyDueDate = 18, IsEnabled = false },
                new Account { Id = 2, Name = "Paid", AccountType = AccountType.Credit, SpentAmount = 0m, MonthlyDueDate = 18, IsEnabled = true },
                new Account { Id = 3, Name = "Checking", AccountType = AccountType.Checking, SpentAmount = 100m, MonthlyDueDate = 18, IsEnabled = true },
                new Account { Id = 4, Name = "No due date", AccountType = AccountType.Credit, SpentAmount = 100m, IsEnabled = true },
                new Account { Id = 5, Name = "Outside", AccountType = AccountType.Credit, SpentAmount = 100m, MonthlyDueDate = 1, IsEnabled = true }
            ]);

        await vm.LoadAsync();

        Assert.Empty(vm.Events);
        Assert.False(vm.HasEvents);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_IncludesGoalDeadlineWithAmountLeftText()
    {
        var today = new DateTime(2026, 6, 14);
        var deadline = today.AddDays(10);
        var vm = CreateVm(
            today,
            recurringTransactions: [],
            savingGoals:
            [
                new SavingGoal
                {
                    Id = 1,
                    Name = "Emergency Fund",
                    CurrentAmount = 25000m,
                    TargetAmount = 50000m,
                    SavingEndDate = deadline
                }
            ]);

        await vm.LoadAsync();

        var item = Assert.Single(vm.Events);
        Assert.Equal(DateOnly.FromDateTime(deadline), item.Date);
        Assert.Equal("Emergency Fund deadline", item.Title);
        Assert.Equal("25,000 left from 50,000", item.AmountText);
        Assert.Equal("Goal Deadline", item.EventTypeText);
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_WhenGoalHasRecurringAndDeadlineInWindow_ShowsBothRows()
    {
        var today = new DateTime(2026, 6, 14);
        var recurringDate = today.AddDays(4);
        var deadline = today.AddDays(8);
        var goal = new SavingGoal
        {
            Id = 7,
            Name = "Laptop",
            CurrentAmount = 400m,
            TargetAmount = 1000m,
            SavingEndDate = deadline
        };
        var vm = CreateVm(
            today,
            recurringTransactions:
            [
                new RecurringTransaction
                {
                    Id = 10,
                    Name = "Laptop contribution",
                    Amount = 100m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = recurringDate.Day,
                    Type = RecurringTransactionType.GoalUpdate,
                    GoalId = goal.Id,
                    Goal = goal,
                    IsEnabled = true
                }
            ],
            savingGoals: [goal]);

        await vm.LoadAsync();

        Assert.Equal(2, vm.Events.Count);
        Assert.Contains(vm.Events, item =>
            item.Title == "Laptop contribution" &&
            item.AmountText == "100" &&
            item.EventTypeText == "Goal");
        Assert.Contains(vm.Events, item =>
            item.Title == "Laptop deadline" &&
            item.AmountText == "600 left from 1,000" &&
            item.EventTypeText == "Goal Deadline");
    }

    [Fact]
    public async Task UpcomingEventsPanelVM_LoadAsync_OrdersEventsByDateThenTitle()
    {
        var today = new DateTime(2026, 6, 14);
        var sameDay = today.AddDays(2);
        var vm = CreateVm(
            today,
            recurringTransactions:
            [
                new RecurringTransaction
                {
                    Id = 1,
                    Name = "Zoo",
                    Amount = 1m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = sameDay.Day,
                    Type = RecurringTransactionType.Expense,
                    IsEnabled = true
                },
                new RecurringTransaction
                {
                    Id = 2,
                    Name = "Alpha",
                    Amount = 2m,
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = sameDay.Day,
                    Type = RecurringTransactionType.Expense,
                    IsEnabled = true
                }
            ],
            savingGoals:
            [
                new SavingGoal
                {
                    Id = 1,
                    Name = "Before",
                    CurrentAmount = 0m,
                    TargetAmount = 10m,
                    SavingEndDate = today.AddDays(1)
                }
            ],
            accounts:
            [
                new Account
                {
                    Id = 1,
                    Name = "Card",
                    AccountType = AccountType.Credit,
                    SpentAmount = 3m,
                    MonthlyDueDate = sameDay.Day,
                    IsEnabled = true
                }
            ]);

        await vm.LoadAsync();

        Assert.Equal(["Before deadline", "Alpha", "Card", "Zoo"], vm.Events.Select(item => item.Title).ToArray());
    }

    private static UpcomingEventsPanelVM CreateVm(
        DateTime today,
        IReadOnlyList<RecurringTransaction> recurringTransactions,
        IReadOnlyList<SavingGoal> savingGoals,
        IReadOnlyList<Account>? accounts = null)
    {
        accounts ??= [];

        var recurringRepository = Substitute.For<IRecurringTransactionRepository>();
        recurringRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(recurringTransactions));

        var savingGoalRepository = Substitute.For<ISavingGoalRepository>();
        savingGoalRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(savingGoals));

        var accountRepository = Substitute.For<IAccountRepository>();
        accountRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(accounts));

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.RecurringTransactions.Returns(recurringRepository);
        unitOfWork.SavingGoals.Returns(savingGoalRepository);
        unitOfWork.Accounts.Returns(accountRepository);

        var mapper = Substitute.For<IMapper>();
        mapper.Map<IReadOnlyList<RecurringTransactionVM>>(Arg.Any<object>())
            .Returns(recurringTransactions.Select(transaction => new RecurringTransactionVM
            {
                Id = transaction.Id,
                Name = transaction.Name,
                Amount = transaction.Amount,
                RecurringPeriod = transaction.RecurringPeriod,
                RecurringTime = transaction.RecurringTime,
                Type = transaction.Type,
                Source = transaction.Source is null
                    ? new AccountVM()
                    : new AccountVM
                    {
                        Id = transaction.Source.Id,
                        Name = transaction.Source.Name,
                        AccountType = transaction.Source.AccountType
                    },
                Goal = transaction.Goal is null
                    ? null
                    : new SavingGoalVM
                    {
                        Id = transaction.Goal.Id,
                        Name = transaction.Goal.Name,
                        CurrentAmount = transaction.Goal.CurrentAmount,
                        TargetAmount = transaction.Goal.TargetAmount,
                        SavingEndDate = transaction.Goal.SavingEndDate
                    },
                IsEnabled = transaction.IsEnabled
            }).ToList());

        return new UpcomingEventsPanelVM(
            new Fluxo.Services.Persistence.AppDataService(unitOfWork),
            mapper,
            () => today,
            new WeakReferenceMessenger());
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
