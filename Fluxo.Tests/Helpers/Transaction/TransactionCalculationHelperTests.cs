using Fluxo.Core.Enums;
using Fluxo.Helpers.Transaction;
using Fluxo.ViewModels.Entities;
using Xunit;

namespace Fluxo.Tests.Helpers.Transaction;

public sealed class TransactionCalculationHelperTests
{
    [Fact]
    public void Installment_amount_rounds_away_from_zero()
    {
        Assert.Equal(3.34m, TransactionCalculationHelper.CalculateInstallmentAmount(10.005m, 3));
    }

    [Theory]
    [InlineData(AccountType.Checking, 100, 0, false, 25, 75)]
    [InlineData(AccountType.Checking, 100, 0, true, 25, 125)]
    [InlineData(AccountType.Credit, 0, 40, false, 25, 65)]
    [InlineData(AccountType.Credit, 0, 40, true, 25, 15)]
    public void Account_projection_respects_account_and_transaction_type(
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
