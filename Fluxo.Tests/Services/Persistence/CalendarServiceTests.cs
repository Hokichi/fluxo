using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Services.Persistence;
using Fluxo.Tests.TestDoubles;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Persistence;

public sealed class CalendarServiceTests
{
    [Fact]
    public async Task CalendarService_GetCalendarDayAsync_FiltersSelectedDateAndBuildsSummaries()
    {
        var (sut, unitOfWork) = CreateSut();
        var selected = new DateOnly(2026, 6, 12);

        unitOfWork.Transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new Transaction
            {
                Id = 1,
                Type = TransactionType.Expense,
                Amount = 74m,
                OccurredOn = new DateTime(2026, 6, 12, 9, 0, 0),
                IsForDeletion = false,
                Name = "Groceries",
                Tag = new Tag { Name = "Food", HexCode = "#00FF00" },
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 2,
                Type = TransactionType.Expense,
                Amount = 10m,
                OccurredOn = new DateTime(2026, 6, 12, 10, 0, 0),
                IsForDeletion = true,
                Name = "Deleted",
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 3,
                Type = TransactionType.Expense,
                Amount = 99m,
                OccurredOn = new DateTime(2026, 6, 11, 23, 59, 0),
                IsForDeletion = false,
                Name = "Different day",
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 10,
                Type = TransactionType.Expense,
                ParentTransactionId = 1,
                Amount = 25m,
                OccurredOn = new DateTime(2026, 6, 12, 11, 0, 0),
                IsForDeletion = false,
                Name = "Child split",
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 4,
                Type = TransactionType.Income,
                Name = "Freelance",
                Amount = 450m,
                OccurredOn = new DateTime(2026, 6, 12, 14, 0, 0),
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 5,
                Type = TransactionType.Income,
                Name = "Other",
                Amount = 200m,
                OccurredOn = new DateTime(2026, 6, 13, 1, 0, 0),
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 11,
                Type = TransactionType.Expense,
                Name = "Excluded expense",
                Amount = 30m,
                OccurredOn = new DateTime(2026, 6, 12, 15, 0, 0),
                IsExcludedFromBudget = true,
                Account = new Account { Name = "Checking" }
            },
            new Transaction
            {
                Id = 12,
                Type = TransactionType.Income,
                Name = "Excluded income",
                Amount = 100m,
                OccurredOn = new DateTime(2026, 6, 12, 16, 0, 0),
                IsExcludedFromBudget = true,
                Account = new Account { Name = "Checking" }
            }
        ]);

        unitOfWork.SavingGoals.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new SavingGoal { Id = 6, Name = "Vacation", CurrentAmount = 100m, TargetAmount = 500m, SavingEndDate = new DateTime(2026, 6, 12) },
            new SavingGoal { Id = 7, Name = "No date", CurrentAmount = 0m, TargetAmount = 10m, SavingEndDate = null }
        ]);

        unitOfWork.RecurringTransactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new RecurringTransaction
            {
                Id = 8,
                Name = "Rent",
                Amount = 1200m,
                IsEnabled = true,
                RecurringPeriod = RecurringPeriod.Monthly,
                RecurringTime = 12,
                Type = RecurringTransactionType.Expense,
                Source = new Account { Name = "Checking" }
            },
            new RecurringTransaction
            {
                Id = 9,
                Name = "Disabled",
                Amount = 1m,
                IsEnabled = false,
                RecurringPeriod = RecurringPeriod.Monthly,
                RecurringTime = 12,
                Type = RecurringTransactionType.Expense,
                Source = new Account { Name = "Checking" }
            }
        ]);

        var result = await sut.GetCalendarDayAsync(selected);

        Assert.Equal(selected, result.Date);
        Assert.Equal(74m, result.TotalSpent);
        Assert.Equal(450m, result.TotalEarned);
        Assert.Equal(2, result.Expenses.Count);
        Assert.Contains(result.Expenses, item => item.Name == "Groceries");
        Assert.Equal(2, result.Incomes.Count);
        Assert.Contains(result.Incomes, item => item.Name == "Freelance");
        Assert.Single(result.GoalDeadlines);
        Assert.Equal("Vacation", result.GoalDeadlines[0].Name);
        Assert.Single(result.RecurringTransactions);
        Assert.Equal("Rent", result.RecurringTransactions[0].Name);
        Assert.Equal(1, result.GoalsDue);
        Assert.Equal(1, result.PaymentsDue);
    }

    [Theory]
    [InlineData(RecurringPeriod.Weekly, 5, 2026, 6, 12, true)]
    [InlineData(RecurringPeriod.Biweekly, 5, 2026, 6, 12, true)]
    [InlineData(RecurringPeriod.Weekly, 4, 2026, 6, 12, false)]
    [InlineData(RecurringPeriod.Monthly, 12, 2026, 6, 12, true)]
    [InlineData(RecurringPeriod.Monthly, 11, 2026, 6, 12, false)]
    [InlineData(RecurringPeriod.None, 0, 2026, 6, 12, false)]
    public async Task CalendarService_GetCalendarDayAsync_FiltersRecurringTransactionsDueOnSelectedDate(
        RecurringPeriod period,
        int recurringTime,
        int year,
        int month,
        int day,
        bool expectedIncluded)
    {
        var (sut, unitOfWork) = CreateSut();
        unitOfWork.Transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.SavingGoals.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.RecurringTransactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new RecurringTransaction
            {
                Id = 10,
                Name = "Candidate",
                Amount = 10m,
                IsEnabled = true,
                RecurringPeriod = period,
                RecurringTime = recurringTime,
                Type = RecurringTransactionType.Expense,
                Source = new Account { Name = "Checking" }
            }
        ]);

        var result = await sut.GetCalendarDayAsync(new DateOnly(year, month, day));

        Assert.Equal(expectedIncluded, result.RecurringTransactions.Count == 1);
    }

    private static (CalendarService Sut, IUnitOfWork UnitOfWork) CreateSut()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Transactions.Returns(Substitute.For<ITransactionRepository>());
        unitOfWork.SavingGoals.Returns(Substitute.For<ISavingGoalRepository>());
        unitOfWork.RecurringTransactions.Returns(Substitute.For<IRecurringTransactionRepository>());

        return (new CalendarService(new InlineDataOperationRunner(unitOfWork)), unitOfWork);
    }
}
