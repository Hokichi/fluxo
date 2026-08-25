using Fluxo.Core.Entities;
using Fluxo.Core.Filters;

namespace Fluxo.Core.Interfaces.Services;

public interface IAppDataService
{
    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken = default);
    Task<Transaction?> GetTransactionByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> SearchTransactionsAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetMarkedTransactionsForDeletionAsync(
        CancellationToken cancellationToken = default);
    Task AddTransactionAsync(Transaction entity, CancellationToken cancellationToken = default);
    void UpdateTransaction(Transaction entity);
    void RemoveTransaction(Transaction entity);

    Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Tag Tag, int Count)>> GetTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Tag Tag, int Count)>> GetTodayTagsByCountDescendingAsync(
        CancellationToken cancellationToken = default);
    Task AddTagAsync(Tag entity, CancellationToken cancellationToken = default);
    void UpdateTag(Tag entity);
    void RemoveTag(Tag entity);

    Task<IReadOnlyList<SavingGoal>> GetSavingGoalsAsync(CancellationToken cancellationToken = default);
    Task<SavingGoal?> GetSavingGoalByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddSavingGoalAsync(SavingGoal entity, CancellationToken cancellationToken = default);
    void UpdateSavingGoal(SavingGoal entity);
    void RemoveSavingGoal(SavingGoal entity);

    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<Account?> GetAccountByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> SearchAccountsAsync(
        AccountFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetMarkedAccountsForDeletionAsync(
        CancellationToken cancellationToken = default);
    Task AddAccountAsync(Account entity, CancellationToken cancellationToken = default);
    void UpdateAccount(Account entity);
    void RemoveAccount(Account entity);

    Task<IReadOnlyList<RecurringTransaction>> GetRecurringTransactionsAsync(CancellationToken cancellationToken = default);
    Task<RecurringTransaction?> GetRecurringTransactionByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddRecurringTransactionAsync(RecurringTransaction entity, CancellationToken cancellationToken = default);
    void UpdateRecurringTransaction(RecurringTransaction entity);
    void RemoveRecurringTransaction(RecurringTransaction entity);

    Task<IReadOnlyList<UserSettings>> GetUserSettingsAsync(CancellationToken cancellationToken = default);
    Task<UserSettings?> GetUserSettingByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddUserSettingAsync(UserSettings entity, CancellationToken cancellationToken = default);
    void UpdateUserSetting(UserSettings entity);
    void RemoveUserSetting(UserSettings entity);

    Task<BudgetAllocation> GetBudgetAllocationAsync(CancellationToken cancellationToken = default);
    Task<BudgetAllocation> EnsureBudgetAllocationAsync(CancellationToken cancellationToken = default);
    void UpdateBudgetAllocation(BudgetAllocation entity);

    void DiscardPendingChanges();
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
