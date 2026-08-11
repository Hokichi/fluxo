using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups.Planning;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Planning;

public class PlanningReportVMTests
{
    [Fact]
    public async Task PlanningReportVM_LoadAsync_UsesBudgetAllocationPercentages()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>())
            .Returns(new BudgetAllocation
            {
                NeedsThreshold = 40,
                WantsThreshold = 35,
                InvestThreshold = 25
            });
        var report = new PlanningReportVM(appData);

        await report.LoadAsync();

        Assert.Equal(40, report.NeedsPercent);
        Assert.Equal(35, report.WantsPercent);
        Assert.Equal(25, report.InvestPercent);
    }

    [Theory]
    [InlineData("Needs", 55, 55, 28, 17)]
    [InlineData("Wants", 21, 55, 21, 24)]
    public async Task PlanningReportVM_ChangingAllocation_RebalancesOtherBucketsByPriority(
        string bucket,
        int newValue,
        int expectedNeeds,
        int expectedWants,
        int expectedInvest)
    {
        var report = await CreateLoadedReportAsync(50, 30, 20);

        SetBucket(report, bucket, newValue);

        Assert.Equal(expectedNeeds, report.NeedsPercent);
        Assert.Equal(expectedWants, report.WantsPercent);
        Assert.Equal(expectedInvest, report.InvestPercent);
        Assert.Equal(100, report.NeedsPercent + report.WantsPercent + report.InvestPercent);
    }

    [Fact]
    public async Task PlanningReportVM_ChangingAllocationSequentially_PreservesPrioritySplitAcrossSliderTicks()
    {
        var report = await CreateLoadedReportAsync(50, 30, 20);

        for (var value = 51; value <= 55; value++)
            report.NeedsPercent = value;

        Assert.Equal(55, report.NeedsPercent);
        Assert.Equal(28, report.WantsPercent);
        Assert.Equal(17, report.InvestPercent);
        Assert.Equal(100, report.NeedsPercent + report.WantsPercent + report.InvestPercent);
    }

    [Fact]
    public async Task PlanningReportVM_DecreasingAllocationSequentially_PreservesPrioritySplitAcrossSliderTicks()
    {
        var report = await CreateLoadedReportAsync(50, 30, 20);

        for (var value = 29; value >= 21; value--)
            report.WantsPercent = value;

        Assert.Equal(55, report.NeedsPercent);
        Assert.Equal(21, report.WantsPercent);
        Assert.Equal(24, report.InvestPercent);
        Assert.Equal(100, report.NeedsPercent + report.WantsPercent + report.InvestPercent);
    }

    [Fact]
    public async Task PlanningReportVM_ChangingAllocation_SpillsRemainderWhenAnotherBucketReachesZero()
    {
        var report = await CreateLoadedReportAsync(98, 1, 1);

        report.NeedsPercent = 100;

        Assert.Equal(100, report.NeedsPercent);
        Assert.Equal(0, report.WantsPercent);
        Assert.Equal(0, report.InvestPercent);
        Assert.Equal(100, report.NeedsPercent + report.WantsPercent + report.InvestPercent);
    }

    [Fact]
    public async Task PlanningReportVM_InvalidAllocationMessage_NamesZeroBuckets()
    {
        var report = await CreateLoadedReportAsync(98, 1, 1);

        report.NeedsPercent = 100;

        Assert.True(report.IsAllocationInvalid);
        Assert.Equal("Invalid allocation.\nWants and Invest cannot be 0%", report.InvalidAllocationMessage);
    }

    [Fact]
    public async Task PlanningReportVM_ComputeAllocationUsage_PerCategory_ReturnsOverflow()
    {
        var report = await CreateLoadedReportAsync(50, 30, 20);
        report.AddIncome(CreateIncome(1, 1000m));
        report.AddExpense(CreateExpense(1, "Rent", ExpenseCategory.Needs, 600m));
        report.AddExpense(CreateExpense(2, "Coffee", ExpenseCategory.Wants, 250m));
        report.AddExpense(CreateExpense(3, "ETF", ExpenseCategory.Savings, 300m));

        Assert.Equal(1d, report.NeedsUsage, 5);
        Assert.Equal(0.2d, report.NeedsOverflow, 5);
        Assert.Equal(120, report.NeedsUsagePercent);
        Assert.Equal(0.83333d, report.WantsUsage, 5);
        Assert.Equal(0d, report.WantsOverflow, 5);
        Assert.Equal(83, report.WantsUsagePercent);
        Assert.Equal(1d, report.InvestUsage, 5);
        Assert.Equal(0.5d, report.InvestOverflow, 5);
        Assert.Equal(150, report.InvestUsagePercent);

        report.Expenses[1].Amount = 360m;

        Assert.Equal(1d, report.WantsUsage, 5);
        Assert.Equal(0.2d, report.WantsOverflow, 5);
        Assert.Equal(120, report.WantsUsagePercent);

        report.Expenses[0].ExpenseCategory = ExpenseCategory.Wants;

        Assert.Equal(0d, report.NeedsUsage, 5);
        Assert.Equal(0d, report.NeedsOverflow, 5);
        Assert.Equal(0, report.NeedsUsagePercent);
        Assert.Equal(1d, report.WantsUsage, 5);
        Assert.Equal(2.2d, report.WantsOverflow, 5);
        Assert.Equal(320, report.WantsUsagePercent);
    }

    [Fact]
    public async Task PlanningReportVM_LoadRecurringIncomesAsync_AddsOnlyEnabledRecurringIncomes()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetRecurringTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns([
                CreateRecurring(1, "Salary", 2500m, RecurringTransactionType.Income, isEnabled: true),
                CreateRecurring(2, "Disabled income", 75m, RecurringTransactionType.Income, isEnabled: false),
                CreateRecurring(3, "Rent", 900m, RecurringTransactionType.Expense, isEnabled: true)
            ]);
        var report = new PlanningReportVM(appData);

        await report.LoadRecurringIncomesAsync();

        var income = Assert.Single(report.Incomes);
        Assert.Equal("Salary", income.Name);
        Assert.Equal(2500m, income.Amount);
        Assert.Equal("Account 1", income.Account.Name);
        Assert.Empty(report.Expenses);
    }

    [Fact]
    public async Task PlanningReportVM_LoadRecurringExpensesAsync_AddsOnlyEnabledRecurringExpenses()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetRecurringTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns([
                CreateRecurring(1, "Salary", 2500m, RecurringTransactionType.Income, isEnabled: true),
                CreateRecurring(2, "Disabled rent", 900m, RecurringTransactionType.Expense, isEnabled: false),
                CreateRecurring(3, "Groceries", 300m, RecurringTransactionType.Expense, isEnabled: true, ExpenseCategory.Wants)
            ]);
        var report = new PlanningReportVM(appData);

        await report.LoadRecurringExpensesAsync();

        var expense = Assert.Single(report.Expenses);
        Assert.Equal("Groceries", expense.Name);
        Assert.Equal(300m, expense.Amount);
        Assert.Equal(ExpenseCategory.Wants, expense.ExpenseCategory);
        Assert.Equal("Tag 3", expense.Tag.Name);
        Assert.Equal("Account 3", expense.Account.Name);
        Assert.Empty(report.Incomes);
    }

    private static async Task<PlanningReportVM> CreateLoadedReportAsync(int needs, int wants, int invest)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>())
            .Returns(new BudgetAllocation
            {
                NeedsThreshold = needs,
                WantsThreshold = wants,
                InvestThreshold = invest
            });
        var report = new PlanningReportVM(appData);
        await report.LoadAsync();
        return report;
    }

    private static void SetBucket(PlanningReportVM report, string bucket, int value)
    {
        switch (bucket)
        {
            case "Needs":
                report.NeedsPercent = value;
                break;

            case "Wants":
                report.WantsPercent = value;
                break;

            case "Invest":
                report.InvestPercent = value;
                break;
        }
    }

    private static TransactionVM CreateIncome(int id, decimal amount)
    {
        return new TransactionVM
        {
            Id = id,
            Amount = amount,
            OccurredOn = DateTime.UnixEpoch.AddDays(id),
            Notes = $"Income {id}",
            Account = new AccountVM
            {
                Id = id,
                Name = $"Account {id}",
                AccountType = AccountType.Checking,
                Balance = 500m,
                IsEnabled = true,
                PinnedOnUI = true
            }
        };
    }

    private static TransactionVM CreateExpense(
        int id,
        string name,
        ExpenseCategory category = ExpenseCategory.Needs,
        decimal? amount = null)
    {
        return new TransactionVM
        {
            Id = id,
            Name = name,
            Amount = amount ?? id * 100m,
            ExpenseCategory = category,
            Tag = new TagVM
            {
                Id = id,
                Name = $"Tag {id}",
                HexCode = "#000000",
                IsSystemTag = false
            },
            Account = new AccountVM
            {
                Id = id,
                Name = $"Expense Source {id}",
                AccountType = AccountType.Checking,
                Balance = 1000m,
                IsEnabled = true,
                PinnedOnUI = true
            }
        };
    }

    private static RecurringTransaction CreateRecurring(
        int id,
        string name,
        decimal amount,
        RecurringTransactionType type,
        bool isEnabled,
        ExpenseCategory? category = null)
    {
        return new RecurringTransaction
        {
            Id = id,
            Name = name,
            Amount = amount,
            Type = type,
            Category = category,
            SourceId = id,
            TagId = type == RecurringTransactionType.Expense ? id : null,
            IsEnabled = isEnabled,
            Source = new Account
            {
                Id = id,
                Name = $"Account {id}",
                AccountType = AccountType.Checking,
                Balance = 1000m,
                IsEnabled = true
            },
            Tag = type == RecurringTransactionType.Expense
                ? new Tag
                {
                    Id = id,
                    Name = $"Tag {id}",
                    HexCode = "#000000"
                }
                : null
        };
    }
}
