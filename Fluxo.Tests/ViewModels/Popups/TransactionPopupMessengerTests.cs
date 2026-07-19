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
using Fluxo.ViewModels.Shell.QuickSetupWizard;
using Fluxo.Services.Dialogs;
using Fluxo.Mappings;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void TransientRecipients_UnregisterWhenClosedOrReopened()
    {
        var messenger = new WeakReferenceMessenger();
        var appData = CreateAppData(CreateAccount());
        var popup = new TransactionPopupVM(appData, messenger);
        popup.Configure(TransactionPopupRequest.View(CreateTransactionVm(CreateAccount(), "Before")));
        popup.Dispose();

        var refresh = messenger.Send(new TransactionPopupRefreshRequestedMessage(42));
        Assert.Throws<InvalidOperationException>(() => refresh.Response.GetAwaiter().GetResult());

        var wizard = new QuickSetupWizardRecurringTransactionsVM(appData, messenger);
        wizard.Dispose();
        var draft = new RecurringDraftSaveRequestedMessage(new TransactionPopupVM.RecurringDraftSaveInput(
            null, RecurringTransactionType.Expense, "Draft", 1m, RecurringPeriod.Monthly, 1, 1,
            ExpenseCategory.Needs, null, null, null));
        Assert.Throws<InvalidOperationException>(() => messenger.Send(draft).Response.GetAwaiter().GetResult());
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

    [Fact]
    public void FirstRunAddTagRequest_UsesHostBeforeMainWindowExists()
    {
        var messenger = new WeakReferenceMessenger();
        var dialog = Substitute.For<IDialogService>();
        var tags = new SettingsTagsTabVM(null!, Substitute.For<IAppDataService>(), messenger);
        using var host = new TransactionPopupAddTagHost(() => tags, dialog, messenger, () => null);

        messenger.Send(new TransactionPopupAddTagRequestedMessage(0));

        dialog.Received(1).ShowAddTag(tags, null);
    }

    [Fact]
    public async Task Ledger_ReceivesTransactionDetailAndInvalidationMessages()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateLedgerVm([], messenger, out var transactionService, out var reloads);

        messenger.Send(new TransactionDetailUpdatedMessage(new TransactionDetailUpdate(
            42,
            new TransactionDetailSnapshot(10m, DateTime.Today, ExpenseCategory.Needs, 1, 1),
            TransactionDetailChangedFields.Amount)));
        await reloads.Task.WaitAsync(TimeSpan.FromSeconds(2));

        messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.Budget));
        await reloads.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await transactionService.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    private static LedgerVM CreateLedgerVm(
        IReadOnlyList<Fluxo.Core.DTO.TransactionDto> transactions,
        IMessenger messenger,
        out ITransactionService transactionService,
        out TaskCompletionSource reloads)
    {
        transactionService = Substitute.For<ITransactionService>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reloads = completion;
        transactionService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            completion.TrySetResult();
            return Task.FromResult(transactions);
        });
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var tagService = Substitute.For<ITagService>();
        tagService.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var mapper = new MapperConfiguration(
            configuration => configuration.AddProfile<DtoViewModelProfile>(),
            NullLoggerFactory.Instance).CreateMapper();
        return new LedgerVM(transactionService, accountService, tagService,
            Substitute.For<Fluxo.Core.Interfaces.Operations.IDataOperationRunner>(), mapper, messenger);
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
