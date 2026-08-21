using Fluxo.Core.Entities;
using Fluxo.Core.Exceptions;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces.Caching;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Caching;

public sealed class AppDataCache(AppDataCacheHydrator hydrator, ILogService logService) : IAppDataCache
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private AppDataSnapshot? _snapshot;

    public bool IsInitialized => Volatile.Read(ref _snapshot) is not null;
    public long Version => Volatile.Read(ref _snapshot)?.Version ?? 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsInitialized)
                throw new InvalidOperationException("Application data cache is already initialized.");

            var snapshot = await hydrator.HydrateAsync(1, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _snapshot, snapshot);
            logService.LogInformation("Application data cache published. Version=1.");
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public Task<IReadOnlyList<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).Transactions(),
            cancellationToken);
    }

    public Task<IReadOnlyList<Transaction>> SearchTransactionsAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).Transactions(filter),
            cancellationToken);
    }

    public Task<IReadOnlyList<Transaction>> GetMarkedTransactionsForDeletionAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).MarkedTransactionsForDeletion(),
            cancellationToken);
    }

    public Task<Transaction?> GetTransactionByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppDataSnapshotMaterializer(CaptureSnapshot()).TransactionById(id));
    }

    public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(() => new AppDataSnapshotMaterializer(snapshot).Accounts(), cancellationToken);
    }

    public Task<IReadOnlyList<Account>> SearchAccountsAsync(
        AccountFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).Accounts(filter),
            cancellationToken);
    }

    public Task<IReadOnlyList<Account>> GetMarkedAccountsForDeletionAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).MarkedAccountsForDeletion(),
            cancellationToken);
    }

    public Task<Account?> GetAccountByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppDataSnapshotMaterializer(CaptureSnapshot()).AccountById(id));
    }

    public Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(() => new AppDataSnapshotMaterializer(snapshot).Tags(), cancellationToken);
    }

    public Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppDataSnapshotMaterializer(CaptureSnapshot()).TagById(id));
    }

    public Task<IReadOnlyList<(Tag Tag, int Count)>> GetTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).TagsByCount(todayOnly: false),
            cancellationToken);
    }

    public Task<IReadOnlyList<(Tag Tag, int Count)>> GetTodayTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).TagsByCount(todayOnly: true),
            cancellationToken);
    }

    public Task<IReadOnlyList<SavingGoal>> GetSavingGoalsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).SavingGoals(),
            cancellationToken);
    }

    public Task<SavingGoal?> GetSavingGoalByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppDataSnapshotMaterializer(CaptureSnapshot()).SavingGoalById(id));
    }

    public Task<IReadOnlyList<RecurringTransaction>> GetRecurringTransactionsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).RecurringTransactions(),
            cancellationToken);
    }

    public Task<RecurringTransaction?> GetRecurringTransactionByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new AppDataSnapshotMaterializer(CaptureSnapshot()).RecurringTransactionById(id));
    }

    public Task<IReadOnlyList<UserSettings>> GetUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureSnapshot();
        return RunCollectionProjection(
            () => new AppDataSnapshotMaterializer(snapshot).UserSettings(),
            cancellationToken);
    }

    public Task<UserSettings?> GetUserSettingByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppDataSnapshotMaterializer(CaptureSnapshot()).UserSettingByName(name));
    }

    public Task<BudgetAllocation?> GetBudgetAllocationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppDataSnapshotMaterializer(CaptureSnapshot()).BudgetAllocation());
    }

    internal AppDataSnapshot CaptureSnapshot()
    {
        return Volatile.Read(ref _snapshot) ?? throw new AppDataCacheNotInitializedException();
    }

    internal Task<AppDataSnapshot> CreateRebuildCandidateAsync(CancellationToken cancellationToken)
    {
        var current = CaptureSnapshot();
        return hydrator.HydrateAsync(current.Version + 1, cancellationToken);
    }

    internal void Publish(AppDataSnapshot snapshot)
    {
        Interlocked.Exchange(ref _snapshot, snapshot);
        logService.LogInformation($"Application data cache published. Version={snapshot.Version}.");
    }

    private static Task<IReadOnlyList<T>> RunCollectionProjection<T>(
        Func<IReadOnlyList<T>> projection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(projection, cancellationToken);
    }
}
