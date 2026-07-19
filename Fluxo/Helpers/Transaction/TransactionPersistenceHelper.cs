using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Notifications;
using Fluxo.Services.Transactions;
using Fluxo.ViewModels.Entities;
using TransactionEntity = Fluxo.Core.Entities.Transaction;

namespace Fluxo.Helpers.Transaction;

public sealed class TransactionPersistenceHelper(IAppDataService appData, IMessenger messenger)
{
    private const string GoalUpdateTagName = "Goal Update";
    private const string GoalUpdateTagColor = "#aed4e1";
    public async Task<Result> SaveAsync(
        TransactionVM loaded,
        TransactionVM pending,
        SaveOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(pending);

        return loaded.Id == 0
            ? await AddAsync(loaded, pending, options, cancellationToken)
            : await EditAsync(loaded, pending, options, cancellationToken);
    }

    public async Task<Result> DeleteAsync(TransactionVM loaded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (loaded.Id <= 0)
            return Result.Failure("Unable to load this transaction.");

        var transaction = await appData.GetTransactionByIdAsync(loaded.Id, cancellationToken);
        if (transaction is null)
            return Result.Failure("Unable to load this transaction.");

        var plan = await BuildDeletionPlanAsync(transaction, cancellationToken);
        if (!plan.IsSuccess)
            return Result.Failure(plan.ErrorMessage);

        var snapshots = plan.Transactions.Select(TransactionMemorySnapshot.Create).ToList();
        foreach (var item in plan.Transactions)
        {
            if (item.AffectsAccountBalance)
            {
                LogMemoryPersistence.RevertTransactionFromAccount(item.Account, item.Type, item.Amount);
                appData.UpdateAccount(item.Account);
            }

            appData.RemoveTransaction(item);
        }

        if (plan.Goal is { } goal)
        {
            goal.CurrentAmount -= transaction.Amount;
            appData.UpdateSavingGoal(goal);
        }

        await appData.SaveChangesAsync(cancellationToken);

        ILogMemoryAction historyAction = snapshots.Count == 1
            ? new DeleteTransactionMemoryAction(snapshots[0])
            : new CompositeLogMemoryAction(
                "Reverse repayment",
                snapshots.Select(snapshot => (ILogMemoryAction)new DeleteTransactionMemoryAction(snapshot)).ToList());
        messenger.Send(new RecordLogMemoryMessage(historyAction));
        messenger.Send(new DashboardDataInvalidatedMessage(
            DashboardDataInvalidationScope.Budget |
            (plan.Goal is null ? DashboardDataInvalidationScope.None : DashboardDataInvalidationScope.SavingGoals)));

        if (plan.RepaymentAccountName is { } accountName)
            FloatingNotificationPublisher.Success(messenger, $"Repayment for {accountName} reversed.", string.Empty);

        return Result.Success(loaded.Id);
    }

