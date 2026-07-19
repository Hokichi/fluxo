using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
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
    public void SaveAsync_Edit_updates_the_loaded_transaction_id()
    {
        RunInSta(() =>
        {
            var accountVm = CreateAccountVm();
            var account = CreateAccount();
            var transaction = CreateTransaction(account);
            var appData = CreateAppData(account, transaction);
            var vm = new TransactionPopupVM(CreateMainViewModel([accountVm]), appData);
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
