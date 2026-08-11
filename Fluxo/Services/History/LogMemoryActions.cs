using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces;
using System.Globalization;

namespace Fluxo.Services.History;

public interface ILogMemoryAction : Fluxo.Core.Interfaces.History.ILogMemoryAction;

public sealed record AccountMemorySnapshot(
    int AccountId,
    string Name,
    AccountType AccountType,
    decimal AccountLimit,
    decimal MaximumSpending,
    decimal? MinimumPayment,
    decimal SpentAmount,
    decimal Balance,
    int? MonthlyDueDate,
    int? DeductSource,
    decimal? InterestRate,
    bool PinnedOnUI,
    bool IsEnabled,
    bool IsDefault = false)
{
    public static AccountMemorySnapshot Create(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountMemorySnapshot(
            account.Id,
            account.Name,
            account.AccountType,
            account.AccountLimit,
            account.MaximumSpending,
            account.MinimumPayment,
            account.SpentAmount,
            account.Balance,
            account.MonthlyDueDate,
            account.DeductSource,
            account.InterestRate,
            account.PinnedOnUI,
            account.IsEnabled,
            account.IsDefault);
    }
}

public sealed record TagMemorySnapshot(
    int TagId,
    string Name,
    string HexCode,
    decimal? SpendingLimit)
{
    public static TagMemorySnapshot Create(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return new TagMemorySnapshot(
            tag.Id,
            tag.Name,
            tag.HexCode,
            tag.SpendingLimit);
    }
}

public sealed record SavingGoalMemorySnapshot(
    int SavingGoalId,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateTime? SavingEndDate,
    DateTime CreatedOn)
{
    public static SavingGoalMemorySnapshot Create(SavingGoal savingGoal)
    {
        ArgumentNullException.ThrowIfNull(savingGoal);

        return new SavingGoalMemorySnapshot(
            savingGoal.Id,
            savingGoal.Name,
            savingGoal.TargetAmount,
            savingGoal.CurrentAmount,
            savingGoal.SavingEndDate,
            savingGoal.CreatedOn);
    }
}

public sealed record UserSettingMemorySnapshot(
    string Name,
    string Value,
    bool Exists)
{
    public static UserSettingMemorySnapshot Create(UserSettings userSettings)
    {
        ArgumentNullException.ThrowIfNull(userSettings);

        return new UserSettingMemorySnapshot(
            userSettings.Name,
            userSettings.Value,
            true);
    }

    public static UserSettingMemorySnapshot Missing(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new UserSettingMemorySnapshot(name, string.Empty, false);
    }
}

public sealed record TransactionMemorySnapshot(
    int TransactionId,
    TransactionType Type,
    int SourceAccountId,
    string Name,
    decimal Amount,
    DateTime OccurredOn,
    string Notes,
    ExpenseCategory? ExpenseCategory,
    int? TagId,
    int? GoalId,
    int? RepaymentAccountId,
    int? ParentTransactionId,
    bool IsPinned,
    bool IsForDeletion,
    bool IsIoU,
    DateTime LoggedOn = default,
    bool ShouldAffectBalance = false)
{
    public bool AffectsAccountBalance =>
        Transaction.ShouldAffectAccountBalance(IsIoU, ShouldAffectBalance);

    public static TransactionMemorySnapshot Create(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return new TransactionMemorySnapshot(
            transaction.Id,
            transaction.Type,
            transaction.SourceAccountId,
            transaction.Name,
            transaction.Amount,
            transaction.OccurredOn,
            transaction.Notes,
            transaction.ExpenseCategory,
            transaction.TagId,
            transaction.GoalId,
            transaction.RepaymentAccountId,
            transaction.ParentTransactionId,
            transaction.IsPinned,
            transaction.IsForDeletion,
            transaction.IsIoU,
            transaction.LoggedOn,
            transaction.ShouldAffectBalance);
    }
}

