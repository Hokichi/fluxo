using Fluxo.Core.Entities;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Caching;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Caching;

namespace Fluxo.Services.Persistence;

public sealed class AppDataService : IAppDataService
{
    private readonly Lock _pendingLock = new();
    private readonly List<Func<IUnitOfWork, CancellationToken, Task>> _pending = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly SemaphoreSlim _allocationGate = new(1, 1);
    private readonly IAppDataCache? _cache;
    private readonly AppDataCommitCoordinator? _coordinator;
    private readonly IUnitOfWork? _directUnitOfWork;
    private BudgetAllocation? _pendingAllocation;

    public AppDataService(IAppDataCache cache, AppDataCommitCoordinator coordinator)
    {
        _cache = cache;
        _coordinator = coordinator;
    }

    internal AppDataService(IUnitOfWork unitOfWork)
    {
        _directUnitOfWork = unitOfWork;
    }

    public Task<IReadOnlyList<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetTransactionsAsync(cancellationToken)
            : _directUnitOfWork.Transactions.GetAllAsync(cancellationToken);

    public Task<Transaction?> GetTransactionByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetTransactionByIdAsync(id, cancellationToken)
            : _directUnitOfWork.Transactions.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Transaction>> SearchTransactionsAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.SearchTransactionsAsync(filter, cancellationToken)
            : _directUnitOfWork.Transactions.SearchAsync(filter, cancellationToken);

