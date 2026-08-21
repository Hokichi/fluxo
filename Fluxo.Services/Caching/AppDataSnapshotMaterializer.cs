using Fluxo.Core.Entities;
using Fluxo.Core.Filters;

namespace Fluxo.Services.Caching;

internal sealed class AppDataSnapshotMaterializer(AppDataSnapshot snapshot)
{
    internal IReadOnlyList<Transaction> Transactions()
    {
        return snapshot.TransactionOrder
            .Select(key => snapshot.Transactions[key.Id])
            .Where(value => !value.IsForDeletion)
            .Select(value => TransactionById(value.Id)!)
            .ToArray();
    }

    internal IReadOnlyList<Transaction> Transactions(TransactionFilter filter)
    {
        IEnumerable<Transaction> values = snapshot.TransactionOrder
            .Select(key => snapshot.Transactions[key.Id]);
        if (!filter.IncludeDeleted)
            values = values.Where(value => !value.IsForDeletion);
        if (filter.Type.HasValue)
            values = values.Where(value => value.Type == filter.Type);
        if (filter.AccountId.HasValue)
            values = values.Where(value => value.SourceAccountId == filter.AccountId);
        if (filter.StartDate.HasValue)
            values = values.Where(value => value.OccurredOn >= filter.StartDate);
        if (filter.EndDate.HasValue)
            values = values.Where(value => value.OccurredOn <= filter.EndDate);
        if (filter.ExpenseCategory.HasValue)
            values = values.Where(value => value.ExpenseCategory == filter.ExpenseCategory);
        if (filter.TagId.HasValue)
            values = values.Where(value => value.TagId == filter.TagId);
        return values.Select(value => TransactionById(value.Id)!).ToArray();
    }

    internal IReadOnlyList<Transaction> MarkedTransactionsForDeletion()
    {
        return snapshot.TransactionOrder
            .Select(key => snapshot.Transactions[key.Id])
            .Where(value => value.IsForDeletion)
            .Select(value => TransactionById(value.Id)!)
            .ToArray();
    }

    internal Transaction? TransactionById(int id)
    {
        if (!snapshot.Transactions.TryGetValue(id, out var value))
            return null;

        var account = AppDataSnapshot.CloneAccount(snapshot.Accounts[value.SourceAccountId]);
        var tag = value.TagId is { } tagId && snapshot.Tags.TryGetValue(tagId, out var storedTag)
            ? AppDataSnapshot.CloneTag(storedTag)
            : null;

        var goal = value.GoalId is { } goalId ? SavingGoalById(goalId) : null;
        var repaymentAccount = value.RepaymentAccountId is { } repaymentId ? AccountById(repaymentId) : null;
        var relatedRecurring = value.RelatedRecurringTransactionId is { } recurringId
            ? RecurringTransactionById(recurringId, includeExpired: true)
            : null;

        return new Transaction
        {
            Id = value.Id,
            Type = value.Type,
            SourceAccountId = value.SourceAccountId,
            Account = account,
            GoalId = value.GoalId,
            Goal = goal,
            RepaymentAccountId = value.RepaymentAccountId,
            RepaymentAccount = repaymentAccount,
            RelatedRecurringTransactionId = value.RelatedRecurringTransactionId,
            RelatedRecurringTransaction = relatedRecurring,
            Name = value.Name,
            Amount = value.Amount,
            OccurredOn = value.OccurredOn,
            LoggedOn = value.LoggedOn,
            Notes = value.Notes,
            ExpenseCategory = value.ExpenseCategory,
            TagId = value.TagId,
            Tag = tag,
            ParentTransactionId = value.ParentTransactionId,
            IsPinned = value.IsPinned,
            IsForDeletion = value.IsForDeletion,
            IsIoU = value.IsIoU,
            ShouldAffectBalance = value.ShouldAffectBalance
        };
    }

    internal IReadOnlyList<Account> Accounts()
    {
        return snapshot.Accounts.Values.OrderBy(value => value.Id).Select(AppDataSnapshot.CloneAccount).ToArray();
    }

