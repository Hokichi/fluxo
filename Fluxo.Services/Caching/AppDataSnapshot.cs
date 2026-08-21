using System.Collections.Immutable;
using System.IO;
using Fluxo.Core.Entities;

namespace Fluxo.Services.Caching;

internal sealed class AppDataSnapshot
{
    private AppDataSnapshot(
        long version,
        ImmutableDictionary<int, Transaction> transactions,
        ImmutableDictionary<int, Tag> tags,
        ImmutableDictionary<int, SavingGoal> savingGoals,
        ImmutableDictionary<int, Account> accounts,
        ImmutableDictionary<int, RecurringTransaction> recurringTransactions,
        ImmutableDictionary<string, UserSettings> userSettings,
        BudgetAllocation? budgetAllocation,
        ImmutableSortedSet<TransactionCacheOrderKey> transactionOrder,
        ImmutableDictionary<int, int> tagUsageCounts)
    {
        Version = version;
        Transactions = transactions;
        Tags = tags;
        SavingGoals = savingGoals;
        Accounts = accounts;
        RecurringTransactions = recurringTransactions;
        UserSettings = userSettings;
        BudgetAllocation = budgetAllocation;
        TransactionOrder = transactionOrder;
        TagUsageCounts = tagUsageCounts;
    }

    internal long Version { get; }
    internal ImmutableDictionary<int, Transaction> Transactions { get; }
    internal ImmutableDictionary<int, Tag> Tags { get; }
    internal ImmutableDictionary<int, SavingGoal> SavingGoals { get; }
    internal ImmutableDictionary<int, Account> Accounts { get; }
    internal ImmutableDictionary<int, RecurringTransaction> RecurringTransactions { get; }
    internal ImmutableDictionary<string, UserSettings> UserSettings { get; }
    internal BudgetAllocation? BudgetAllocation { get; }
    internal ImmutableSortedSet<TransactionCacheOrderKey> TransactionOrder { get; }
    internal ImmutableDictionary<int, int> TagUsageCounts { get; }

    internal static AppDataSnapshot Create(
        long version,
        IEnumerable<Transaction> transactions,
        IEnumerable<Tag> tags,
        IEnumerable<SavingGoal> savingGoals,
        IEnumerable<Account> accounts,
        IEnumerable<RecurringTransaction> recurringTransactions,
        IEnumerable<UserSettings> userSettings,
        BudgetAllocation? budgetAllocation)
    {
        var transactionMap = transactions.ToImmutableDictionary(item => item.Id, CloneTransactionScalar);
        var tagMap = tags.ToImmutableDictionary(item => item.Id, CloneTag);
        var savingGoalMap = savingGoals.ToImmutableDictionary(item => item.Id, CloneSavingGoal);
        var accountMap = accounts.ToImmutableDictionary(item => item.Id, CloneAccount);
        var recurringMap = recurringTransactions.ToImmutableDictionary(item => item.Id, CloneRecurringScalar);
        var userSettingMap = userSettings.ToImmutableDictionary(
            item => item.Name,
            CloneUserSetting,
            StringComparer.Ordinal);
        var transactionOrder = transactionMap.Values
            .Select(item => new TransactionCacheOrderKey(item.OccurredOn, item.LoggedOn, item.Id))
            .ToImmutableSortedSet();
        var tagUsageCounts = transactionMap.Values
            .Where(item => !item.IsForDeletion && item.TagId.HasValue)
            .GroupBy(item => item.TagId!.Value)
            .ToImmutableDictionary(group => group.Key, group => group.Count());

        var snapshot = new AppDataSnapshot(
            version,
            transactionMap,
            tagMap,
            savingGoalMap,
            accountMap,
            recurringMap,
            userSettingMap,
            budgetAllocation is null ? null : CloneBudgetAllocation(budgetAllocation),
            transactionOrder,
            tagUsageCounts);
        snapshot.Validate();
        return snapshot;
    }

