using AutoMapper;
using Fluxo.Helpers.Transaction;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.ViewModels.Entities;
using CoreILogMemoryAction = Fluxo.Core.Interfaces.History.ILogMemoryAction;

namespace Fluxo.ViewModels.Shell.Main;

public partial class SpentAllowancePanelVM : ObservableRecipient,
    IRecipient<DateRangeSelectionChangedMessage>,
    IRecipient<AllTimeViewModeMessage>,
    IRecipient<DashboardDataInvalidatedMessage>,
    IRecipient<RecordLogMemoryMessage>,
    IRecipient<LogMemoryActionAppliedMessage>
{
    private readonly IDataOperationRunner _dataOperationRunner;
    private readonly ITransactionService _transactionService;
    private readonly IMapper _mapper;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly IAccountService _accountService;
    private readonly Func<DateTime> _todayProvider;

    private List<TransactionVM> _allTransactions = [];
    private BudgetAllocation _budgetAllocation = new();
    private (DateTime From, DateTime To)? _selectedRange;
    private List<AccountVM> _accounts = [];

    public SpentAllowancePanelVM(
        ITransactionService transactionService,
        IAccountService accountService,
        IDataOperationRunner dataOperationRunner,
        IMapper mapper,
        IMessenger? messenger = null,
        Func<DateTime>? todayProvider = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _transactionService = transactionService;
        _accountService = accountService;
        _dataOperationRunner = dataOperationRunner;
        _mapper = mapper;
        _todayProvider = todayProvider ?? (() => DateTime.Today);

        IsActive = true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Net))]
    private decimal _totalSpent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Net))]
    private decimal _totalEarned;

    [ObservableProperty]
    private decimal _allowance;

    public decimal Net => TotalEarned - TotalSpent;

    public void Receive(DateRangeSelectionChangedMessage message)
    {
        _selectedRange = message.Value;
        RefreshMetrics();
    }

    public void Receive(AllTimeViewModeMessage message)
    {
        _selectedRange = null;
        RefreshMetrics();
    }

    public void Receive(DashboardDataInvalidatedMessage message)
    {
        if (!message.Value.HasFlag(DashboardDataInvalidationScope.Budget))
            return;

        _ = ReloadFromServicesAsync();
    }

    public void Receive(RecordLogMemoryMessage message)
    {
        ApplyLogMemoryAction(message.Value, LogMemoryApplyDirection.Reapply);
    }

    public void Receive(LogMemoryActionAppliedMessage message)
    {
        var (action, direction) = message.Value;
        ApplyLogMemoryAction(action, direction);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _budgetAllocation = await LoadBudgetAllocationAsync(cancellationToken);

        var transactions = _mapper.Map<IReadOnlyList<TransactionVM>>(
            await _transactionService.GetAllAsync(cancellationToken));
        var accounts = _mapper.Map<IReadOnlyList<AccountVM>>(
            await _accountService.GetAllAsync(cancellationToken));
        _allTransactions = transactions
            .Where(transaction => !transaction.IsForDeletion)
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.LoggedOn)
            .ToList();
        _accounts = accounts.ToList();

        RefreshMetrics();
    }

    private async Task ReloadFromServicesAsync()
    {
        await _reloadGate.WaitAsync();

        try
        {
            await LoadAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private void RefreshMetrics()
    {
        var budgetTransactions = BudgetEffectiveTransactionFilter.Select(_allTransactions);
        var visibleTransactions = _selectedRange is { } range
            ? budgetTransactions.Where(transaction => transaction.OccurredOn.Date >= range.From.Date && transaction.OccurredOn.Date <= range.To.Date)
            : budgetTransactions;

        TotalSpent = visibleTransactions.Where(transaction => transaction.Type == TransactionType.Expense)
            .Sum(transaction => transaction.Amount);
        TotalEarned = visibleTransactions.Where(transaction => transaction.Type == TransactionType.Income)
            .Sum(transaction => transaction.Amount);

        var totalIncomeAmount = _accounts.Where(source => source.IsEnabled).Sum(source => source.Balance);
        Allowance = BudgetAllocationCalculator.CalculateDailyAllowance(
            _budgetAllocation,
            _todayProvider(),
            totalIncomeAmount);
    }

    private void ApplyLogMemoryAction(CoreILogMemoryAction action, LogMemoryApplyDirection direction)
    {
        switch (action)
        {
            case CompositeLogMemoryAction compositeAction:
                foreach (var childAction in compositeAction.Actions)
                    ApplyLogMemoryAction(childAction, direction);
                return;

            case DeleteTransactionMemoryAction deleteTransactionAction:
                ApplyDeletedTransactionAction(deleteTransactionAction, direction);
                return;
        }
    }

    private void ApplyDeletedTransactionAction(DeleteTransactionMemoryAction action, LogMemoryApplyDirection direction)
    {
        if (action.Snapshot is not { } snapshot)
            return;

        if (direction == LogMemoryApplyDirection.Reapply)
        {
            RemoveTransaction(snapshot.TransactionId);
            RestoreTransactionFromTrackedSource(snapshot);
            RefreshMetrics();
            return;
        }

        UpsertTransaction(snapshot);
        ApplyTransactionToTrackedSource(snapshot);
        RefreshMetrics();
    }

    private void RemoveTransaction(int transactionId)
    {
        _allTransactions = _allTransactions
            .Where(transaction => transaction.Id != transactionId)
            .ToList();
    }

    private void UpsertTransaction(TransactionMemorySnapshot snapshot)
    {
        var existingIndex = _allTransactions.FindIndex(transaction => transaction.Id == snapshot.TransactionId);
        var vm = ToTransactionVm(snapshot);

        if (existingIndex >= 0)
            _allTransactions[existingIndex] = vm;
        else
            _allTransactions.Add(vm);
    }

    private void ApplyTransactionToTrackedSource(TransactionMemorySnapshot snapshot)
    {
        var source = _accounts.FirstOrDefault(candidate => candidate.Id == snapshot.SourceAccountId);
        if (source is null)
            return;

        if (snapshot.Type == TransactionType.Expense)
            ApplyExpense(source, snapshot.Amount);
        else
            ApplyIncome(source, snapshot.Amount);
    }

    private void RestoreTransactionFromTrackedSource(TransactionMemorySnapshot snapshot)
    {
        var source = _accounts.FirstOrDefault(candidate => candidate.Id == snapshot.SourceAccountId);
        if (source is null)
            return;

        if (snapshot.Type == TransactionType.Expense)
            ApplyIncome(source, snapshot.Amount);
        else
            ApplyExpense(source, snapshot.Amount);
    }

    private TransactionVM ToTransactionVm(TransactionMemorySnapshot snapshot)
    {
        var source = _accounts.FirstOrDefault(candidate => candidate.Id == snapshot.SourceAccountId);

        return new TransactionVM
        {
            Id = snapshot.TransactionId,
            Type = snapshot.Type,
            Amount = snapshot.Amount,
            OccurredOn = snapshot.OccurredOn,
            Notes = snapshot.Notes,
            IsForDeletion = snapshot.IsForDeletion,
            ParentTransactionId = snapshot.ParentTransactionId,
            Name = snapshot.Name,
            ExpenseCategory = snapshot.ExpenseCategory,
            IsPinned = snapshot.IsPinned,
            IsIoU = snapshot.IsIoU,
            ShouldAffectBalance = snapshot.ShouldAffectBalance,
            IsExcludedFromBudget = snapshot.IsExcludedFromBudget,
            Account = new AccountVM
            {
                Id = snapshot.SourceAccountId,
                Name = source?.Name ?? string.Empty,
                AccountType = source?.AccountType ?? AccountType.Checking
            }
        };
    }

    private async Task<BudgetAllocation> LoadBudgetAllocationAsync(CancellationToken cancellationToken)
    {
        return await _dataOperationRunner.RunAsync(async (scope, ct) =>
            await scope.UnitOfWork.BudgetAllocation.GetAsync(ct) ?? new BudgetAllocation(), cancellationToken);
    }

    private static void ApplyExpense(AccountVM account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
            account.SpentAmount += amount;
        else
            account.Balance -= amount;
    }

    private static void ApplyIncome(AccountVM account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
        else
            account.Balance += amount;
    }
}
