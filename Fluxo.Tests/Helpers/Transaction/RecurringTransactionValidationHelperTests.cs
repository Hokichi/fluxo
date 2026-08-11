using Fluxo.Core.Enums;
using Fluxo.Helpers.Transaction;
using Xunit;

namespace Fluxo.Tests.Helpers.Transaction;

public sealed class RecurringTransactionValidationHelperTests
{
    [Theory]
    [InlineData(RecurringPeriod.Weekly, "8", "Recurring weekday must be between Monday and Sunday.")]
    [InlineData(RecurringPeriod.Monthly, "29", "Recurring day must be between 1 and 28.")]
    public void RecurringTransactionValidationHelper_Invalid_RecurringTimeReturnsExistingMessage(
        RecurringPeriod period,
        string value,
        string expectedMessage)
    {
        var result = RecurringTransactionValidationHelper.ValidateTime(period, value);

        Assert.False(result.IsValid);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }

    [Fact]
    public void RecurringTransactionValidationHelper_Installments_RequireAtLeastOneOccurrence()
    {
        var result = RecurringTransactionValidationHelper.ValidateInstallments(
            RecurringPeriod.Weekly, "1", new DateTime(2026, 7, 18), new DateTime(2026, 7, 19));

        Assert.False(result.IsValid);
        Assert.Equal("Installment end date must be today or later.", result.ErrorMessage);
    }
}
