using AutoMapper;
using System.Windows;
using System.Runtime.ExceptionServices;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
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
using Microsoft.Extensions.DependencyInjection;
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
        var draft = new RecurringDraftSaveRequestedMessage(new RecurringDraftSaveInput(
            null, RecurringTransactionType.Expense, "Draft", 1m, RecurringPeriod.Monthly, 1, 1,
            ExpenseCategory.Needs, null, null, null));
        Assert.Throws<InvalidOperationException>(() => messenger.Send(draft).Response.GetAwaiter().GetResult());
    }

    [Fact]
    public async Task PopupRequestAndRecurringMessage_UseNeutralContracts()
    {
        var messenger = new WeakReferenceMessenger();
        var popupDraft = new TransactionPopupDraft(
            true, "Expense", 10m, 1, DateTime.Today, "Note", ExpenseCategory.Needs, 2);
        var request = TransactionPopupRequest.Add(popupDraft);
        var recurringInput = new RecurringDraftSaveInput(
            null, RecurringTransactionType.Expense, "Draft", 1m, RecurringPeriod.Monthly, 1, 1,
            ExpenseCategory.Needs, null, null, null);
        var message = new RecurringDraftSaveRequestedMessage(recurringInput);
        var recipient = new object();
        messenger.Register<RecurringDraftSaveRequestedMessage>(recipient,
            (_, requestMessage) => requestMessage.Reply(TransactionPopupSubmissionResult.Success()));
        await messenger.Send(message);

        Assert.Equal(popupDraft, request.Draft);
        Assert.Equal(recurringInput, message.Input);
        Assert.True((await message.Response).IsSuccess);
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
        RunInSta(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var dialog = Substitute.For<IDialogService>();
            var tags = new SettingsTagsTabVM(null!, Substitute.For<IAppDataService>(), messenger);
            using var root = new ServiceCollection()
                .AddScoped(_ => tags)
                .BuildServiceProvider();
            var owner = new Window();
            using var host = new TransactionPopupAddTagHost(
                root.GetRequiredService<IServiceScopeFactory>(), dialog, messenger, _ => owner);

            messenger.Send(new TransactionPopupAddTagRequestedMessage(0, Guid.NewGuid()));

            dialog.Received(1).ShowAddTag(tags, owner);
            owner.Close();
        });
    }

    [Fact]
    public void AddTagRequest_WithTwoHosts_UsesOnlyLatestHostAndScopedWorkflow()
    {
        RunInSta(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var dialog = Substitute.For<IDialogService>();
            var firstTags = new SettingsTagsTabVM(null!, Substitute.For<IAppDataService>(), messenger);
            var secondTags = new SettingsTagsTabVM(null!, Substitute.For<IAppDataService>(), messenger);
            using var firstRoot = new ServiceCollection().AddScoped(_ => firstTags).BuildServiceProvider();
            using var secondRoot = new ServiceCollection().AddScoped(_ => secondTags).BuildServiceProvider();
            var owner = new Window();
            using var firstHost = new TransactionPopupAddTagHost(
                firstRoot.GetRequiredService<IServiceScopeFactory>(), dialog, messenger, _ => owner);
            using var secondHost = new TransactionPopupAddTagHost(
                secondRoot.GetRequiredService<IServiceScopeFactory>(), dialog, messenger, _ => owner);

            messenger.Send(new TransactionPopupAddTagRequestedMessage(0, Guid.NewGuid()));

            dialog.DidNotReceive().ShowAddTag(firstTags, owner);
            dialog.Received(1).ShowAddTag(secondTags, owner);
            secondHost.Dispose();
            messenger.Send(new TransactionPopupAddTagRequestedMessage(0, Guid.NewGuid()));
            dialog.Received(1).ShowAddTag(firstTags, owner);
            owner.Close();
        });
    }

    [Fact]
    public void AddTagRequest_FromSiblingPopup_IsIgnoredByActiveOwnerHost()
    {
        RunInSta(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var dialog = Substitute.For<IDialogService>();
            var tags = new SettingsTagsTabVM(null!, Substitute.For<IAppDataService>(), messenger);
            using var root = new ServiceCollection().AddScoped(_ => tags).BuildServiceProvider();
            var owner = new Window();
            var requester = Guid.NewGuid();
            using var host = new TransactionPopupAddTagHost(
                root.GetRequiredService<IServiceScopeFactory>(), dialog, messenger,
                candidate => candidate == requester ? owner : null);

            messenger.Send(new TransactionPopupAddTagRequestedMessage(0, Guid.NewGuid()));
            dialog.DidNotReceive().ShowAddTag(tags, owner);

            messenger.Send(new TransactionPopupAddTagRequestedMessage(0, requester));
            dialog.Received(1).ShowAddTag(tags, owner);
            owner.Close();
        });
    }

    [Fact]
    public async Task ComposedMainAndLedgerGraph_RefreshesEachOwnerOncePerPath()
    {
        var messenger = new WeakReferenceMessenger();
        var graph = CreateMainGraph(messenger);

        messenger.Send(new TransactionDetailUpdatedMessage(new TransactionDetailUpdate(
            42,
            new TransactionDetailSnapshot(10m, DateTime.Today, ExpenseCategory.Needs, 1, 1),
            TransactionDetailChangedFields.Amount)));
        await graph.LedgerFirstReload.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, graph.LedgerCalls());
        await graph.BudgetTransactionService.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
        await graph.SpentTransactionService.Received(1).GetAllAsync(Arg.Any<CancellationToken>());

        messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.Budget));
        await graph.LedgerSecondReload.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, graph.LedgerCalls());
        await graph.BudgetTransactionService.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
        await graph.SpentTransactionService.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    private static MainGraph CreateMainGraph(IMessenger messenger)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var userSettings = Substitute.For<IUserSettingsRepository>();
        userSettings.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.UserSettings.Returns(userSettings);
        var budgetAllocation = Substitute.For<IBudgetAllocationRepository>();
        budgetAllocation.GetAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation());
        unitOfWork.BudgetAllocation.Returns(budgetAllocation);
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.Transactions.Returns(transactions);
        var savingGoals = Substitute.For<ISavingGoalRepository>();
        savingGoals.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.SavingGoals.Returns(savingGoals);
        var recurring = Substitute.For<IRecurringTransactionRepository>();
        recurring.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.RecurringTransactions.Returns(recurring);
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.Accounts.Returns(accounts);

        var runner = new InlineDataOperationRunner(unitOfWork);
        var mapper = Substitute.For<IMapper>();
        mapper.Map<IReadOnlyList<TransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<AccountVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<SavingGoalVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<Fluxo.Core.DTO.SavingGoalDto>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<RecurringTransactionVM>>(Arg.Any<object>()).Returns([]);
        mapper.Map<IReadOnlyList<Fluxo.Core.DTO.RecurringTransactionDto>>(Arg.Any<object>()).Returns([]);

        var budgetTransactions = Substitute.For<ITransactionService>();
        budgetTransactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var budgetAccounts = Substitute.For<IAccountService>();
        budgetAccounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var tags = Substitute.For<ITagService>();
        tags.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var spentTransactions = Substitute.For<ITransactionService>();
        spentTransactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var spentAccounts = Substitute.For<IAccountService>();
        spentAccounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var notificationTransactions = Substitute.For<ITransactionService>();
        notificationTransactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var notificationAccounts = Substitute.For<IAccountService>();
        notificationAccounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        var dashboard = new DashboardVM(
            new NotificationPanelVM(notificationTransactions, notificationAccounts, runner, mapper, messenger: messenger),
            new BudgetAllocationPanelVM(budgetTransactions, budgetAccounts, tags, runner, mapper, messenger),
            new SpentAllowancePanelVM(spentTransactions, spentAccounts, runner, mapper, messenger),
            new SavingGoalsPanelVM(runner, mapper, messenger),
            new UpcomingEventsPanelVM(runner, mapper, messenger: messenger),
            new MainViewModeToggleVM(messenger));

        var ledgerTransactions = Substitute.For<ITransactionService>();
        var ledgerReloads = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var ledgerCalls = 0;
        ledgerTransactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var call = Interlocked.Increment(ref ledgerCalls);
            if (call <= ledgerReloads.Length)
                ledgerReloads[call - 1].TrySetResult();
            return Task.FromResult<IReadOnlyList<Fluxo.Core.DTO.TransactionDto>>([]);
        });
        var ledgerAccounts = Substitute.For<IAccountService>();
        ledgerAccounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var ledgerTags = Substitute.For<ITagService>();
        ledgerTags.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var ledger = new LedgerVM(ledgerTransactions, ledgerAccounts, ledgerTags, runner,
            new MapperConfiguration(configuration => configuration.AddProfile<DtoViewModelProfile>(), NullLoggerFactory.Instance).CreateMapper(), messenger);
        var main = new MainVM(runner, dashboard, new DaySpinnerVM(messenger), ledger, messenger: messenger);
        return new MainGraph(main, budgetTransactions, spentTransactions, ledgerReloads[0], ledgerReloads[1], () => ledgerCalls);
    }

    private sealed record MainGraph(
        MainVM Main,
        ITransactionService BudgetTransactionService,
        ITransactionService SpentTransactionService,
        TaskCompletionSource LedgerFirstReload,
        TaskCompletionSource LedgerSecondReload,
        Func<int> LedgerCalls);

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

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