public sealed class AddTransactionMemoryAction(
    TransactionMemorySnapshot snapshot,
    bool shouldAdjustAccountTotals = true) : ILogMemoryAction
{
    public TransactionMemorySnapshot Snapshot => snapshot;
    public string Description => snapshot.Type == TransactionType.Expense ? "Add expense" : "Add income";
    public string Title => $"{snapshot.Name} Added";
    public string Summary => $"{LogMemoryDisplay.TransactionNoun(snapshot.Type)} added";
    public string Details => $"{LogMemoryDisplay.Amount(snapshot.Amount)} · {LogMemoryDisplay.Date(snapshot.OccurredOn)}";

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        var transaction = await unitOfWork.Transactions.GetByIdAsync(snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            return;

        if (shouldAdjustAccountTotals && transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.RevertTransactionFromAccount(transaction.Account, transaction.Type, transaction.Amount);
            unitOfWork.Accounts.Update(transaction.Account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            unitOfWork, snapshot.GoalId, -snapshot.Amount, cancellationToken);
        unitOfWork.Transactions.Remove(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.Transactions.GetByIdAsync(snapshot.TransactionId, cancellationToken) is not null)
            return;

        var account = await LogMemoryPersistence.GetRequiredAccountAsync(
            unitOfWork, snapshot.SourceAccountId, cancellationToken);
        var transaction = CreateTransaction(snapshot, account);
        await unitOfWork.Transactions.AddAsync(transaction, cancellationToken);
        if (shouldAdjustAccountTotals && snapshot.AffectsAccountBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(account, snapshot.Type, snapshot.Amount);
            unitOfWork.Accounts.Update(account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            unitOfWork, snapshot.GoalId, snapshot.Amount, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    internal static Transaction CreateTransaction(TransactionMemorySnapshot value, Account account) => new()
    {
        Id = value.TransactionId,
        Type = value.Type,
        SourceAccountId = value.SourceAccountId,
        Account = account,
        Name = value.Name,
        Amount = value.Amount,
        OccurredOn = value.OccurredOn,
        Notes = value.Notes,
        ExpenseCategory = value.ExpenseCategory,
        TagId = value.TagId,
        GoalId = value.GoalId,
        RepaymentAccountId = value.RepaymentAccountId,
        ParentTransactionId = value.ParentTransactionId,
        IsPinned = value.IsPinned,
        IsForDeletion = value.IsForDeletion,
        IsIoU = value.IsIoU,
        ShouldAffectBalance = value.ShouldAffectBalance
    };
}

public sealed class EditTransactionMemoryAction(
    TransactionMemorySnapshot before,
    TransactionMemorySnapshot after) : ILogMemoryAction
{
    public TransactionMemorySnapshot Before => before;
    public TransactionMemorySnapshot After => after;
    public string Description => "Edit transaction";
    public string Title => $"{after.Name} Updated";
    public string Summary => $"{LogMemoryDisplay.TransactionNoun(after.Type)} information updated";
    public string Details => LogMemoryDisplay.Changes(
        ("Name", before.Name, after.Name),
        ("Type", before.Type.ToString(), after.Type.ToString()),
        ("Amount", LogMemoryDisplay.Amount(before.Amount), LogMemoryDisplay.Amount(after.Amount)),
        ("Date", LogMemoryDisplay.Date(before.OccurredOn), LogMemoryDisplay.Date(after.OccurredOn)),
        ("Category", before.ExpenseCategory?.ToString() ?? "None", after.ExpenseCategory?.ToString() ?? "None"),
        ("Notes", LogMemoryDisplay.Text(before.Notes), LogMemoryDisplay.Text(after.Notes)),
        ("Pinned", LogMemoryDisplay.YesNo(before.IsPinned), LogMemoryDisplay.YesNo(after.IsPinned)));
    public Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default) =>
        ApplyAsync(unitOfWork, before, cancellationToken);
    public Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default) =>
        ApplyAsync(unitOfWork, after, cancellationToken);

    private static async Task ApplyAsync(IUnitOfWork unitOfWork, TransactionMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var transaction = await unitOfWork.Transactions.GetByIdAsync(snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            return;

        var oldAccount = transaction.Account;
        var newAccount = await LogMemoryPersistence.GetRequiredAccountAsync(
            unitOfWork, snapshot.SourceAccountId, cancellationToken);
        var oldAffectsBalance = transaction.AffectsAccountBalance;
        if (oldAffectsBalance)
            LogMemoryPersistence.RevertTransactionFromAccount(oldAccount, transaction.Type, transaction.Amount);
        if (snapshot.AffectsAccountBalance)
            LogMemoryPersistence.ApplyTransactionToAccount(newAccount, snapshot.Type, snapshot.Amount);

        transaction.Type = snapshot.Type;
        transaction.SourceAccountId = snapshot.SourceAccountId;
        transaction.Account = newAccount;
        transaction.Name = snapshot.Name;
        transaction.Amount = snapshot.Amount;
        transaction.OccurredOn = snapshot.OccurredOn;
        transaction.Notes = snapshot.Notes;
        transaction.ExpenseCategory = snapshot.ExpenseCategory;
        transaction.TagId = snapshot.TagId;
        transaction.GoalId = snapshot.GoalId;
        transaction.RepaymentAccountId = snapshot.RepaymentAccountId;
        transaction.ParentTransactionId = snapshot.ParentTransactionId;
        transaction.IsPinned = snapshot.IsPinned;
        transaction.IsForDeletion = snapshot.IsForDeletion;
        transaction.IsIoU = snapshot.IsIoU;
        transaction.ShouldAffectBalance = snapshot.ShouldAffectBalance;

        unitOfWork.Transactions.Update(transaction);
        if (oldAffectsBalance)
            unitOfWork.Accounts.Update(oldAccount);
        if (snapshot.AffectsAccountBalance && (!ReferenceEquals(oldAccount, newAccount) || !oldAffectsBalance))
            unitOfWork.Accounts.Update(newAccount);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeleteTransactionMemoryAction(TransactionMemorySnapshot snapshot) : ILogMemoryAction
{
    public TransactionMemorySnapshot Snapshot => snapshot;
    public string Description => "Delete transaction";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => $"{LogMemoryDisplay.TransactionNoun(snapshot.Type)} deleted";
    public string Details => $"{LogMemoryDisplay.Amount(snapshot.Amount)} · {LogMemoryDisplay.Date(snapshot.OccurredOn)}";

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.Transactions.GetByIdAsync(snapshot.TransactionId, cancellationToken) is not null)
            return;

        var account = await LogMemoryPersistence.GetRequiredAccountAsync(
            unitOfWork, snapshot.SourceAccountId, cancellationToken);
        await unitOfWork.Transactions.AddAsync(AddTransactionMemoryAction.CreateTransaction(snapshot, account), cancellationToken);
        if (snapshot.AffectsAccountBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(account, snapshot.Type, snapshot.Amount);
            unitOfWork.Accounts.Update(account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            unitOfWork, snapshot.GoalId, snapshot.Amount, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        var transaction = await unitOfWork.Transactions.GetByIdAsync(snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            return;

        if (transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.RevertTransactionFromAccount(transaction.Account, transaction.Type, transaction.Amount);
            unitOfWork.Accounts.Update(transaction.Account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            unitOfWork, snapshot.GoalId, -snapshot.Amount, cancellationToken);
        unitOfWork.Transactions.Remove(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CompositeLogMemoryAction(string description, IReadOnlyList<ILogMemoryAction> actions)
    : ILogMemoryAction
{
    public string Description { get; } = description;
    public IReadOnlyList<ILogMemoryAction> Actions { get; } = actions;
    public string Title => $"{Description} Completed";
    public string Summary => $"{Description} completed";
    public string Details => string.Join(" · ", Actions.Select(action => action.Title));

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        foreach (var action in actions.Reverse())
            await action.RevertAsync(unitOfWork, cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        foreach (var action in actions)
            await action.ReapplyAsync(unitOfWork, cancellationToken);
    }
}

public sealed class AddAccountMemoryAction(AccountMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Add account";
    public string Title => $"{snapshot.Name} Added";
    public string Summary => "Account added";
    public string Details => $"{snapshot.AccountType} · Balance {LogMemoryDisplay.Amount(snapshot.Balance)}";

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        var account =
            await unitOfWork.Accounts.GetByIdAsync(snapshot.AccountId, cancellationToken);
        if (account is null)
            return;

        unitOfWork.Accounts.Remove(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.Accounts.GetByIdAsync(snapshot.AccountId, cancellationToken) is not null)
            return;

        var account = new Account
        {
            Id = snapshot.AccountId,
            Name = snapshot.Name,
            AccountType = snapshot.AccountType,
            AccountLimit = snapshot.AccountLimit,
            MaximumSpending = snapshot.MaximumSpending,
            MinimumPayment = snapshot.MinimumPayment,
            SpentAmount = snapshot.SpentAmount,
            Balance = snapshot.Balance,
            MonthlyDueDate = snapshot.MonthlyDueDate,
            DeductSource = snapshot.DeductSource,
            InterestRate = snapshot.InterestRate,
            PinnedOnUI = snapshot.PinnedOnUI,
            IsEnabled = snapshot.IsEnabled,
            IsDefault = snapshot.IsDefault
        };

        await unitOfWork.Accounts.AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class EditAccountMemoryAction(
    AccountMemorySnapshot before,
    AccountMemorySnapshot after) : ILogMemoryAction
{
    public string Description => "Edit account";
    public string Title => $"{after.Name} Updated";
    public string Summary => "Account information updated";
    public string Details => LogMemoryDisplay.Changes(
        ("Name", before.Name, after.Name),
        ("Type", before.AccountType.ToString(), after.AccountType.ToString()),
        ("Balance", LogMemoryDisplay.Amount(before.Balance), LogMemoryDisplay.Amount(after.Balance)),
        ("Account limit", LogMemoryDisplay.Amount(before.AccountLimit), LogMemoryDisplay.Amount(after.AccountLimit)),
        ("Maximum spending", LogMemoryDisplay.Amount(before.MaximumSpending), LogMemoryDisplay.Amount(after.MaximumSpending)),
        ("Minimum payment", LogMemoryDisplay.OptionalAmount(before.MinimumPayment), LogMemoryDisplay.OptionalAmount(after.MinimumPayment)),
        ("Pinned", LogMemoryDisplay.YesNo(before.PinnedOnUI), LogMemoryDisplay.YesNo(after.PinnedOnUI)),
        ("Enabled", LogMemoryDisplay.YesNo(before.IsEnabled), LogMemoryDisplay.YesNo(after.IsEnabled)),
        ("Default", LogMemoryDisplay.YesNo(before.IsDefault), LogMemoryDisplay.YesNo(after.IsDefault)));

    public Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(unitOfWork, before, cancellationToken);
    }

    public Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(unitOfWork, after, cancellationToken);
    }

    private static async Task ApplySnapshotAsync(IUnitOfWork unitOfWork, AccountMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var account =
            await LogMemoryPersistence.GetRequiredAccountAsync(unitOfWork, snapshot.AccountId,
                cancellationToken);

        account.Name = snapshot.Name;
        account.AccountType = snapshot.AccountType;
        account.AccountLimit = snapshot.AccountLimit;
        account.MaximumSpending = snapshot.MaximumSpending;
        account.MinimumPayment = snapshot.MinimumPayment;
        account.SpentAmount = snapshot.SpentAmount;
        account.Balance = snapshot.Balance;
        account.MonthlyDueDate = snapshot.MonthlyDueDate;
        account.DeductSource = snapshot.DeductSource;
        account.InterestRate = snapshot.InterestRate;
        account.PinnedOnUI = snapshot.PinnedOnUI;
        account.IsEnabled = snapshot.IsEnabled;
        account.IsDefault = snapshot.IsDefault;

        unitOfWork.Accounts.Update(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeleteAccountMemoryAction(AccountMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Delete account";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => "Account deleted";
    public string Details => $"{snapshot.AccountType} · Balance {LogMemoryDisplay.Amount(snapshot.Balance)}";

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.Accounts.GetByIdAsync(snapshot.AccountId, cancellationToken) is not null)
            return;

        var account = new Account
        {
            Id = snapshot.AccountId,
            Name = snapshot.Name,
            AccountType = snapshot.AccountType,
            AccountLimit = snapshot.AccountLimit,
            MaximumSpending = snapshot.MaximumSpending,
            MinimumPayment = snapshot.MinimumPayment,
            SpentAmount = snapshot.SpentAmount,
            Balance = snapshot.Balance,
            MonthlyDueDate = snapshot.MonthlyDueDate,
            DeductSource = snapshot.DeductSource,
            InterestRate = snapshot.InterestRate,
            PinnedOnUI = snapshot.PinnedOnUI,
            IsEnabled = snapshot.IsEnabled,
            IsDefault = snapshot.IsDefault
        };

        await unitOfWork.Accounts.AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        var account =
            await unitOfWork.Accounts.GetByIdAsync(snapshot.AccountId, cancellationToken);
        if (account is null)
            return;

        unitOfWork.Accounts.Remove(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeleteTagMemoryAction(TagMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Delete tag";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => "Tag deleted";
    public string Details => snapshot.SpendingLimit.HasValue
        ? $"Limit {LogMemoryDisplay.Amount(snapshot.SpendingLimit.Value)}"
        : "No spending limit";

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.Tags.GetByIdAsync(snapshot.TagId, cancellationToken) is not null)
            return;

        var tag = new Tag
        {
            Id = snapshot.TagId,
            Name = snapshot.Name,
            HexCode = snapshot.HexCode,
            SpendingLimit = snapshot.SpendingLimit
        };

        await unitOfWork.Tags.AddAsync(tag, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        var tag = await unitOfWork.Tags.GetByIdAsync(snapshot.TagId, cancellationToken);
        if (tag is null)
            return;

        unitOfWork.Tags.Remove(tag);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class EditTagMemoryAction(
    TagMemorySnapshot before,
    TagMemorySnapshot after) : ILogMemoryAction
{
    public string Description => "Edit tag";
    public string Title => $"{after.Name} Updated";
    public string Summary => "Tag information updated";
    public string Details => LogMemoryDisplay.Changes(
        ("Name", before.Name, after.Name),
        ("Color", before.HexCode, after.HexCode),
        ("Spending limit", LogMemoryDisplay.OptionalAmount(before.SpendingLimit), LogMemoryDisplay.OptionalAmount(after.SpendingLimit)));

    public Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(unitOfWork, before, cancellationToken);
    }

    public Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(unitOfWork, after, cancellationToken);
    }

    private static async Task ApplySnapshotAsync(IUnitOfWork unitOfWork, TagMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var tag = await unitOfWork.Tags.GetByIdAsync(snapshot.TagId, cancellationToken);
        if (tag is null)
            return;

        tag.Name = snapshot.Name;
        tag.HexCode = snapshot.HexCode;
        tag.SpendingLimit = snapshot.SpendingLimit;
        unitOfWork.Tags.Update(tag);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeleteSavingGoalMemoryAction(SavingGoalMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Delete saving goal";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => "Saving goal deleted";
    public string Details => $"{LogMemoryDisplay.Amount(snapshot.CurrentAmount)} of {LogMemoryDisplay.Amount(snapshot.TargetAmount)}";

    public async Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.SavingGoals.GetByIdAsync(snapshot.SavingGoalId, cancellationToken) is not null)
            return;

        var savingGoal = new SavingGoal
        {
            Id = snapshot.SavingGoalId,
            Name = snapshot.Name,
            TargetAmount = snapshot.TargetAmount,
            CurrentAmount = snapshot.CurrentAmount,
            SavingEndDate = snapshot.SavingEndDate,
            CreatedOn = snapshot.CreatedOn
        };

        await unitOfWork.SavingGoals.AddAsync(savingGoal, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        var savingGoal = await unitOfWork.SavingGoals.GetByIdAsync(snapshot.SavingGoalId, cancellationToken);
        if (savingGoal is null)
            return;

        unitOfWork.SavingGoals.Remove(savingGoal);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SetUserSettingMemoryAction(
    UserSettingMemorySnapshot before,
    UserSettingMemorySnapshot after) : ILogMemoryAction
{
    public string Description => "Update setting";
    public string Title => $"{after.Name} Updated";
    public string Summary => "Setting updated";
    public string Details => string.Empty;

    public Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(unitOfWork, before, cancellationToken);
    }

    public Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(unitOfWork, after, cancellationToken);
    }

    private static async Task ApplySnapshotAsync(IUnitOfWork unitOfWork, UserSettingMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existingSetting = await unitOfWork.UserSettings.GetByNameAsync(snapshot.Name, cancellationToken);

        if (!snapshot.Exists)
        {
            if (existingSetting is null)
                return;

            unitOfWork.UserSettings.Remove(existingSetting);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        if (existingSetting is null)
        {
            await unitOfWork.UserSettings.AddAsync(new UserSettings
            {
                Name = snapshot.Name,
                Value = snapshot.Value
            }, cancellationToken);
        }
        else
        {
            existingSetting.Value = snapshot.Value;
            unitOfWork.UserSettings.Update(existingSetting);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static class LogMemoryDisplay
{
    internal static string Amount(decimal value) =>
        value.ToString("#,0.##", CultureInfo.CurrentCulture);

    internal static string OptionalAmount(decimal? value) =>
        value.HasValue ? Amount(value.Value) : "None";

    internal static string Date(DateTime value) =>
        value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

    internal static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "None" : value;

    internal static string YesNo(bool value) => value ? "Yes" : "No";

    internal static string TransactionNoun(TransactionType type) =>
        type == TransactionType.Expense ? "Expense" : "Income";

    internal static string Changes(params (string Label, string Before, string After)[] values) =>
        string.Join(" · ", values
            .Where(value => !string.Equals(value.Before, value.After, StringComparison.Ordinal))
            .Select(value => $"{value.Label}: {value.Before} → {value.After}"));
}

internal static class LogMemoryPersistence
{
    internal static async Task AdjustGoalAsync(
        IUnitOfWork unitOfWork,
        int? goalId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (goalId is not { } id)
            return;

        var goal = await unitOfWork.SavingGoals.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Saving goal {id} was not found.");
        goal.CurrentAmount += amount;
        unitOfWork.SavingGoals.Update(goal);
    }

    internal static void ApplyTransactionToAccount(Account account, TransactionType type, decimal amount)
    {
        if (type == TransactionType.Expense)
            ApplyExpenseToAccount(account, amount);
        else
            ApplyIncomeToAccount(account, amount);
    }

    internal static void RevertTransactionFromAccount(Account account, TransactionType type, decimal amount)
    {
        if (type == TransactionType.Expense)
            RevertExpenseFromAccount(account, amount);
        else
            RevertIncomeFromAccount(account, amount);
    }

    internal static void ApplyExpenseToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount += amount;
            return;
        }

        account.Balance -= amount;
    }

    internal static void RevertExpenseFromAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
            return;
        }

        account.Balance += amount;
    }

    internal static void ApplyIncomeToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
            return;
        }

        account.Balance += amount;
    }

    internal static void RevertIncomeFromAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount += amount;
            return;
        }

        account.Balance -= amount;
    }

    internal static async Task<Account> GetRequiredAccountAsync(IUnitOfWork unitOfWork,
        int accountId, CancellationToken cancellationToken)
    {
        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, cancellationToken);
        return account ??
               throw new InvalidOperationException($"Unable to find account {accountId}.");
    }

    internal static async Task<Tag> GetRequiredTagAsync(IUnitOfWork unitOfWork, int tagId,
        CancellationToken cancellationToken)
    {
        var tag = await unitOfWork.Tags.GetByIdAsync(tagId, cancellationToken);
        return tag ?? throw new InvalidOperationException($"Unable to find expense tag {tagId}.");
    }
}
