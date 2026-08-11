using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Popups.Settings;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Settings;

public sealed class SettingsSignedAmountTests
{
    [Theory]
    [InlineData(RecurringTransactionType.Income, "+")]
    [InlineData(RecurringTransactionType.Expense, "-")]
    public void SettingsSignedAmount_RecurringTransaction_AmountSignMatchesType(RecurringTransactionType type, string expected)
    {
        var item = new SettingsRecurringTransactionItemVM(new RecurringTransaction
        {
            Type = type,
            Amount = 25m,
            Name = "Recurring"
        });

        Assert.Equal(expected, item.AmountSign);
    }

    [Theory]
    [InlineData(IoUKind.Debt, "+")]
    [InlineData(IoUKind.Lend, "-")]
    public void SettingsSignedAmount_DebtIoU_AmountSignMatchesKind(IoUKind kind, string expected)
    {
        var item = new IoUItemVM { Kind = kind, Amount = 25m };

        Assert.Equal(expected, item.AmountSign);
    }
}
