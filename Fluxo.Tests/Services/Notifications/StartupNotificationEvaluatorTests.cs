using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Services.Notifications;
using Fluxo.Tests.TestDoubles;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Notifications;

public sealed class StartupNotificationEvaluatorTests
{
    [Fact]
    public async Task StartupNotificationEvaluator_EvaluateAsync_BuildsBudgetCreditAndBalanceCards_WhenTheirThresholdsAreReached()
    {
        var result = await CreateEvaluator(
            accounts:
            [
                new() { Id = 1, Name = "Checking", AccountType = AccountType.Checking, IsEnabled = true },
                new() { Id = 2, Name = "Visa", AccountType = AccountType.Credit, AccountLimit = 100m, SpentAmount = 30m, IsEnabled = true },
                new() { Id = 3, Name = "Cash", AccountType = AccountType.Cash, Balance = 10m, IsEnabled = true }
            ],
            transactions:
            [
                Expense(1, 45m, ExpenseCategory.Needs),
                Expense(3, 90m, ExpenseCategory.Wants)
            ],
            settings:
            [
                Setting(UserSettingNames.IsBudgetThresholdNotifEnabled, true),
                Setting(UserSettingNames.IsLowCreditNotifEnabled, true),
                Setting(UserSettingNames.IsLowAccountBalanceNotifEnabled, true)
            ],
            allocation: new BudgetAllocation { AllocationLimit = 100m }).EvaluateAsync();

        Assert.Contains(result.Notifications, card => card.Type == "BudgetThresholdNeeds");
        Assert.Contains(result.Notifications, card => card.Type == "LowCredit-2");
        Assert.Contains(result.Notifications, card => card.Type == "LowBalance-3");
    }

    [Fact]
    public async Task StartupNotificationEvaluator_EvaluateAsync_BuildsDailyAllowanceCard_AtConfiguredWarningPercentage()
    {
        var result = await CreateEvaluator(
            accounts: [new() { Id = 1, Name = "Checking", AccountType = AccountType.Checking, IsEnabled = true }],
            transactions: [Expense(1, 95m, ExpenseCategory.Needs)],
            settings:
            [
                Setting(UserSettingNames.IsDailyAllowanceNotifEnabled, true),
                new UserSettings { Name = UserSettingNames.BudgetUsageWarningPercentage, Value = "0.90" }
            ],
            allocation: new BudgetAllocation { AllocationLimit = 3_100m }).EvaluateAsync();

        Assert.Contains(result.Notifications, card => card.Type == "DailyAllowance");
    }

    [Fact]
    public async Task StartupNotificationEvaluator_EvaluateAsync_SkipsCards_WhileNotificationsAreSnoozed()
    {
        var now = DateTime.Now;
        var result = await CreateEvaluator(
            accounts:
            [
                new()
                {
                    Id = 1, Name = "Visa", AccountType = AccountType.Credit, MonthlyDueDate = 1,
                    SpentAmount = 100m, IsEnabled = true
                }
            ],
            settings:
            [
                new UserSettings
                {
                    Name = UserSettingNames.NotificationsSnoozeEndDate,
                    Value = now.AddHours(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                }
            ],
            now: now).EvaluateAsync();

        Assert.Empty(result.Notifications);
    }

    [Fact]
    public async Task StartupNotificationEvaluator_EvaluateAsync_ClearsCreditOverdue_WhenRepaymentExistsAfterDueDate()
    {
        var result = await CreateEvaluator(
            accounts:
            [
                new()
                {
                    Id = 1, Name = "Visa", AccountType = AccountType.Credit, MonthlyDueDate = 5,
                    SpentAmount = 100m, IsEnabled = true
                }
            ],
            transactions:
            [
                new()
                {
                    Type = TransactionType.Income, RepaymentAccountId = 1, Amount = 100m,
                    OccurredOn = new DateTime(2026, 7, 6)
                }
            ]).EvaluateAsync();

        Assert.DoesNotContain(1, result.OverdueAccountIds);
    }

    [Fact]
    public async Task StartupNotificationEvaluator_EvaluateAsync_FlagsRecurringTransaction_WhenOnlyOldOccurrenceIsLinked()
    {
        var result = await CreateEvaluator(
            transactions:
            [
                new()
                {
                    Type = TransactionType.Expense, RelatedRecurringTransactionId = 7,
                    OccurredOn = new DateTime(2026, 6, 1)
                }
            ],
            recurring:
            [
                new()
                {
                    Id = 7, Name = "Rent", IsEnabled = true, RecurringPeriod = RecurringPeriod.Weekly,
                    RecurringTime = 5
                }
            ]).EvaluateAsync();

        Assert.Contains(7, result.OverdueRecurringTransactionIds);
    }

    [Fact]
    public async Task StartupNotificationEvaluator_EvaluateAsync_DoesNotFlagGoalWithoutEndDate()
    {
        var result = await CreateEvaluator(
            goals: [new() { Id = 3, Name = "Emergency", CurrentAmount = 20m, TargetAmount = 100m }])
            .EvaluateAsync();

        Assert.DoesNotContain(3, result.OverdueSavingGoalIds);
    }

    private static StartupNotificationEvaluator CreateEvaluator(
        IReadOnlyList<Account>? accounts = null,
        IReadOnlyList<Transaction>? transactions = null,
        IReadOnlyList<RecurringTransaction>? recurring = null,
        IReadOnlyList<SavingGoal>? goals = null,
        IReadOnlyList<UserSettings>? settings = null,
        BudgetAllocation? allocation = null,
        DateTime? now = null)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Accounts.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(accounts ?? (IReadOnlyList<Account>)[]));
        unitOfWork.Transactions.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(transactions ?? (IReadOnlyList<Transaction>)[]));
        unitOfWork.RecurringTransactions.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(recurring ?? (IReadOnlyList<RecurringTransaction>)[]));
        unitOfWork.SavingGoals.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(goals ?? (IReadOnlyList<SavingGoal>)[]));
        unitOfWork.UserSettings.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(settings ?? (IReadOnlyList<UserSettings>)[]));
        unitOfWork.BudgetAllocation.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(allocation));

        return new StartupNotificationEvaluator(new InlineDataOperationRunner(unitOfWork), () => now ?? new DateTime(2026, 7, 10));
    }

    private static Transaction Expense(int accountId, decimal amount, ExpenseCategory category) => new()
    {
        Type = TransactionType.Expense,
        SourceAccountId = accountId,
        Amount = amount,
        ExpenseCategory = category,
        OccurredOn = new DateTime(2026, 7, 10)
    };

    private static UserSettings Setting(string name, bool value) => new() { Name = name, Value = value.ToString() };
}
