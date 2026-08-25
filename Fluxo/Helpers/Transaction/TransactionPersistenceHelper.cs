using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
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

    public void PublishPostCommit(Result result) => PublishPostCommit(result.PostCommit);

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
        PublishPostCommit(() =>
        {
            messenger.Send(new RecordLogMemoryMessage(historyAction));
            messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget |
                DashboardDataInvalidationScope.Notifications |
                (plan.Goal is null ? DashboardDataInvalidationScope.None : DashboardDataInvalidationScope.SavingGoals)));

            if (plan.RepaymentAccountName is { } accountName)
                FloatingNotificationPublisher.Success(messenger, $"Repayment for {accountName} reversed.", string.Empty);
        });

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
            return await AddRepaymentAsync(loaded, pending, account, options, cancellationToken);

        SavingGoal? goal = null;
        Tag? tag = null;
        if (pending.GoalId is { } goalId)
        {
            goal = await appData.GetSavingGoalByIdAsync(goalId, cancellationToken);
            if (goal is null)
                return Result.Failure("Please select a valid goal.");
            tag = await ResolveTagAsync(GoalUpdateTagName, GoalUpdateTagColor, false, cancellationToken);
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

        return await CompleteAsync(
            transaction,
            transaction.Id,
            () =>
            {
                messenger.Send(new RecordLogMemoryMessage(
                    new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(transaction))));
                messenger.Send(new DashboardDataInvalidatedMessage(
                    DashboardDataInvalidationScope.Budget |
                    (options.SuppressNotificationInvalidation
                        ? DashboardDataInvalidationScope.None
                        : DashboardDataInvalidationScope.Notifications) |
                    (goal is null
                        ? DashboardDataInvalidationScope.None
                        : DashboardDataInvalidationScope.SavingGoals)));
            },
            options,
            cancellationToken);
    }

    private async Task<Result> AddRepaymentAsync(
        TransactionVM loaded,
        TransactionVM pending,
        Account source,
        SaveOptions options,
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
        pair.Expense.Account = source;
        pair.Expense.RepaymentAccount = target;
        pair.Income.Account = target;
        pair.Income.RepaymentAccount = target;
        await appData.AddTransactionAsync(pair.Expense, cancellationToken);
        await appData.AddTransactionAsync(pair.Income, cancellationToken);
        appData.UpdateAccount(source);
        appData.UpdateAccount(target);
        return await CompleteAsync(
            pair.Expense,
            pair.Expense.Id,
            () =>
            {
                messenger.Send(new RecordLogMemoryMessage(new CompositeLogMemoryAction(
                    "Repayment",
                    [
                        new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(pair.Expense)),
                        new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(pair.Income))
                    ])));
                messenger.Send(new DashboardDataInvalidatedMessage(
                    DashboardDataInvalidationScope.Budget |
                    (options.SuppressNotificationInvalidation
                        ? DashboardDataInvalidationScope.None
                        : DashboardDataInvalidationScope.Notifications)));
            },
            options,
            cancellationToken);
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

        if (loaded.HasSameValues(pending))
            return Result.Success(loaded.Id, transaction);

        if (options.IsRepayment)
            return await EditRepaymentAsync(loaded, pending, transaction, options, cancellationToken);

        if (pending.GoalId is > 0)
            return await EditGoalAsync(loaded, pending, transaction, options, cancellationToken);

        var oldAccount = transaction.Account;
        if (oldAccount is null)
            return Result.Failure("Unable to load this transaction source.");
        var selectedAccount = await appData.GetAccountByIdAsync(pending.SourceAccountId, cancellationToken);
        if (selectedAccount is null)
            return Result.Failure("Please select a valid account.");
        var sameAccount = oldAccount.Id == selectedAccount.Id;
        var newAccount = sameAccount ? oldAccount : selectedAccount;

        Tag? tag = null;
        if (pending.Tag is { Id: > 0 } pendingTag)
        {
            tag = await appData.GetTagByIdAsync(pendingTag.Id, cancellationToken);
            if (tag is null)
                return Result.Failure("Please select a valid tag.");
        }

        var before = TransactionMemorySnapshot.Create(transaction);
        var newAffectsBalance = TransactionEntity.ShouldAffectAccountBalance(pending.IsIoU, pending.ShouldAffectBalance);
        var accountValidation = await ValidateEditedAccountAsync(
            loaded, pending, transaction, oldAccount, newAccount, newAffectsBalance, options, cancellationToken);
        if (!accountValidation.IsSuccess || accountValidation.RequiresConfirmation)
            return accountValidation;

        ApplyAccountEditBalance(oldAccount, transaction, newAccount, pending, newAffectsBalance);

        var sourceChanged = transaction.SourceAccountId != newAccount.Id;
        ApplyPending(transaction, pending, newAccount, tag,
            options.RelatedRecurringTransactionId ?? transaction.RelatedRecurringTransactionId);
        appData.UpdateTransaction(transaction);

        if (sourceChanged)
        {
            foreach (var child in (await appData.GetTransactionsAsync(cancellationToken))
                         .Where(item => item.ParentTransactionId == transaction.Id && !item.IsForDeletion))
            {
                child.Account = newAccount;
                child.SourceAccountId = newAccount.Id;
                appData.UpdateTransaction(child);
            }
        }

        var changedFields = GetChangedFields(loaded, pending);
        return await CompleteAsync(
            transaction,
            loaded.Id,
            () =>
            {
                messenger.Send(new TransactionDetailUpdatedMessage(new TransactionDetailUpdate(
                    loaded.Id,
                    new TransactionDetailSnapshot(
                        loaded.Amount,
                        loaded.OccurredOn,
                        loaded.ExpenseCategory ?? ExpenseCategory.Needs,
                        loaded.SourceAccountId,
                        loaded.Tag?.Id ?? 0),
                    changedFields,
                    options.SuppressNotificationInvalidation)));
                messenger.Send(new RecordLogMemoryMessage(
                    new EditTransactionMemoryAction(before, TransactionMemorySnapshot.Create(transaction))));
            },
            options,
            cancellationToken);
    }

    private async Task<Result> EditGoalAsync(
        TransactionVM loaded,
        TransactionVM pending,
        TransactionEntity transaction,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        var oldAccount = transaction.Account;
        var newAccount = await appData.GetAccountByIdAsync(pending.SourceAccountId, cancellationToken);
        if (oldAccount is null || newAccount is null)
            return Result.Failure("Please select a valid account.");

        var newAffectsBalance = TransactionEntity.ShouldAffectAccountBalance(pending.IsIoU, pending.ShouldAffectBalance);
        var accountValidation = await ValidateEditedAccountAsync(
            loaded, pending, transaction, oldAccount, newAccount, newAffectsBalance, options, cancellationToken);
        if (!accountValidation.IsSuccess || accountValidation.RequiresConfirmation)
            return accountValidation;

        var goal = await appData.GetSavingGoalByIdAsync(pending.GoalId!.Value, cancellationToken);
        if (goal is null)
            return Result.Failure("Please select a valid goal.");

        var oldGoal = transaction.GoalId is { } oldGoalId && oldGoalId != goal.Id
            ? await appData.GetSavingGoalByIdAsync(oldGoalId, cancellationToken)
            : goal;
        if (oldGoal is null)
            return Result.Failure("Unable to load the linked saving goal.");

        var tag = await ResolveTagAsync(GoalUpdateTagName, GoalUpdateTagColor, false, cancellationToken);
        var before = TransactionMemorySnapshot.Create(transaction);
        var oldAmount = transaction.Amount;
        ApplyAccountEditBalance(
            oldAccount,
            transaction,
            newAccount,
            pending,
            TransactionEntity.ShouldAffectAccountBalance(pending.IsIoU, pending.ShouldAffectBalance));
        ApplyPending(transaction, pending, newAccount, tag,
            options.RelatedRecurringTransactionId ?? transaction.RelatedRecurringTransactionId);
        transaction.Name = BuildGoalUpdateName(goal.Name);
        transaction.Notes = $"Goal update for {goal.Name}";
        transaction.ExpenseCategory = ExpenseCategory.Excluded;
        transaction.IsPinned = false;
        appData.UpdateTransaction(transaction);

        if (oldGoal.Id == goal.Id)
            goal.CurrentAmount += pending.Amount - oldAmount;
        else
        {
            oldGoal.CurrentAmount -= oldAmount;
            goal.CurrentAmount += pending.Amount;
            appData.UpdateSavingGoal(oldGoal);
        }

        appData.UpdateSavingGoal(goal);
        return await CompleteAsync(
            transaction,
            loaded.Id,
            () =>
            {
                messenger.Send(new TransactionDetailUpdatedMessage(new TransactionDetailUpdate(
                    loaded.Id,
                    new TransactionDetailSnapshot(
                        loaded.Amount,
                        loaded.OccurredOn,
                        loaded.ExpenseCategory ?? ExpenseCategory.Savings,
                        loaded.SourceAccountId,
                        loaded.Tag?.Id ?? tag.Id),
                    GetChangedFields(loaded, pending),
                    options.SuppressNotificationInvalidation)));
                messenger.Send(new RecordLogMemoryMessage(
                    new EditTransactionMemoryAction(before, TransactionMemorySnapshot.Create(transaction))));
            },
            options,
            cancellationToken);
    }

    private async Task<Result> EditRepaymentAsync(
        TransactionVM loaded,
        TransactionVM pending,
        TransactionEntity transaction,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        if (transaction.Type != TransactionType.Expense || transaction.RepaymentAccountId is null)
            return Result.Failure("Unable to load this repayment.");

        var income = RepaymentTransactionSupport.FindNewestIncome(
            transaction, await appData.GetTransactionsAsync(cancellationToken));
        if (income is null)
            return Result.Failure("Unable to find the matching repayment income.");

        var source = await appData.GetAccountByIdAsync(pending.SourceAccountId, cancellationToken);
        if (pending.RepaymentAccountId is not { } repaymentAccountId)
            return Result.Failure("Please select a valid credit account.");
        var target = await appData.GetAccountByIdAsync(repaymentAccountId, cancellationToken);
        var oldSource = transaction.SourceAccountId == pending.SourceAccountId
            ? source
            : await appData.GetAccountByIdAsync(transaction.SourceAccountId, cancellationToken);
        var oldTarget = transaction.RepaymentAccountId == repaymentAccountId
            ? target
            : await appData.GetAccountByIdAsync(transaction.RepaymentAccountId.Value, cancellationToken);
        if (source is null || target is null || oldSource is null || oldTarget is null)
            return Result.Failure("Invalid Repayment");

        var accountValidation = await ValidateEditedAccountAsync(
            loaded, pending, transaction, oldSource, source, newAffectsBalance: true, options, cancellationToken);
        if (!accountValidation.IsSuccess || accountValidation.RequiresConfirmation)
            return accountValidation;

        var availableTargetAmount = target is not null && oldTarget is not null && target.Id == oldTarget.Id
            ? target.SpentAmount + transaction.Amount
            : target?.SpentAmount ?? 0m;
        if (source.AccountType != AccountType.Checking ||
            target.AccountType != AccountType.Credit || pending.Amount <= 0m || pending.Amount > availableTargetAmount)
            return Result.Failure("Invalid Repayment");

        var tag = await ResolveTagAsync(SystemTags.BalanceUpdateName, SystemTags.BalanceUpdateHexCode, true,
            cancellationToken);
        var beforeExpense = TransactionMemorySnapshot.Create(transaction);
        var beforeIncome = TransactionMemorySnapshot.Create(income);

        var accounts = new Dictionary<int, Account>
        {
            [source.Id] = source,
            [target.Id] = target
        };
        if (!accounts.ContainsKey(oldSource!.Id))
            accounts[oldSource!.Id] = oldSource;
        if (!accounts.ContainsKey(oldTarget!.Id))
            accounts[oldTarget!.Id] = oldTarget;

        LogMemoryPersistence.RevertTransactionFromAccount(accounts[oldSource.Id], transaction.Type, transaction.Amount);
        LogMemoryPersistence.RevertTransactionFromAccount(accounts[oldTarget.Id], income.Type, income.Amount);
        LogMemoryPersistence.ApplyTransactionToAccount(accounts[source.Id], TransactionType.Expense, pending.Amount);
        LogMemoryPersistence.ApplyTransactionToAccount(accounts[target.Id], TransactionType.Income, pending.Amount);
        foreach (var account in accounts.Values)
            appData.UpdateAccount(account);

        ApplyPending(transaction, pending, source, tag, transaction.RelatedRecurringTransactionId);
        transaction.Name = pending.Name.Trim();
        transaction.ExpenseCategory = ExpenseCategory.Excluded;
        transaction.IsPinned = false;
        income.Type = TransactionType.Income;
        income.SourceAccountId = target.Id;
        income.Account = target;
        income.RepaymentAccountId = target.Id;
        income.Name = $"Repayment from {source.Name}";
        income.Amount = pending.Amount;
        income.OccurredOn = pending.OccurredOn;
        income.Notes = string.Empty;
        income.ExpenseCategory = ExpenseCategory.Excluded;
        income.Tag = tag;
        income.TagId = tag.Id;
        appData.UpdateTransaction(transaction);
        appData.UpdateTransaction(income);
        var invalidationScope = DashboardDataInvalidationScope.Budget;
        if (!options.SuppressNotificationInvalidation)
            invalidationScope |= DashboardDataInvalidationScope.Notifications;
        return await CompleteAsync(
            transaction,
            loaded.Id,
            () =>
            {
                messenger.Send(new RecordLogMemoryMessage(new CompositeLogMemoryAction(
                    "Repayment",
                    [
                        new EditTransactionMemoryAction(beforeExpense, TransactionMemorySnapshot.Create(transaction)),
                        new EditTransactionMemoryAction(beforeIncome, TransactionMemorySnapshot.Create(income))
                    ])));
                messenger.Send(new DashboardDataInvalidatedMessage(invalidationScope));
            },
            options,
            cancellationToken);
    }

    private async Task<Result> CompleteAsync(
        TransactionEntity transaction,
        int transactionId,
        Action postCommit,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        if (options.DeferCommit)
            return Result.Success(transactionId, transaction, postCommit);

        await appData.SaveChangesAsync(cancellationToken);
        PublishPostCommit(postCommit);
        return Result.Success(transaction.Id > 0 ? transaction.Id : transactionId, transaction);
    }

    private static void PublishPostCommit(Action? postCommit)
    {
        if (postCommit is null)
            return;

        try
        {
            postCommit();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(
                exception,
                "Transaction changes were saved, but the current UI could not be refreshed.");
        }
    }

    private void ApplyAccountEditBalance(
        Account oldAccount,
        TransactionEntity transaction,
        Account newAccount,
        TransactionVM pending,
        bool newAffectsBalance)
    {
        if (oldAccount.Id == newAccount.Id)
        {
            if (transaction.AffectsAccountBalance)
                LogMemoryPersistence.RevertTransactionFromAccount(oldAccount, transaction.Type, transaction.Amount);
            if (newAffectsBalance)
                LogMemoryPersistence.ApplyTransactionToAccount(oldAccount, pending.Type, pending.Amount);
            if (transaction.AffectsAccountBalance || newAffectsBalance)
                appData.UpdateAccount(oldAccount);
            return;
        }

        if (transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.RevertTransactionFromAccount(oldAccount, transaction.Type, transaction.Amount);
            appData.UpdateAccount(oldAccount);
        }
        if (newAffectsBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(newAccount, pending.Type, pending.Amount);
            appData.UpdateAccount(newAccount);
        }
    }

    private async Task<Result> ValidateEditedAccountAsync(
        TransactionVM loaded,
        TransactionVM pending,
        TransactionEntity transaction,
        Account oldAccount,
        Account newAccount,
        bool newAffectsBalance,
        SaveOptions options,
        CancellationToken cancellationToken)
    {
        if (pending.Type != TransactionType.Expense || !newAffectsBalance)
            return Result.Success(loaded.Id);

        var (projectedBalance, projectedSpent) = ProjectAccountState(
            transaction, pending, oldAccount, newAccount, newAffectsBalance);

        if (newAccount.AccountType == AccountType.Credit)
        {
            if (newAccount.AccountLimit > 0m && projectedSpent > newAccount.AccountLimit)
                return Result.Failure("Amount exceeds this source's account limit.");

            if (!options.AllowMaximumSpendingOverflow && newAccount.MaximumSpending > 0m &&
                projectedSpent > newAccount.MaximumSpending)
                return Result.Confirmation($"This expense exceeds {newAccount.Name}'s maximum spending limit. Save anyway?");
        }
        else
        {
            if (projectedBalance < 0m)
                return Result.Failure("Amount exceeds this source's available balance.");

            if (!options.AllowMaximumSpendingOverflow && newAccount.MaximumSpending > 0m)
            {
                var currentSpending = (await appData.GetTransactionsAsync(cancellationToken))
                    .Where(item => item.Id != transaction.Id && item.SourceAccountId == newAccount.Id &&
                                   item.Type == TransactionType.Expense && item.AffectsAccountBalance && !item.IsForDeletion)
                    .Sum(item => item.Amount);
                if (currentSpending + pending.Amount > newAccount.MaximumSpending)
                    return Result.Confirmation($"This expense exceeds {newAccount.Name}'s maximum spending limit. Save anyway?");
            }
        }

        return Result.Success(loaded.Id);
    }

    private static (decimal Balance, decimal SpentAmount) ProjectAccountState(
        TransactionEntity transaction,
        TransactionVM pending,
        Account oldAccount,
        Account newAccount,
        bool newAffectsBalance)
    {
        var balance = newAccount.Balance;
        var spentAmount = newAccount.SpentAmount;
        if (oldAccount.Id == newAccount.Id && transaction.AffectsAccountBalance)
            RevertAccountEffect(transaction.Type, transaction.Amount, newAccount.AccountType, ref balance, ref spentAmount);

        if (newAffectsBalance)
            ApplyAccountEffect(pending.Type, pending.Amount, newAccount.AccountType, ref balance, ref spentAmount);

        return (balance, spentAmount);
    }

    private static void ApplyAccountEffect(
        TransactionType type,
        decimal amount,
        AccountType accountType,
        ref decimal balance,
        ref decimal spentAmount)
    {
        if (accountType == AccountType.Credit)
        {
            spentAmount = type == TransactionType.Expense
                ? spentAmount + amount
                : Math.Max(0m, spentAmount - amount);
            return;
        }

        balance += type == TransactionType.Expense ? -amount : amount;
    }

    private static void RevertAccountEffect(
        TransactionType type,
        decimal amount,
        AccountType accountType,
        ref decimal balance,
        ref decimal spentAmount)
    {
        if (accountType == AccountType.Credit)
        {
            spentAmount = type == TransactionType.Expense
                ? Math.Max(0m, spentAmount - amount)
                : spentAmount + amount;
            return;
        }

        balance += type == TransactionType.Expense ? amount : -amount;
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
            transaction.ExpenseCategory = ExpenseCategory.Excluded;
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
        transaction.ExpenseCategory = pending.ExpenseCategory;
        transaction.Tag = tag;
        transaction.TagId = tag?.Id;
        transaction.IsPinned = pending.IsPinned;
        transaction.IsIoU = pending.IsIoU;
        transaction.ShouldAffectBalance = pending.ShouldAffectBalance;
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
        return fields;
    }

    public readonly record struct SaveOptions(
        bool AllowMaximumSpendingOverflow = false,
        bool IsRepayment = false,
        int? RelatedRecurringTransactionId = null,
        bool SuppressNotificationInvalidation = false,
        bool DeferCommit = false);

    public readonly record struct Result(
        bool IsSuccess,
        string? ErrorMessage,
        bool RequiresConfirmation,
        int TransactionId,
        TransactionEntity? Transaction,
        Action? PostCommit)
    {
        public static Result Success(
            int transactionId,
            TransactionEntity? transaction = null,
            Action? postCommit = null) =>
            new(true, null, false, transactionId, transaction, postCommit);

        public static Result Failure(string? errorMessage) => new(false, errorMessage, false, 0, null, null);
        public static Result Confirmation(string message) => new(false, message, true, 0, null, null);
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
