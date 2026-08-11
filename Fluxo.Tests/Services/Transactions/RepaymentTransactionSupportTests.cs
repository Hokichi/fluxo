using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Services.Transactions;
using Xunit;

namespace Fluxo.Tests.Services.Transactions;

public sealed class RepaymentTransactionSupportTests
{
    [Fact]
    public void RepaymentTransactionSupport_FindNewestIncome_SelectsNewestMatchingCreditIncome()
    {
        var checking = new Account { Id = 1, Name = "Checking" };
        var expense = new Transaction
        {
            Type = TransactionType.Expense,
            Account = checking,
            SourceAccountId = checking.Id,
            RepaymentAccountId = 2,
            Amount = 75m,
            OccurredOn = new DateTime(2026, 7, 2)
        };
        var older = Income(10, 2, 75m, new DateTime(2026, 7, 2), new DateTime(2026, 7, 2, 9, 0, 0));
        var newer = Income(11, 2, 75m, new DateTime(2026, 7, 2), new DateTime(2026, 7, 2, 10, 0, 0));
        var wrongAmount = Income(12, 2, 50m, new DateTime(2026, 7, 2), new DateTime(2026, 7, 2, 11, 0, 0));

        var match = RepaymentTransactionSupport.FindNewestIncome(
            expense,
            [older, wrongAmount, newer]);
        Assert.Same(newer, match);
    }

    [Fact]
    public void RepaymentTransactionSupport_Create_ProducesExcludedBalanceUpdatePair_AndMutatesAccounts()
    {
        var checking = new Account
        {
            Id = 1,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 500m
        };
        var credit = new Account
        {
            Id = 2,
            Name = "Visa",
            AccountType = AccountType.Credit,
            SpentAmount = 200m
        };
        var tag = new Tag { Id = 3, Name = "Balance Update", HexCode = "#fff" };

        var pair = RepaymentTransactionSupport.Create(
            checking,
            credit,
            75m,
            new DateTime(2026, 6, 28),
            tag);

        Assert.Equal(425m, checking.Balance);
        Assert.Equal(125m, credit.SpentAmount);
        Assert.Equal(TransactionType.Expense, pair.Expense.Type);
        Assert.Equal("Repayment to Visa", pair.Expense.Name);
        Assert.Equal(ExpenseCategory.Excluded, pair.Expense.ExpenseCategory);
        Assert.Equal(3, pair.Expense.TagId);
        Assert.Equal(2, pair.Expense.RepaymentAccountId);
        Assert.Equal(TransactionType.Income, pair.Income.Type);
        Assert.Equal("Repayment from Checking", pair.Income.Name);
        Assert.Equal(3, pair.Income.TagId);
        Assert.Equal(2, pair.Income.SourceAccountId);
        Assert.Equal(2, pair.Income.RepaymentAccountId);
        Assert.Equal(new DateTime(2026, 6, 28), pair.Income.OccurredOn);
        Assert.Equal(ExpenseCategory.Excluded, pair.Income.ExpenseCategory);
    }

    [Theory]
    [InlineData(AccountType.Cash, AccountType.Credit, 10)]
    [InlineData(AccountType.Checking, AccountType.Checking, 10)]
    [InlineData(AccountType.Checking, AccountType.Credit, 0)]
    [InlineData(AccountType.Checking, AccountType.Credit, 101)]
    public void RepaymentTransactionSupport_Create_RejectsInvalidRepayment(
        AccountType sourceType,
        AccountType targetType,
        decimal amount)
    {
        var source = new Account { AccountType = sourceType };
        var target = new Account { AccountType = targetType, SpentAmount = 100m };
        var tag = new Tag { Id = 1, Name = "Balance Update", HexCode = "#fff" };

        Assert.Throws<ArgumentException>(() =>
            RepaymentTransactionSupport.Create(source, target, amount, DateTime.Today, tag));
    }

    private static Transaction Income(int id, int accountId, decimal amount, DateTime date, DateTime loggedOn) => new()
    {
        Id = id,
        Type = TransactionType.Income,
        Name = "Repayment from Checking",
        SourceAccountId = accountId,
        RepaymentAccountId = accountId,
        Amount = amount,
        OccurredOn = date,
        LoggedOn = loggedOn
    };
}
