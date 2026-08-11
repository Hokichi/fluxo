using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.Services.Transactions;

public static class RepaymentTransactionSupport
{
    public static Transaction? FindNewestIncome(
        Transaction expense,
        IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(transactions);

        if (expense.RepaymentAccountId is not { } creditAccountId)
            return null;

        var expectedName = $"Repayment from {expense.Account.Name}";
        return transactions
            .Where(candidate =>
                candidate.Type == TransactionType.Income &&
                !candidate.IsForDeletion &&
                candidate.SourceAccountId == creditAccountId &&
                candidate.RepaymentAccountId == creditAccountId &&
                candidate.Amount == expense.Amount &&
                candidate.OccurredOn.Date == expense.OccurredOn.Date &&
                string.Equals(candidate.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.LoggedOn)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefault();
    }

    public static RepaymentTransactionPair Create(
        Account source,
        Account target,
        decimal amount,
        DateTime occurredOn,
        Tag balanceUpdateTag,
        string? expenseName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(balanceUpdateTag);

        if (source.AccountType != AccountType.Checking)
            throw new ArgumentException("Repayment source must be a checking account.", nameof(source));
        if (target.AccountType != AccountType.Credit)
            throw new ArgumentException("Repayment target must be a credit account.", nameof(target));
        if (amount <= 0m || amount > target.SpentAmount)
            throw new ArgumentException(
                "Repayment amount must be positive and not exceed spent amount.",
                nameof(amount));

        var expense = new Transaction
        {
            Type = TransactionType.Expense,
            Name = string.IsNullOrWhiteSpace(expenseName)
                ? $"Repayment to {target.Name}"
                : expenseName.Trim(),
            Amount = amount,
            OccurredOn = occurredOn,
            Notes = string.Empty,
            ExpenseCategory = ExpenseCategory.Excluded,
            SourceAccountId = source.Id,
            RepaymentAccountId = target.Id,
            TagId = balanceUpdateTag.Id
        };
        var income = new Transaction
        {
            Type = TransactionType.Income,
            Name = $"Repayment from {source.Name}",
            Amount = amount,
            OccurredOn = occurredOn,
            Notes = string.Empty,
            ExpenseCategory = ExpenseCategory.Excluded,
            SourceAccountId = target.Id,
            RepaymentAccountId = target.Id,
            TagId = balanceUpdateTag.Id
        };

        source.Balance -= amount;
        target.SpentAmount -= amount;

        return new RepaymentTransactionPair(expense, income);
    }
}

public readonly record struct RepaymentTransactionPair(Transaction Expense, Transaction Income);
