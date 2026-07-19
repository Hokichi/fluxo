using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupMessengerTests
{
    [Fact]
    public async Task TransactionUpdate_RefreshesMatchingOpenPopup()
    {
        var messenger = new WeakReferenceMessenger();
        var account = CreateAccount();
        var appData = CreateAppData(account);
        var vm = new TransactionPopupVM(appData, messenger);
        vm.Configure(TransactionPopupRequest.View(CreateTransactionVm(account, "Before")));
        await vm.InitializeAsync();

        appData.GetTransactionByIdAsync(42, Arg.Any<CancellationToken>()).Returns(
            CreateTransaction(account, "After"));

        var message = messenger.Send(new TransactionPopupRefreshRequestedMessage(42));
        Assert.True(await message.Response);
        Assert.Equal("After", vm.NameText);
    }

    [Fact]
    public async Task SplitRequest_PublishesEntityIdWithoutWorkflowViewModel()
    {
        var messenger = new WeakReferenceMessenger();
        var account = CreateAccount();
        var vm = new TransactionPopupVM(CreateAppData(account), messenger);
        vm.Configure(TransactionPopupRequest.View(CreateTransactionVm(account, "Expense")));
        await vm.InitializeAsync();
        int? requestedId = null;
        var recipient = new object();
        messenger.Register<TransactionSplitRequestedMessage>(recipient,
            (_, message) => requestedId = message.Value);

        vm.RequestSplit();

        Assert.Equal(42, requestedId);
    }

    [Fact]
    public async Task SettingsRecurringRequest_PublishesInitializationDataInsteadOfPopupViewModel()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = Substitute.For<IAppDataService>();
        SettingsDialogRequest? request = null;
        var recipient = new object();
        messenger.Register<SettingsDialogRequestedMessage>(recipient, (_, message) => request = message.Value);
        var vm = new SettingsRecurringTransactionsTabVM(appData, messenger);

        await vm.OpenAddRecurringTransactionAsync();

        var popupRequest = Assert.IsType<TransactionPopupRequest>(request?.Payload);
        Assert.Equal(SettingsDialogRequestType.AddRecurringTransaction, request?.RequestType);
        Assert.Equal(TransactionPopupRequestKind.AddRecurringTransaction, popupRequest.Kind);
        Assert.True(popupRequest.LockRecurringMode);
    }

    [Fact]
    public async Task BudgetInvalidation_ReloadsDashboardRecipientThroughInjectedMessenger()
    {
        var messenger = new WeakReferenceMessenger();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var budgetRepository = Substitute.For<Fluxo.Core.Interfaces.Repositories.IBudgetAllocationRepository>();
        budgetRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation());
        unitOfWork.BudgetAllocation.Returns(budgetRepository);
        var transactionService = Substitute.For<ITransactionService>();
        transactionService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TransactionDto>>([]));
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.AccountDto>>([]));
        var mapper = Substitute.For<IMapper>();
        mapper.Map<IReadOnlyList<TransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<AccountVM>>(Arg.Any<object>()).Returns([]);
        _ = new SpentAllowancePanelVM(
            transactionService,
            accountService,
            new InlineDataOperationRunner(unitOfWork),
            mapper,
            messenger);

        messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.Budget));

        await transactionService.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    private static IAppDataService CreateAppData(Account account)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns([account]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(
            [new Tag { Id = 1, Name = "General", HexCode = "#22C55E" }]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>()).Returns([]);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation());
        appData.GetTransactionByIdAsync(42, Arg.Any<CancellationToken>()).Returns(
            CreateTransaction(account, "Before"));
        return appData;
    }

    private static Account CreateAccount() => new()
    {
        Id = 1,
        Name = "Checking",
        AccountType = AccountType.Checking,
        Balance = 500m,
        IsEnabled = true
    };

    private static Transaction CreateTransaction(Account account, string name) => new()
    {
        Id = 42,
        Type = TransactionType.Expense,
        SourceAccountId = account.Id,
        Account = account,
        TagId = 1,
        Tag = new Tag { Id = 1, Name = "General", HexCode = "#22C55E" },
        Name = name,
        Amount = 10m,
        OccurredOn = DateTime.Today,
        ExpenseCategory = ExpenseCategory.Needs
    };

    private static TransactionVM CreateTransactionVm(Account account, string name) => new()
    {
        Id = 42,
        Type = TransactionType.Expense,
        SourceAccountId = account.Id,
        Account = new AccountVM
        {
            Id = account.Id,
            Name = account.Name,
            AccountType = account.AccountType,
            Balance = account.Balance,
            IsEnabled = account.IsEnabled
        },
        Tag = new TagVM { Id = 1, Name = "General", HexCode = "#22C55E" },
        Name = name,
        Amount = 10m,
        OccurredOn = DateTime.Today,
        ExpenseCategory = ExpenseCategory.Needs
    };
}