    internal AppDataSnapshot Apply(long version, AppDataChangeSet changeSet)
    {
        var transactions = Transactions;
        var tags = Tags;
        var savingGoals = SavingGoals;
        var accounts = Accounts;
        var recurringTransactions = RecurringTransactions;
        var userSettings = UserSettings;
        var budgetAllocation = BudgetAllocation;
        var transactionOrder = TransactionOrder;
        var tagUsageCounts = TagUsageCounts;

        foreach (var change in changeSet.Changes)
        {
            switch (change.Entity)
            {
                case Transaction transaction:
                    if (transactions.TryGetValue(transaction.Id, out var previousTransaction))
                    {
                        transactionOrder = transactionOrder.Remove(new TransactionCacheOrderKey(
                            previousTransaction.OccurredOn,
                            previousTransaction.LoggedOn,
                            previousTransaction.Id));
                        tagUsageCounts = AdjustTagCount(tagUsageCounts, previousTransaction, -1);
                    }

                    if (change.Kind == AppDataChangeKind.Deleted)
                    {
                        transactions = transactions.Remove(transaction.Id);
                    }
                    else
                    {
                        var stored = CloneTransactionScalar(transaction);
                        transactions = transactions.SetItem(stored.Id, stored);
                        transactionOrder = transactionOrder.Add(new TransactionCacheOrderKey(
                            stored.OccurredOn,
                            stored.LoggedOn,
                            stored.Id));
                        tagUsageCounts = AdjustTagCount(tagUsageCounts, stored, 1);
                    }
                    break;
                case Tag tag:
                    tags = change.Kind == AppDataChangeKind.Deleted
                        ? tags.Remove(tag.Id)
                        : tags.SetItem(tag.Id, CloneTag(tag));
                    break;
                case SavingGoal goal:
                    savingGoals = change.Kind == AppDataChangeKind.Deleted
                        ? savingGoals.Remove(goal.Id)
                        : savingGoals.SetItem(goal.Id, CloneSavingGoal(goal));
                    break;
                case Account account:
                    accounts = change.Kind == AppDataChangeKind.Deleted
                        ? accounts.Remove(account.Id)
                        : accounts.SetItem(account.Id, CloneAccount(account));
                    break;
                case RecurringTransaction recurring:
                    recurringTransactions = change.Kind == AppDataChangeKind.Deleted
                        ? recurringTransactions.Remove(recurring.Id)
                        : recurringTransactions.SetItem(recurring.Id, CloneRecurringScalar(recurring));
                    break;
                case UserSettings setting:
                    userSettings = change.Kind == AppDataChangeKind.Deleted
                        ? userSettings.Remove(setting.Name)
                        : userSettings.SetItem(setting.Name, CloneUserSetting(setting));
                    break;
                case BudgetAllocation allocation:
                    budgetAllocation = change.Kind == AppDataChangeKind.Deleted
                        ? null
                        : CloneBudgetAllocation(allocation);
                    break;
            }
        }

        var candidate = new AppDataSnapshot(
            version,
            transactions,
            tags,
            savingGoals,
            accounts,
            recurringTransactions,
            userSettings,
            budgetAllocation,
            transactionOrder,
            tagUsageCounts);
        candidate.Validate();
        return candidate;
    }

    internal void Validate()
    {
        foreach (var transaction in Transactions.Values)
        {
            Require(Accounts, transaction.SourceAccountId, nameof(Transaction.Account), transaction.Id);
            RequireOptional(Tags, transaction.TagId, nameof(Transaction.Tag), transaction.Id);
            RequireOptional(SavingGoals, transaction.GoalId, nameof(Transaction.Goal), transaction.Id);
            RequireOptional(Accounts, transaction.RepaymentAccountId, nameof(Transaction.RepaymentAccount), transaction.Id);
            RequireOptional(
                RecurringTransactions,
                transaction.RelatedRecurringTransactionId,
                nameof(Transaction.RelatedRecurringTransaction),
                transaction.Id);
            RequireOptional(Transactions, transaction.ParentTransactionId, nameof(Transaction.ParentTransaction), transaction.Id);
        }

        foreach (var recurring in RecurringTransactions.Values)
        {
            Require(Accounts, recurring.SourceId, nameof(RecurringTransaction.Source), recurring.Id);
            RequireOptional(Tags, recurring.TagId, nameof(RecurringTransaction.Tag), recurring.Id);
            RequireOptional(SavingGoals, recurring.GoalId, nameof(RecurringTransaction.Goal), recurring.Id);
        }
    }

    private static void Require<T>(IReadOnlyDictionary<int, T> values, int id, string relation, int ownerId)
    {
        if (!values.ContainsKey(id))
            throw new InvalidDataException($"{relation} target {id} for entity {ownerId} is missing.");
    }

