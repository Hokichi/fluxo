using Fluxo.Core.Enums;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class BudgetForecastVMTests
{
    [Fact]
    public void BudgetForecastVM_CountOccurrences_Monthly_CountsEveryDueDateThroughTarget()
    {
        var count = BudgetForecastVM.CountRecurringOccurrences(
            RecurringPeriod.Monthly,
            recurringTime: 1,
            today: new DateTime(2026, 6, 22),
            targetDate: new DateTime(2026, 8, 10));

        Assert.Equal(2, count);
    }

    [Fact]
    public void BudgetForecastVM_CountOccurrences_Weekly_CountsMatchingWeekdaysThroughTarget()
    {
        var count = BudgetForecastVM.CountRecurringOccurrences(
            RecurringPeriod.Weekly,
            recurringTime: 5,
            today: new DateTime(2026, 6, 22),
            targetDate: new DateTime(2026, 7, 3));

        Assert.Equal(2, count);
    }

    [Fact]
    public void BudgetForecastVM_CountAllocationPeriods_RoundsUp()
    {
        Assert.Equal(2, BudgetForecastVM.CountAllocationPeriods(dayCount: 12, allocationPeriodDays: 7));
    }

    [Fact]
    public void BudgetForecastVM_CalculateCategoryBudget_MultipliesBaseAllocationByRoundedPeriodCount()
    {
        Assert.Equal(2_000m, BudgetForecastVM.CalculateCategoryBudget(1_000m, dayCount: 12, allocationPeriodDays: 7));
    }

    [Fact]
    public void BudgetForecastVM_GetAvailableBalance_ForCredit_ReturnsLimitMinusSpent()
    {
        var account = new Account
        {
            AccountType = AccountType.Credit,
            AccountLimit = 10_000m,
            SpentAmount = 3_250m,
            Balance = 0m
        };

        Assert.Equal(6_750m, BudgetForecastVM.GetAvailableBalance(account));
    }

    [Fact]
    public void BudgetForecastVM_PurchaseCategoryRadioButtons_SelectCategory()
    {
        var vm = new BudgetForecastVM(Substitute.For<IAppDataService>());

        vm.IsPurchaseWantsCategory = true;
        Assert.Equal(ExpenseCategory.Wants, vm.SelectedPurchaseCategory);

        vm.IsPurchaseInvestCategory = true;
        Assert.Equal(ExpenseCategory.Savings, vm.SelectedPurchaseCategory);
        Assert.False(vm.IsPurchaseWantsCategory);
        Assert.True(vm.IsPurchaseInvestCategory);
    }

    [Fact]
    public void BudgetForecastVM_TestTransactionVisibility_DefaultsToHidden()
    {
        var vm = new BudgetForecastVM(Substitute.For<IAppDataService>());

        Assert.False(vm.IsTestTransactionVisible);
    }

    [Fact]
    public void BudgetForecastVM_ForecastBalance_ReportsDirectionAgainstCurrentBalance()
    {
        var account = new BudgetForecastAccountRowVM(1, "Cash", 100m);

        account.Balance = 99m;
        Assert.True(account.IsBalanceDecreased);
        Assert.False(account.IsBalanceIncreased);

        account.Balance = 101m;
        Assert.False(account.IsBalanceDecreased);
        Assert.True(account.IsBalanceIncreased);

        account.Balance = 100m;
        Assert.False(account.IsBalanceDecreased);
        Assert.False(account.IsBalanceIncreased);
    }

    [Theory]
    [InlineData(100, 500, "Affordable")]
    [InlineData(600, 1000, "Not recommended")]
    [InlineData(1200, 1000, "Not affordable")]
    public void BudgetForecastVM_BuildPurchaseResult_ReturnsExpectedSeverity(decimal purchase, decimal accountRemaining, string expectedStart)
    {
        var result = BudgetForecastVM.BuildPurchaseResult(
            purchaseAmount: purchase,
            accountBalance: accountRemaining,
            categoryRemaining: 500m,
            categoryName: "Needs",
            accountName: "Cash");

        Assert.StartsWith(expectedStart, result.Message, StringComparison.Ordinal);
    }
}