    private async Task<Result> AddAsync(
        TransactionVM loaded,
        TransactionVM pending,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        var account = await appData.GetAccountByIdAsync(pending.SourceAccountId, cancellationToken);
        if (account is null)
            return Result.Failure("Please select a valid account.");

        if (options.IsRepayment)
            return await AddRepaymentAsync(loaded, pending, account, cancellationToken);

        SavingGoal? goal = null;
        Tag? tag = null;
        if (pending.GoalId is { } goalId)
        {
            goal = await appData.GetSavingGoalByIdAsync(goalId, cancellationToken);
            if (goal is null)
                return Result.Failure("Please select a valid goal.");
            tag = await ResolveTagAsync(GoalUpdateTagName, GoalUpdateTagColor, true, cancellationToken);
        }
        else if (pending.Tag is { Id: > 0 } pendingTag)
        {
            tag = await appData.GetTagByIdAsync(pendingTag.Id, cancellationToken);
            if (tag is null)
                return Result.Failure("Please select a valid tag.");
        }

        var transaction = CreateTransaction(pending, account, tag, goal, options.RelatedRecurringTransactionId);
        await appData.AddTransactionAsync(transaction, cancellationToken);

        if (transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(account, transaction.Type, transaction.Amount);
            appData.UpdateAccount(account);
        }

        if (goal is not null)
        {
            goal.CurrentAmount += transaction.Amount;
            appData.UpdateSavingGoal(goal);
        }

        await appData.SaveChangesAsync(cancellationToken);
        messenger.Send(new RecordLogMemoryMessage(
            new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(transaction))));
        messenger.Send(new DashboardDataInvalidatedMessage(
            DashboardDataInvalidationScope.Budget |
            DashboardDataInvalidationScope.Notifications |
            (goal is null ? DashboardDataInvalidationScope.None : DashboardDataInvalidationScope.SavingGoals)));
        return Result.Success(transaction.Id);
    }

    private async Task<Result> AddRepaymentAsync(
        TransactionVM loaded,
        TransactionVM pending,
        Account source,
        CancellationToken cancellationToken)
    {
        if (pending.RepaymentAccountId is not { } repaymentAccountId)
            return Result.Failure("Please select a valid credit account.");

        var target = await appData.GetAccountByIdAsync(repaymentAccountId, cancellationToken);
        if (target is null || target.AccountType != AccountType.Credit)
            return Result.Failure("Please select a valid credit account.");
        if (pending.Amount > target.SpentAmount)
            return Result.Failure("Invalid Repayment");

        var tag = await ResolveTagAsync(SystemTags.BalanceUpdateName, SystemTags.BalanceUpdateHexCode, true,
            cancellationToken);
        var pair = RepaymentTransactionSupport.Create(source, target, pending.Amount, pending.OccurredOn, tag,
            pending.Name);
        await appData.AddTransactionAsync(pair.Expense, cancellationToken);
        await appData.AddTransactionAsync(pair.Income, cancellationToken);
        appData.UpdateAccount(source);
        appData.UpdateAccount(target);
        await appData.SaveChangesAsync(cancellationToken);

        messenger.Send(new RecordLogMemoryMessage(new CompositeLogMemoryAction(
            "Repayment",
            [
                new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(pair.Expense)),
                new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(pair.Income))
            ])));
        messenger.Send(new DashboardDataInvalidatedMessage(
            DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));
        return Result.Success(pair.Expense.Id);
    }

    private async Task<Result> EditAsync(
        TransactionVM loaded,
        TransactionVM pending,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        var transaction = await appData.GetTransactionByIdAsync(loaded.Id, cancellationToken);
        if (transaction is null || transaction.Type != loaded.Type)
            return Result.Failure("Unable to load this transaction.");

        if (loaded.Equals(pending))
            return Result.Success(loaded.Id);

        var oldAccount = transaction.Account;
        if (oldAccount is null)
            return Result.Failure("Unable to load this transaction source.");
        var newAccount = await appData.GetAccountByIdAsync(pending.SourceAccountId, cancellationToken);
        if (newAccount is null)
            return Result.Failure("Please select a valid account.");

        Tag? tag = null;
        if (pending.Tag is { Id: > 0 } pendingTag)
        {
            tag = await appData.GetTagByIdAsync(pendingTag.Id, cancellationToken);
            if (tag is null)
                return Result.Failure("Please select a valid tag.");
        }

        if (!options.AllowMaximumSpendingOverflow && pending.Type == TransactionType.Expense &&
            TransactionEntity.ShouldAffectAccountBalance(pending.IsIoU, pending.ShouldAffectBalance) &&
            newAccount.MaximumSpending > 0m)
        {
            var currentSpending = newAccount.AccountType == AccountType.Credit
                ? newAccount.SpentAmount
                : (await appData.GetTransactionsAsync(cancellationToken))
                    .Where(item => item.Id != transaction.Id && item.SourceAccountId == newAccount.Id &&
                                   item.Type == TransactionType.Expense && item.AffectsAccountBalance && !item.IsForDeletion)
                    .Sum(item => item.Amount);
            if (currentSpending + pending.Amount > newAccount.MaximumSpending)
                return Result.Confirmation($"This expense exceeds {newAccount.Name}'s maximum spending limit. Save anyway?");
        }

        var before = TransactionMemorySnapshot.Create(transaction);
        if (transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.RevertTransactionFromAccount(oldAccount, transaction.Type, transaction.Amount);
            appData.UpdateAccount(oldAccount);
        }

        var newAffectsBalance = TransactionEntity.ShouldAffectAccountBalance(pending.IsIoU, pending.ShouldAffectBalance);
        if (newAffectsBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(newAccount, pending.Type, pending.Amount);
            appData.UpdateAccount(newAccount);
        }

        var sourceChanged = transaction.SourceAccountId != newAccount.Id;
        var exclusionChanged = transaction.IsExcludedFromBudget != pending.IsExcludedFromBudget;
        ApplyPending(transaction, pending, newAccount, tag, options.RelatedRecurringTransactionId);
        appData.UpdateTransaction(transaction);

        if (sourceChanged || exclusionChanged)
        {
            foreach (var child in (await appData.GetTransactionsAsync(cancellationToken))
                         .Where(item => item.ParentTransactionId == transaction.Id && !item.IsForDeletion))
            {
                child.Account = newAccount;
                child.SourceAccountId = newAccount.Id;
                child.IsExcludedFromBudget = pending.IsExcludedFromBudget;
                appData.UpdateTransaction(child);
            }
        }

        await appData.SaveChangesAsync(cancellationToken);
        var changedFields = GetChangedFields(loaded, pending);
        messenger.Send(new TransactionDetailUpdatedMessage(new TransactionDetailUpdate(
            loaded.Id,
            new TransactionDetailSnapshot(
                loaded.Amount,
                loaded.OccurredOn,
                loaded.ExpenseCategory ?? ExpenseCategory.Needs,
                loaded.SourceAccountId,
                loaded.Tag?.Id ?? 0),
            changedFields)));
        messenger.Send(new RecordLogMemoryMessage(
            new EditTransactionMemoryAction(before, TransactionMemorySnapshot.Create(transaction))));
        messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.Budget));
        return Result.Success(loaded.Id);
    }

    private async Task<Tag> ResolveTagAsync(
        string name,
        string color,
        bool isSystem,
        CancellationToken cancellationToken)
    {
        var existing = (await appData.GetTagsAsync(cancellationToken)).FirstOrDefault(tag =>
            string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var tag = new Tag { Name = name, HexCode = color, IsSystemTag = isSystem };
        await appData.AddTagAsync(tag, cancellationToken);
        return tag;
    }

    private async Task<DeletionPlan> BuildDeletionPlanAsync(TransactionEntity transaction, CancellationToken cancellationToken)
    {
        if (string.Equals(transaction.Tag?.Name, GoalUpdateTagName,
                StringComparison.OrdinalIgnoreCase) && transaction.GoalId is { } goalId)
        {
            var goal = await appData.GetSavingGoalByIdAsync(goalId, cancellationToken);
            return goal is null
                ? DeletionPlan.Failure("Unable to load the linked saving goal.")
                : DeletionPlan.Success([transaction], goal);
        }

        if (transaction.Type == TransactionType.Expense && transaction.RepaymentAccountId is not null &&
            string.Equals(transaction.Tag?.Name, SystemTags.BalanceUpdateName, StringComparison.OrdinalIgnoreCase))
        {
            var income = RepaymentTransactionSupport.FindNewestIncome(
                transaction, await appData.GetTransactionsAsync(cancellationToken));
            if (income is null)
                return DeletionPlan.Failure("Unable to find the matching repayment income.");
            return DeletionPlan.Success(
                [transaction, income],
                repaymentAccountName: transaction.RepaymentAccount?.Name ?? income.Account.Name);
        }

        return DeletionPlan.Success([transaction]);
    }

    private static TransactionEntity CreateTransaction(
        TransactionVM pending,
        Account account,
        Tag? tag,
        SavingGoal? goal,
        int? relatedRecurringTransactionId)
    {
        var transaction = new TransactionEntity();
        ApplyPending(transaction, pending, account, tag, relatedRecurringTransactionId);
        if (goal is not null)
        {
            transaction.Name = BuildGoalUpdateName(goal.Name);
            transaction.Notes = $"Goal update for {goal.Name}";
            transaction.ExpenseCategory = ExpenseCategory.Savings;
            transaction.Tag = tag;
            transaction.TagId = tag?.Id;
            transaction.IsPinned = false;
        }
        return transaction;
    }

    private static void ApplyPending(
        TransactionEntity transaction,
        TransactionVM pending,
        Account account,
        Tag? tag,
        int? relatedRecurringTransactionId)
    {
        transaction.Type = pending.Type;
        transaction.SourceAccountId = account.Id;
        transaction.Account = account;
        transaction.GoalId = pending.GoalId;
        transaction.RepaymentAccountId = pending.RepaymentAccountId;
        transaction.RelatedRecurringTransactionId = relatedRecurringTransactionId;
        transaction.Name = pending.Type == TransactionType.Expense
            ? BuildTransactionName(pending.Name, pending.Notes, tag?.Name ?? string.Empty)
            : pending.Name.Trim();
        transaction.Amount = pending.Amount;
        transaction.OccurredOn = pending.OccurredOn;
        transaction.Notes = pending.Notes;
        transaction.ExpenseCategory = pending.Type == TransactionType.Expense ? pending.ExpenseCategory : null;
        transaction.Tag = tag;
        transaction.TagId = tag?.Id;
        transaction.IsPinned = pending.IsPinned;
        transaction.IsIoU = pending.IsIoU;
        transaction.ShouldAffectBalance = pending.ShouldAffectBalance;
        transaction.IsExcludedFromBudget = pending.IsExcludedFromBudget;
    }

    internal static string BuildTransactionName(string name, string note, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        if (string.IsNullOrWhiteSpace(note))
            return fallbackName;
        return note.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                   .Select(line => line.Trim())
                   .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? fallbackName;
    }

    private static string BuildGoalUpdateName(string goalName) =>
        string.IsNullOrWhiteSpace(goalName)
            ? GoalUpdateTagName
            : $"{GoalUpdateTagName}: {goalName.Trim()}";

    private static TransactionDetailChangedFields GetChangedFields(TransactionVM loaded, TransactionVM pending)
    {
        var fields = TransactionDetailChangedFields.None;
        if (loaded.Name != pending.Name) fields |= TransactionDetailChangedFields.Name;
        if (loaded.Amount != pending.Amount) fields |= TransactionDetailChangedFields.Amount;
        if (loaded.OccurredOn.Date != pending.OccurredOn.Date) fields |= TransactionDetailChangedFields.Date;
        if (loaded.ExpenseCategory != pending.ExpenseCategory) fields |= TransactionDetailChangedFields.Category;
        if (loaded.SourceAccountId != pending.SourceAccountId) fields |= TransactionDetailChangedFields.Account;
        if (loaded.Tag?.Id != pending.Tag?.Id) fields |= TransactionDetailChangedFields.Tag;
        if (loaded.Notes != pending.Notes) fields |= TransactionDetailChangedFields.Note;
        if (loaded.IsPinned != pending.IsPinned) fields |= TransactionDetailChangedFields.Pin;
        if (loaded.IsIoU != pending.IsIoU || loaded.ShouldAffectBalance != pending.ShouldAffectBalance)
            fields |= TransactionDetailChangedFields.IoU;
        if (loaded.IsExcludedFromBudget != pending.IsExcludedFromBudget)
            fields |= TransactionDetailChangedFields.BudgetExclusion;
        return fields;
    }

    public readonly record struct SaveOptions(
        bool AllowMaximumSpendingOverflow = false,
        bool IsRepayment = false,
        int? RelatedRecurringTransactionId = null);

    public readonly record struct Result(
        bool IsSuccess,
        string? ErrorMessage,
        bool RequiresConfirmation,
        int TransactionId)
    {
        public static Result Success(int transactionId) => new(true, null, false, transactionId);
        public static Result Failure(string? errorMessage) => new(false, errorMessage, false, 0);
        public static Result Confirmation(string message) => new(false, message, true, 0);
    }

    private readonly record struct DeletionPlan(
        bool IsSuccess,
        string? ErrorMessage,
        IReadOnlyList<TransactionEntity> Transactions,
        SavingGoal? Goal,
        string? RepaymentAccountName)
    {
        public static DeletionPlan Success(
            IReadOnlyList<TransactionEntity> transactions,
            SavingGoal? goal = null,
            string? repaymentAccountName = null) =>
            new(true, null, transactions, goal, repaymentAccountName);
        public static DeletionPlan Failure(string errorMessage) => new(false, errorMessage, [], null, null);
    }
}