    private static void RequireOptional<T>(
        IReadOnlyDictionary<int, T> values,
        int? id,
        string relation,
        int ownerId)
    {
        if (id.HasValue)
            Require(values, id.Value, relation, ownerId);
    }

    private static ImmutableDictionary<int, int> AdjustTagCount(
        ImmutableDictionary<int, int> counts,
        Transaction transaction,
        int adjustment)
    {
        if (transaction.IsForDeletion || transaction.TagId is not { } tagId)
            return counts;

        var next = counts.GetValueOrDefault(tagId) + adjustment;
        return next <= 0 ? counts.Remove(tagId) : counts.SetItem(tagId, next);
    }

    private static Transaction CloneTransactionScalar(Transaction value) => new()
    {
        Id = value.Id,
        Type = value.Type,
        SourceAccountId = value.SourceAccountId,
        Account = null!,
        GoalId = value.GoalId,
        RepaymentAccountId = value.RepaymentAccountId,
        RelatedRecurringTransactionId = value.RelatedRecurringTransactionId,
        Name = value.Name,
        Amount = value.Amount,
        OccurredOn = value.OccurredOn,
        LoggedOn = value.LoggedOn,
        Notes = value.Notes,
        ExpenseCategory = value.ExpenseCategory,
        TagId = value.TagId,
        ParentTransactionId = value.ParentTransactionId,
        IsPinned = value.IsPinned,
        IsForDeletion = value.IsForDeletion,
        IsIoU = value.IsIoU,
        ShouldAffectBalance = value.ShouldAffectBalance
    };

    internal static Account CloneAccount(Account value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        AccountType = value.AccountType,
        AccountLimit = value.AccountLimit,
        MaximumSpending = value.MaximumSpending,
        MinimumPayment = value.MinimumPayment,
        SpentAmount = value.SpentAmount,
        Balance = value.Balance,
        MonthlyDueDate = value.MonthlyDueDate,
        DeductSource = value.DeductSource,
        InterestRate = value.InterestRate,
        PinnedOnUI = value.PinnedOnUI,
        IsEnabled = value.IsEnabled,
        IsDefault = value.IsDefault,
        IsForDeletion = value.IsForDeletion
    };

    internal static Tag CloneTag(Tag value) => new()
    {
        Id = value.Id,
        IsSystemTag = value.IsSystemTag,
        Name = value.Name,
        HexCode = value.HexCode,
        SpendingLimit = value.SpendingLimit
    };

    internal static SavingGoal CloneSavingGoal(SavingGoal value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        TargetAmount = value.TargetAmount,
        CurrentAmount = value.CurrentAmount,
        SavingEndDate = value.SavingEndDate,
        CreatedOn = value.CreatedOn
    };

    internal static RecurringTransaction CloneRecurringScalar(RecurringTransaction value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        Amount = value.Amount,
        RecurringPeriod = value.RecurringPeriod,
        RecurringTime = value.RecurringTime,
        Type = value.Type,
        Category = value.Category,
        SourceId = value.SourceId,
        TagId = value.TagId,
        GoalId = value.GoalId,
        IsEnabled = value.IsEnabled,
        EndDate = value.EndDate,
        Source = null!
    };

    internal static UserSettings CloneUserSetting(UserSettings value) => new()
    {
        Name = value.Name,
        Value = value.Value
    };

    internal static BudgetAllocation CloneBudgetAllocation(BudgetAllocation value) => new()
    {
        Id = value.Id,
        NeedsThreshold = value.NeedsThreshold,
        WantsThreshold = value.WantsThreshold,
        InvestThreshold = value.InvestThreshold,
        AllocationPeriod = value.AllocationPeriod,
        PeriodStart = value.PeriodStart,
        CurrentPeriodIndex = value.CurrentPeriodIndex,
        LastRolloverPeriodStart = value.LastRolloverPeriodStart,
        AllocationLimit = value.AllocationLimit,
        NeedsDebt = value.NeedsDebt,
        WantsDebt = value.WantsDebt,
        InvestDebt = value.InvestDebt,
        RolloverPolicy = value.RolloverPolicy,
        OverspendPolicy = value.OverspendPolicy
    };
}
