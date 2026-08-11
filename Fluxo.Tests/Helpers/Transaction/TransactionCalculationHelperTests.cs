using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Helpers.Transaction;
using Fluxo.ViewModels.Entities;
using Xunit;
using TransactionEntity = Fluxo.Core.Entities.Transaction;

namespace Fluxo.Tests.Helpers.Transaction;

public sealed class TransactionCalculationHelperTests
{
    [Fact]
    public void TransactionCalculationHelper_CalculateSpentByCategory_ExcludesExcludedCategory()
    {
        var today = DateTime.Today;
        var logs = new[]
        {
            new TransactionEntity { Amount = 10m, ExpenseCategory = ExpenseCategory.Needs, OccurredOn = today },
            new TransactionEntity { Amount = 20m, ExpenseCategory = ExpenseCategory.Excluded, OccurredOn = today }
        };

        var result = TransactionCalculationHelper.CalculateSpentByCategory(
            logs,
            new BudgetAllocationPeriod(today, today));

        Assert.Equal(10m, result[ExpenseCategory.Needs]);
        Assert.DoesNotContain(ExpenseCategory.Excluded, result.Keys);
    }

    [Fact]
    public void TransactionCalculationHelper_Installment_AmountRoundsAwayFrom_Zero()
    {
        Assert.Equal(3.34m, TransactionCalculationHelper.CalculateInstallmentAmount(10.005m, 3));
    }

    [Theory]
    [InlineData(AccountType.Checking, 100, 0, false, 25, 75)]
    [InlineData(AccountType.Checking, 100, 0, true, 25, 125)]
    [InlineData(AccountType.Credit, 0, 40, false, 25, 65)]
    [InlineData(AccountType.Credit, 0, 40, true, 25, 15)]
    public void TransactionCalculationHelper_Account_ProjectionRespectsAccountAndTransactionType(
        AccountType accountType,
        decimal balance,
        decimal spent,
        bool isIncome,
        decimal amount,
        decimal expected)
    {
        var account = new AccountVM { AccountType = accountType, Balance = balance, SpentAmount = spent };

        Assert.Equal(expected, TransactionCalculationHelper.CalculateAccountToBe(account, isIncome, amount));
    }
}
