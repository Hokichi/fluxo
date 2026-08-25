using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Mappings;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;
using Fluxo.ViewModels.Shell.Main;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class LedgerVMDateRangeTests
{
    [Fact]
    public async Task LedgerVMDateRange_LoadAllTransactionsAsync_SetsEarliestDateThroughToday()
    {
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Old", OccurredOn = DateTime.Today.AddDays(-20), LoggedOn = DateTime.Today.AddDays(-20) },
            new TransactionDto { Id = 2, Type = TransactionType.Income, Name = "New", OccurredOn = DateTime.Today.AddDays(-2), LoggedOn = DateTime.Today.AddDays(-2) }
        ]);

        await vm.LoadAllTransactionsAsync();

        Assert.Equal(DateTime.Today.AddDays(-20), vm.StartDate.Date);
        Assert.Equal(DateTime.Today, vm.EndDate.Date);
    }

    [Fact]
    public async Task LedgerVMDateRange_LoadAllTransactionsAsync_WithoutTransactions_UsesToday()
    {
        var vm = CreateVm([]);

        await vm.LoadAllTransactionsAsync();

        Assert.Equal(DateTime.Today, vm.StartDate.Date);
        Assert.Equal(DateTime.Today, vm.EndDate.Date);
    }

    [Fact]
    public void LedgerVMDateRange_GlobalDashboardPeriodMessages_DoNotReplaceLedgerSelection()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVm([], messenger);
        vm.ApplyExternalDateRange(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-3), refresh: false);

        messenger.Send(new DateRangeSelectionChangedMessage(DateTime.Today.AddDays(-2), DateTime.Today));
        messenger.Send(new AllTimeViewModeMessage());

        Assert.Equal(DateTime.Today.AddDays(-10), vm.StartDate.Date);
        Assert.Equal(DateTime.Today.AddDays(-3), vm.EndDate.Date);
    }

    [Fact]
    public async Task LedgerVMDateRange_DedicatedDateRangeMessage_AppliesRangeOnNextLoad()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVm([], messenger);

        messenger.Send(new LedgerDateRangeRequestedMessage(DateTime.Today.AddDays(-6), DateTime.Today));
        await vm.LoadAsync();

        Assert.Equal(DateTime.Today.AddDays(-6), vm.StartDate.Date);
        Assert.Equal(DateTime.Today, vm.EndDate.Date);
    }

    [Fact]
    public async Task LedgerVMDateRange_DedicatedAllTimeMessage_RunsAllTransactionsFlowOnNextLoad()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Old", OccurredOn = DateTime.Today.AddDays(-20), LoggedOn = DateTime.Today.AddDays(-20) }
        ], messenger);

        messenger.Send(new LedgerAllTimeRequestedMessage());
        await vm.LoadAsync();

        Assert.Equal(DateTime.Today.AddDays(-20), vm.StartDate.Date);
        Assert.Equal(DateTime.Today, vm.EndDate.Date);
    }

    [Fact]
    public async Task LedgerVMDateRange_CategoryFilter_Excluded_ShowsOnlyExcludedTransactions()
    {
        var vm = CreateVm(CreateCategoryFilterTransactions());
        await vm.LoadAsync();

        vm.CategoryFilters.Single(option => option.IsAll).IsChecked = false;
        vm.CategoryFilters.Single(option => option.Label == "Excluded").IsChecked = true;
        vm.TransactionsView.Refresh();

        Assert.Equal(
            ["Excluded expense", "Excluded income"],
            vm.TransactionsView.Cast<LedgerTransactionItemVM>().Select(item => item.Name).Order());
    }

    [Fact]
    public async Task LedgerVMDateRange_CategoryFilter_NeedsAndExcluded_UsesUnion()
    {
        var vm = CreateVm(CreateCategoryFilterTransactions());
        await vm.LoadAsync();

        vm.CategoryFilters.Single(option => option.IsAll).IsChecked = false;
        vm.CategoryFilters.Single(option => option.Label == "Needs").IsChecked = true;
        vm.CategoryFilters.Single(option => option.Label == "Excluded").IsChecked = true;
        vm.TransactionsView.Refresh();

        Assert.Equal(
            ["Excluded expense", "Excluded income", "Needs expense"],
            vm.TransactionsView.Cast<LedgerTransactionItemVM>().Select(item => item.Name).Order());
    }

    [Fact]
    public async Task LedgerVMDateRange_LoadAsync_OrdersTransactionsByLoggedOn()
    {
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Older", Amount = 1m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(8) },
            new TransactionDto { Id = 2, Type = TransactionType.Expense, Name = "Newer", Amount = 100m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(9) }
        ]);

        await vm.LoadAsync();

        Assert.Equal(["Newer", "Older"], vm.TransactionsView.Cast<LedgerTransactionItemVM>().Select(item => item.Name));
    }

    [Fact]
    public async Task LedgerVMDateRange_LoadAsync_InitiallyShowsOnlyTodayTransactions()
    {
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Older", OccurredOn = DateTime.Today.AddDays(-1), LoggedOn = DateTime.Today.AddDays(-1) },
            new TransactionDto { Id = 2, Type = TransactionType.Income, Name = "Today", OccurredOn = DateTime.Today, LoggedOn = DateTime.Today }
        ]);

        await vm.LoadAsync();

        Assert.Equal(["Today"], vm.TransactionsView.Cast<LedgerTransactionItemVM>().Select(item => item.Name));
    }

    [Fact]
    public async Task LedgerVMDateRange_ApplyFilters_ParentShowsOnlyMatchingChildrenAndVisibleCount()
    {
        var vm = CreateVm(CreateParentWithChildren(), tags:
        [
            new TagDto { Id = 1, Name = "Food", HexCode = "#111111" },
            new TagDto { Id = 2, Name = "Travel", HexCode = "#222222" }
        ]);
        await vm.LoadAsync();

        vm.TagFilters.Single(option => option.IsAll).IsChecked = false;
        vm.TagFilters.Single(option => option.Label == "Food").IsChecked = true;
        vm.ApplyFilters();

        var parent = vm.TransactionsView.Cast<LedgerTransactionItemVM>().Single();
        Assert.Equal(1, parent.VisibleChildCount);
        Assert.Equal(["Food child"], parent.VisibleChildTransactions.Select(child => child.Name));
    }

    [Fact]
    public async Task LedgerVMDateRange_FiltersAndBulkEdit_TrackDistinctSelectionStates()
    {
        var vm = CreateVm(CreateParentWithChildren());
        await vm.LoadAsync();

        Assert.False(vm.HasActiveFilters);
        vm.ToggleSelectionModeCommand.Execute(null);
        Assert.True(vm.IsSelectionModeEnabled);
        Assert.False(vm.IsBulkEditEnabled);

        vm.IsBulkEditEnabled = true;
        Assert.True(vm.IsBulkEditEnabled);

        vm.ToggleSelectionModeCommand.Execute(null);
        Assert.False(vm.IsBulkEditEnabled);
    }

    [Fact]
    public async Task LedgerVMDateRange_ReloadPeriod_PreservesAppliedTagFilter()
    {
        var vm = CreateVm(CreateParentWithChildren(), tags:
        [
            new TagDto { Id = 1, Name = "Food", HexCode = "#111111" },
            new TagDto { Id = 2, Name = "Travel", HexCode = "#222222" }
        ]);
        await vm.LoadAsync();

        vm.TagFilters.Single(option => option.IsAll).IsChecked = false;
        vm.TagFilters.Single(option => option.Label == "Food").IsChecked = true;
        vm.ApplyFilters();

        vm.StartDate = DateTime.Today.AddDays(-1);
        await vm.LoadAsync();

        Assert.True(vm.TagFilters.Single(option => option.Label == "Food").IsChecked);
        Assert.False(vm.TagFilters.Single(option => option.IsAll).IsChecked);
    }

    [Fact]
    public async Task LedgerVMDateRange_SelectionMode_SelectedVisibleLeavesExposeSignedTotal()
    {
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Parent", Amount = 40m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today },
            new TransactionDto { Id = 2, Type = TransactionType.Expense, ParentTransactionId = 1, Name = "Child", Amount = 40m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today },
            new TransactionDto { Id = 3, Type = TransactionType.Income, Name = "Income", Amount = 65m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today }
        ]);
        await vm.LoadAsync();

        vm.ToggleSelectionModeCommand.Execute(null);

        Assert.Equal(25m, vm.SelectedVisibleTotal);
    }

    [Fact]
    public async Task LedgerVMDateRange_GroupedByType_OrdersItemsByLoggedOnWithoutForcingGroupOrder()
    {
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Older expense", OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(8) },
            new TransactionDto { Id = 2, Type = TransactionType.Income, Name = "Newer income", OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(9) }
        ]);
        await vm.LoadAsync();

        vm.SelectedGroupingMode = LedgerGroupingMode.Types;

        Assert.Equal(["Newer income", "Older expense"], vm.TransactionsView.Cast<LedgerTransactionItemVM>().Select(item => item.Name));
    }

    [Fact]
    public async Task LedgerVMDateRange_GroupedByCategory_OrdersItemsByLoggedOnWithoutForcingGroupOrder()
    {
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Older needs", ExpenseCategory = ExpenseCategory.Needs, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(8) },
            new TransactionDto { Id = 2, Type = TransactionType.Expense, Name = "Newer wants", ExpenseCategory = ExpenseCategory.Wants, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(9) }
        ]);
        await vm.LoadAsync();

        vm.SelectedGroupingMode = LedgerGroupingMode.Category;

        Assert.Equal(["Newer wants", "Older needs"], vm.TransactionsView.Cast<LedgerTransactionItemVM>().Select(item => item.Name));
    }

    [Fact]
    public async Task LedgerVMDateRange_LoadAllTransactionsAsync_ShowsLoadingToastUntilReloadSettles()
    {
        var dialogService = Substitute.For<IDialogService>();
        var uiSettleAwaiter = Substitute.For<IUiSettleAwaiter>();
        dialogService.ShowToastWhileAsync("Loading data", Arg.Any<Func<Task>>())
            .Returns(call => call.Arg<Func<Task>>().Invoke());
        var vm = CreateVm(
        [
            new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Transaction", OccurredOn = DateTime.Today, LoggedOn = DateTime.Today }
        ], dialogService: dialogService, uiSettleAwaiter: uiSettleAwaiter);

        await vm.LoadAllTransactionsAsync();

        await dialogService.Received(1).ShowToastWhileAsync("Loading data", Arg.Any<Func<Task>>());
        await uiSettleAwaiter.Received(1).WaitForUiReadyAsync();
    }

    private static IReadOnlyList<TransactionDto> CreateCategoryFilterTransactions() =>
    [
        new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Needs expense", ExpenseCategory = ExpenseCategory.Needs, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today },
        new TransactionDto { Id = 2, Type = TransactionType.Expense, Name = "Excluded expense", ExpenseCategory = ExpenseCategory.Excluded, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today },
        new TransactionDto { Id = 3, Type = TransactionType.Income, Name = "Excluded income", ExpenseCategory = ExpenseCategory.Excluded, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today },
        new TransactionDto { Id = 4, Type = TransactionType.Income, Name = "Included income", OccurredOn = DateTime.Today, LoggedOn = DateTime.Today }
    ];

    private static IReadOnlyList<TransactionDto> CreateParentWithChildren() =>
    [
        new TransactionDto { Id = 1, Type = TransactionType.Expense, Name = "Parent", Amount = 50m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today },
        new TransactionDto { Id = 2, Type = TransactionType.Expense, ParentTransactionId = 1, Name = "Food child", Amount = 20m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(2), Tag = new TagDto { Id = 1, Name = "Food", HexCode = "#111111" } },
        new TransactionDto { Id = 3, Type = TransactionType.Expense, ParentTransactionId = 1, Name = "Travel child", Amount = 30m, OccurredOn = DateTime.Today, LoggedOn = DateTime.Today.AddHours(1), Tag = new TagDto { Id = 2, Name = "Travel", HexCode = "#222222" } }
    ];

    private static LedgerVM CreateVm(
        IReadOnlyList<TransactionDto> transactions,
        IMessenger? messenger = null,
        IReadOnlyList<TagDto>? tags = null,
        IDialogService? dialogService = null,
        IUiSettleAwaiter? uiSettleAwaiter = null)
    {
        var transactionService = Substitute.For<ITransactionService>();
        transactionService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(transactions);
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var tagService = Substitute.For<ITagService>();
        tagService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(tags ?? []);
        var mapper = new MapperConfiguration(
            configuration => configuration.AddProfile<DtoViewModelProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        return new LedgerVM(
            transactionService,
            accountService,
            tagService,
            Substitute.For<IAppDataService>(),
            mapper,
            messenger,
            dialogService,
            uiSettleAwaiter);
    }
}