    internal IReadOnlyList<Account> Accounts(AccountFilter filter)
    {
        IEnumerable<Account> values = snapshot.Accounts.Values;
        if (!string.IsNullOrWhiteSpace(filter.Name))
            values = values.Where(value => value.Name.Contains(filter.Name));
        if (filter.Type.HasValue)
            values = values.Where(value => value.AccountType == filter.Type);
        if (filter.PinnedOnUIOnly)
            values = values.Where(value => value.PinnedOnUI);
        if (filter.EnabledOnly)
            values = values.Where(value => value.IsEnabled);
        return values.OrderBy(value => value.Id).Select(AppDataSnapshot.CloneAccount).ToArray();
    }

    internal IReadOnlyList<Account> MarkedAccountsForDeletion()
    {
        return snapshot.Accounts.Values
            .Where(value => value.IsForDeletion)
            .OrderBy(value => value.Id)
            .Select(AppDataSnapshot.CloneAccount)
            .ToArray();
    }

    internal Account? AccountById(int id)
    {
        return snapshot.Accounts.TryGetValue(id, out var value) ? AppDataSnapshot.CloneAccount(value) : null;
    }

    internal IReadOnlyList<Tag> Tags()
    {
        return snapshot.Tags.Values.OrderBy(value => value.Id).Select(AppDataSnapshot.CloneTag).ToArray();
    }

    internal Tag? TagById(int id)
    {
        return snapshot.Tags.TryGetValue(id, out var value) ? AppDataSnapshot.CloneTag(value) : null;
    }

    internal IReadOnlyList<(Tag Tag, int Count)> TagsByCount(bool todayOnly)
    {
        IReadOnlyDictionary<int, int> counts = snapshot.TagUsageCounts;
        if (todayOnly)
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            counts = snapshot.Transactions.Values
                .Where(value =>
                    !value.IsForDeletion && value.TagId.HasValue &&
                    value.OccurredOn >= today && value.OccurredOn < tomorrow)
                .GroupBy(value => value.TagId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        return snapshot.Tags.Values
            .Select(value => (Tag: AppDataSnapshot.CloneTag(value), Count: counts.GetValueOrDefault(value.Id)))
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Tag.Id)
            .ToArray();
    }

    internal IReadOnlyList<SavingGoal> SavingGoals()
    {
        return snapshot.SavingGoals.Values
            .OrderBy(value => value.Id)
            .Select(AppDataSnapshot.CloneSavingGoal)
            .ToArray();
    }

    internal SavingGoal? SavingGoalById(int id)
    {
        return snapshot.SavingGoals.TryGetValue(id, out var value)
            ? AppDataSnapshot.CloneSavingGoal(value)
            : null;
    }

    internal IReadOnlyList<RecurringTransaction> RecurringTransactions()
    {
        return snapshot.RecurringTransactions.Values
            .Where(IsCurrent)
            .OrderBy(value => value.Id)
            .Select(value => MaterializeRecurring(value))
            .ToArray();
    }

    internal RecurringTransaction? RecurringTransactionById(int id)
    {
        return RecurringTransactionById(id, includeExpired: false);
    }

    internal IReadOnlyList<UserSettings> UserSettings()
    {
        return snapshot.UserSettings.Values
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .Select(AppDataSnapshot.CloneUserSetting)
            .ToArray();
    }

    internal UserSettings? UserSettingByName(string name)
    {
        return snapshot.UserSettings.TryGetValue(name, out var value)
            ? AppDataSnapshot.CloneUserSetting(value)
            : null;
    }

    internal BudgetAllocation? BudgetAllocation()
    {
        return snapshot.BudgetAllocation is null
            ? null
            : AppDataSnapshot.CloneBudgetAllocation(snapshot.BudgetAllocation);
    }

    private RecurringTransaction? RecurringTransactionById(int id, bool includeExpired)
    {
        if (!snapshot.RecurringTransactions.TryGetValue(id, out var value) || (!includeExpired && !IsCurrent(value)))
            return null;
        return MaterializeRecurring(value);
    }

    private RecurringTransaction MaterializeRecurring(RecurringTransaction value)
    {
        var result = AppDataSnapshot.CloneRecurringScalar(value);
        result.Source = AppDataSnapshot.CloneAccount(snapshot.Accounts[value.SourceId]);
        result.Tag = value.TagId is { } tagId ? TagById(tagId) : null;
        result.Goal = value.GoalId is { } goalId ? SavingGoalById(goalId) : null;
        return result;
    }

    private static bool IsCurrent(RecurringTransaction value)
    {
        return value.EndDate is not { } endDate || endDate.Date >= DateTime.Today;
    }
}
