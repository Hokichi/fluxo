using Fluxo.Core.Entities;
using Fluxo.Core.Filters;

namespace Fluxo.Core.Interfaces.Caching;

public interface IAppDataCache
{
    bool IsInitialized { get; }
    long Version { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> SearchTransactionsAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetMarkedTransactionsForDeletionAsync(
        CancellationToken cancellationToken = default);
    Task<Transaction?> GetTransactionByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> SearchAccountsAsync(
        AccountFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetMarkedAccountsForDeletionAsync(
        CancellationToken cancellationToken = default);
    Task<Account?> GetAccountByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Tag Tag, int Count)>> GetTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Tag Tag, int Count)>> GetTodayTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavingGoal>> GetSavingGoalsAsync(CancellationToken cancellationToken = default);
    Task<SavingGoal?> GetSavingGoalByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecurringTransaction>> GetRecurringTransactionsAsync(
        CancellationToken cancellationToken = default);
    Task<RecurringTransaction?> GetRecurringTransactionByIdAsync(
        int id,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSettings>> GetUserSettingsAsync(CancellationToken cancellationToken = default);
    Task<UserSettings?> GetUserSettingByNameAsync(
        string name,
        CancellationToken cancellationToken = default);
    Task<BudgetAllocation?> GetBudgetAllocationAsync(CancellationToken cancellationToken = default);
}