    public Task<IReadOnlyList<Transaction>> GetMarkedTransactionsForDeletionAsync(
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetMarkedTransactionsForDeletionAsync(cancellationToken)
            : _directUnitOfWork.Transactions.GetMarkedForDeletionAsync(cancellationToken);

    public Task AddTransactionAsync(Transaction entity, CancellationToken cancellationToken = default) =>
        Enqueue((unitOfWork, ct) => unitOfWork.Transactions.AddAsync(entity, ct), cancellationToken);

    public void UpdateTransaction(Transaction entity) => Enqueue(unitOfWork => unitOfWork.Transactions.Update(entity));
    public void RemoveTransaction(Transaction entity) => Enqueue(unitOfWork => unitOfWork.Transactions.Remove(entity));

    public Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetTagsAsync(cancellationToken)
            : _directUnitOfWork.Tags.GetAllAsync(cancellationToken);

    public Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetTagByIdAsync(id, cancellationToken)
            : _directUnitOfWork.Tags.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<(Tag Tag, int Count)>> GetTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetTagsByCountDescendingAsync(cancellationToken)
            : _directUnitOfWork.Tags.GetTagsByCountDescendingAsync(cancellationToken);

    public Task<IReadOnlyList<(Tag Tag, int Count)>> GetTodayTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetTodayTagsByCountDescendingAsync(cancellationToken)
            : _directUnitOfWork.Tags.GetTodayTagsByCountDescendingAsync(cancellationToken);

    public Task AddTagAsync(Tag entity, CancellationToken cancellationToken = default) =>
        Enqueue((unitOfWork, ct) => unitOfWork.Tags.AddAsync(entity, ct), cancellationToken);

    public void UpdateTag(Tag entity) => Enqueue(unitOfWork => unitOfWork.Tags.Update(entity));
    public void RemoveTag(Tag entity) => Enqueue(unitOfWork => unitOfWork.Tags.Remove(entity));

    public Task<IReadOnlyList<SavingGoal>> GetSavingGoalsAsync(CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetSavingGoalsAsync(cancellationToken)
            : _directUnitOfWork.SavingGoals.GetAllAsync(cancellationToken);

    public Task<SavingGoal?> GetSavingGoalByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetSavingGoalByIdAsync(id, cancellationToken)
            : _directUnitOfWork.SavingGoals.GetByIdAsync(id, cancellationToken);

    public Task AddSavingGoalAsync(SavingGoal entity, CancellationToken cancellationToken = default) =>
        Enqueue((unitOfWork, ct) => unitOfWork.SavingGoals.AddAsync(entity, ct), cancellationToken);

    public void UpdateSavingGoal(SavingGoal entity) => Enqueue(unitOfWork => unitOfWork.SavingGoals.Update(entity));
    public void RemoveSavingGoal(SavingGoal entity) => Enqueue(unitOfWork => unitOfWork.SavingGoals.Remove(entity));

    public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetAccountsAsync(cancellationToken)
            : _directUnitOfWork.Accounts.GetAllAsync(cancellationToken);

    public Task<Account?> GetAccountByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetAccountByIdAsync(id, cancellationToken)
            : _directUnitOfWork.Accounts.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Account>> SearchAccountsAsync(
        AccountFilter filter,
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.SearchAccountsAsync(filter, cancellationToken)
            : _directUnitOfWork.Accounts.SearchAsync(filter, cancellationToken);

    public Task<IReadOnlyList<Account>> GetMarkedAccountsForDeletionAsync(
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetMarkedAccountsForDeletionAsync(cancellationToken)
            : _directUnitOfWork.Accounts.GetMarkedForDeletionAsync(cancellationToken);

    public Task AddAccountAsync(Account entity, CancellationToken cancellationToken = default) =>
        Enqueue((unitOfWork, ct) => unitOfWork.Accounts.AddAsync(entity, ct), cancellationToken);

    public void UpdateAccount(Account entity) => Enqueue(unitOfWork => unitOfWork.Accounts.Update(entity));
    public void RemoveAccount(Account entity) => Enqueue(unitOfWork => unitOfWork.Accounts.Remove(entity));

    public Task<IReadOnlyList<RecurringTransaction>> GetRecurringTransactionsAsync(
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetRecurringTransactionsAsync(cancellationToken)
            : _directUnitOfWork.RecurringTransactions.GetAllAsync(cancellationToken);

    public Task<RecurringTransaction?> GetRecurringTransactionByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetRecurringTransactionByIdAsync(id, cancellationToken)
            : _directUnitOfWork.RecurringTransactions.GetByIdAsync(id, cancellationToken);

    public Task AddRecurringTransactionAsync(
        RecurringTransaction entity,
        CancellationToken cancellationToken = default) =>
        Enqueue((unitOfWork, ct) => unitOfWork.RecurringTransactions.AddAsync(entity, ct), cancellationToken);

    public void UpdateRecurringTransaction(RecurringTransaction entity) =>
        Enqueue(unitOfWork => unitOfWork.RecurringTransactions.Update(entity));

    public void RemoveRecurringTransaction(RecurringTransaction entity) =>
        Enqueue(unitOfWork => unitOfWork.RecurringTransactions.Remove(entity));

    public Task<IReadOnlyList<UserSettings>> GetUserSettingsAsync(CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetUserSettingsAsync(cancellationToken)
            : _directUnitOfWork.UserSettings.GetAllAsync(cancellationToken);

    public Task<UserSettings?> GetUserSettingByNameAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        _directUnitOfWork is null
            ? _cache!.GetUserSettingByNameAsync(name, cancellationToken)
            : _directUnitOfWork.UserSettings.GetByNameAsync(name, cancellationToken);

    public Task AddUserSettingAsync(UserSettings entity, CancellationToken cancellationToken = default) =>
        Enqueue((unitOfWork, ct) => unitOfWork.UserSettings.AddAsync(entity, ct), cancellationToken);

    public void UpdateUserSetting(UserSettings entity) => Enqueue(unitOfWork => unitOfWork.UserSettings.Update(entity));
    public void RemoveUserSetting(UserSettings entity) => Enqueue(unitOfWork => unitOfWork.UserSettings.Remove(entity));

    public Task<BudgetAllocation> GetBudgetAllocationAsync(CancellationToken cancellationToken = default) =>
        EnsureBudgetAllocationAsync(cancellationToken);

    public async Task<BudgetAllocation> EnsureBudgetAllocationAsync(CancellationToken cancellationToken = default)
    {
        await _allocationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pendingAllocation is not null)
                return _pendingAllocation;

            var existing = _directUnitOfWork is null
                ? await _cache!.GetBudgetAllocationAsync(cancellationToken).ConfigureAwait(false)
                : await _directUnitOfWork.BudgetAllocation.GetAsync(cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                return existing;

            var allocation = new BudgetAllocation();
            _pendingAllocation = allocation;
            await Enqueue(
                (unitOfWork, ct) => unitOfWork.BudgetAllocation.AddAsync(allocation, ct),
                cancellationToken).ConfigureAwait(false);
            return allocation;
        }
        finally
        {
            _allocationGate.Release();
        }
    }

    public void UpdateBudgetAllocation(BudgetAllocation entity) =>
        Enqueue(unitOfWork => unitOfWork.BudgetAllocation.Update(entity));

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_directUnitOfWork is not null)
        {
            await _directUnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _pendingAllocation = null;
            return;
        }

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Func<IUnitOfWork, CancellationToken, Task>[] operations;
            lock (_pendingLock)
                operations = [.. _pending];

            await _coordinator!.SaveAsync(operations, cancellationToken).ConfigureAwait(false);

            lock (_pendingLock)
            {
                _pending.RemoveRange(0, operations.Length);
                _pendingAllocation = null;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private Task Enqueue(
        Func<IUnitOfWork, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_directUnitOfWork is not null)
            return operation(_directUnitOfWork, cancellationToken);

        lock (_pendingLock)
            _pending.Add(operation);
        return Task.CompletedTask;
    }

    private void Enqueue(Action<IUnitOfWork> operation)
    {
        if (_directUnitOfWork is not null)
        {
            operation(_directUnitOfWork);
            return;
        }

        lock (_pendingLock)
        {
            _pending.Add((unitOfWork, _) =>
            {
                operation(unitOfWork);
                return Task.CompletedTask;
            });
        }
    }
}
