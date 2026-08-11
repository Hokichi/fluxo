using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Constants;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;
using System.Globalization;

namespace Fluxo.Services.Notifications;

public sealed record StartupNotificationEvaluation(
    IReadOnlySet<int> OverdueAccountIds,
    IReadOnlySet<int> OverdueRecurringTransactionIds,
    IReadOnlySet<int> OverdueSavingGoalIds,
    IReadOnlyList<NotificationVM> Notifications);

public sealed class StartupNotificationEvaluator(
    IDataOperationRunner dataOperationRunner,
    Func<DateTime>? clock = null)
{
    public Task<StartupNotificationEvaluation> EvaluateAsync(CancellationToken cancellationToken = default) =>
        EvaluateAsync(null, null, cancellationToken);

    public Task<StartupNotificationEvaluation> EvaluateEntityAsync(
        NotificationEntityKind kind,
        int entityId,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(kind, entityId, cancellationToken);

    private Task<StartupNotificationEvaluation> EvaluateAsync(
        NotificationEntityKind? requestedKind,
        int? requestedId,
        CancellationToken cancellationToken) =>
        dataOperationRunner.RunAsync(async (scope, ct) =>
        {
            var unitOfWork = scope.UnitOfWork;
            var now = clock?.Invoke() ?? DateTime.Now;
            var today = now.Date;
            var transactions = (await unitOfWork.Transactions.GetAllAsync(ct))
                .Where(transaction => !transaction.IsForDeletion).ToList();
            var accounts = (await unitOfWork.Accounts.GetAllAsync(ct))
                .Where(account => !account.IsForDeletion && account.IsEnabled).ToList();
            var recurring = (await unitOfWork.RecurringTransactions.GetAllAsync(ct))
                .Where(item => item.IsEnabled && (item.EndDate is null || item.EndDate.Value.Date >= today)).ToList();
            var goals = await unitOfWork.SavingGoals.GetAllAsync(ct);
            var settings = (await unitOfWork.UserSettings.GetAllAsync(ct)).ToDictionary(setting => setting.Name, setting => setting.Value);
            var allocation = await unitOfWork.BudgetAllocation.GetAsync(ct);
            if (IsSnoozed(UserSettingNames.NotificationsSnoozeEndDate, now))
                return new StartupNotificationEvaluation(new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), []);

            var latePaymentEnabled = Enabled(UserSettingNames.IsLatePaymentNotifEnabled, true);
            var budgetThresholdEnabled = Enabled(UserSettingNames.IsBudgetThresholdNotifEnabled, true);
            var lowCreditEnabled = Enabled(UserSettingNames.IsLowCreditNotifEnabled, false);
            var lowBalanceEnabled = Enabled(UserSettingNames.IsLowAccountBalanceNotifEnabled, lowCreditEnabled);
            var recurringEnabled = Enabled(UserSettingNames.IsRecurringOverdueNotifEnabled, true);
            var goalsEnabled = Enabled(UserSettingNames.IsGoalOverdueNotifEnabled, true);
            var dailyAllowanceEnabled = Enabled(UserSettingNames.IsDailyAllowanceNotifEnabled, true);
            var budgetUsageWarningPercentage = Decimal(UserSettingNames.BudgetUsageWarningPercentage, 0.90m);
            var creditUsageWarningPercentage = Decimal(UserSettingNames.CreditUsageWarningPercentage, 0.30m);
            var lowAccountBalancePercentage = Decimal(UserSettingNames.LowAccountBalancePercentage, 0.20m);

            var overdueAccounts = accounts.Where(account => IsCreditOverdue(account, transactions, today))
                .Select(account => account.Id).ToHashSet();
            var overdueRecurring = recurring.Where(item => IsRecurringOverdue(item, transactions, today))
                .Select(item => item.Id).ToHashSet();
            var overdueGoals = goals.Where(goal => IsGoalOverdue(goal, today))
                .Select(goal => goal.Id).ToHashSet();

            var notifications = new List<NotificationVM>();
            if (latePaymentEnabled && requestedKind is not NotificationEntityKind.RecurringTransaction)
                notifications.AddRange(accounts.Where(account => overdueAccounts.Contains(account.Id)).Select(account => Card(
                    $"LatePayment-{account.Id}", $"Late Payment - {account.Name}",
                    $"{account.Name} payment is overdue.", NotificationSeverity.Danger)));
            if (recurringEnabled && requestedKind is not NotificationEntityKind.Account and not NotificationEntityKind.SavingGoal)
                notifications.AddRange(recurring.Where(item => overdueRecurring.Contains(item.Id)).Select(item => Card(
                    $"RecurringTransactionOverdue-{item.Id}", $"Recurring Transaction Overdue - {item.Name}",
                    $"{item.Name} needs completion.", NotificationSeverity.Warning)));
            if (goalsEnabled && requestedKind is not NotificationEntityKind.Account and not NotificationEntityKind.RecurringTransaction)
                notifications.AddRange(goals.Where(goal => overdueGoals.Contains(goal.Id)).Select(goal => Card(
                    $"GoalOverdue-{goal.Id}", $"Goal Overdue - {goal.Name}",
                    $"{goal.Name} is past its target date.", NotificationSeverity.Danger)));
            var budgetTransactions = SelectBudgetTransactions(transactions).ToList();
            if (budgetThresholdEnabled && allocation is not null)
                AddBudgetThresholdNotifications(notifications, allocation, budgetTransactions, accounts, today, budgetUsageWarningPercentage);

            if (lowCreditEnabled)
                AddLowCreditNotifications(notifications, accounts, creditUsageWarningPercentage);

            if (lowBalanceEnabled)
                AddLowBalanceNotifications(notifications, accounts, transactions, lowAccountBalancePercentage);

            var dailyAllowance = allocation is null ? 0m : BudgetAllocationCalculator.CalculateDailyAllowance(allocation, today);
            var todaySpending = budgetTransactions.Where(transaction => transaction.Type == TransactionType.Expense && transaction.OccurredOn.Date == today).Sum(transaction => transaction.Amount);
            if (dailyAllowanceEnabled && dailyAllowance > 0m && todaySpending >= dailyAllowance * budgetUsageWarningPercentage)
                notifications.Add(Card("DailyAllowance", "Daily Allowance Reached", "Today's spending reached the daily allowance.", NotificationSeverity.Warning));

            if (requestedKind is not null && requestedId is not null)
                notifications = notifications.Where(card => card.Type.EndsWith($"-{requestedId}", StringComparison.Ordinal)).ToList();

            return new StartupNotificationEvaluation(overdueAccounts, overdueRecurring, overdueGoals, notifications);

            bool Enabled(string name, bool fallback) => !settings.TryGetValue(name, out var value) || !bool.TryParse(value, out var enabled) ? fallback : enabled;
            decimal Decimal(string name, decimal fallback) => !settings.TryGetValue(name, out var value) || !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? fallback : parsed;
            bool IsSnoozed(string name, DateTime current) => settings.TryGetValue(name, out var value) &&
                DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var endDate) && endDate > current;
        }, cancellationToken);

    private static void AddBudgetThresholdNotifications(
        List<NotificationVM> notifications,
        BudgetAllocation allocation,
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Account> accounts,
        DateTime today,
        decimal warningPercentage)
    {
        var currentPeriod = BudgetAllocationCalculator.ResolveCurrentPeriod(allocation.AllocationPeriod, today, allocation.PeriodStart);
        var previousPeriod = BudgetAllocationCalculator.ResolvePreviousPeriod(allocation.AllocationPeriod, today, allocation.PeriodStart);
        var snapshot = BudgetAllocationCalculator.CalculateSnapshot(
            allocation,
            SpentByCategory(transactions, currentPeriod.Start, currentPeriod.End),
            SpentByCategory(transactions, previousPeriod.Start, previousPeriod.End),
            today,
            accounts.Sum(account => account.Balance));

        AddBudgetThresholdNotification("Needs", ExpenseCategory.Needs, snapshot.Needs, NotificationSeverity.Danger);
        AddBudgetThresholdNotification("Wants", ExpenseCategory.Wants, snapshot.Wants, NotificationSeverity.Warning);
        AddBudgetThresholdNotification("Savings", ExpenseCategory.Savings, snapshot.Invest, NotificationSeverity.Warning);

        void AddBudgetThresholdNotification(
            string label,
            ExpenseCategory category,
            BudgetAllocationCategoryState state,
            NotificationSeverity severity)
        {
            if (state.Available <= 0m || state.Spent / state.Available < warningPercentage)
                return;

            notifications.Add(Card(
                $"BudgetThreshold{label}",
                $"Budget Threshold - {label}",
                $"{label} has reached {state.Percentage}% of its allocation.",
                severity));
        }
    }

    private static void AddLowCreditNotifications(
        List<NotificationVM> notifications,
        IReadOnlyList<Account> accounts,
        decimal warningPercentage)
    {
        foreach (var account in accounts.Where(account => account.AccountType == AccountType.Credit && account.AccountLimit > 0m))
        {
            var usage = account.SpentAmount / account.AccountLimit;
            if (usage < warningPercentage)
                continue;

            notifications.Add(Card(
                $"LowCredit-{account.Id}",
                $"Low Credit - {account.Name}",
                $"{account.Name} is using {(int)Math.Round(usage * 100m, MidpointRounding.AwayFromZero)}% of its limit.",
                NotificationSeverity.Warning));
        }
    }

    private static void AddLowBalanceNotifications(
        List<NotificationVM> notifications,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<Transaction> transactions,
        decimal warningPercentage)
    {
        foreach (var account in accounts.Where(account => account.AccountType is AccountType.Cash or AccountType.Checking))
        {
            var totalBeforeSpending = account.Balance + transactions
                .Where(transaction => transaction.Type == TransactionType.Expense && transaction.SourceAccountId == account.Id)
                .Sum(transaction => transaction.Amount);
            if (totalBeforeSpending <= 0m)
                continue;

            var remaining = account.Balance / totalBeforeSpending;
            if (remaining >= warningPercentage)
                continue;

            notifications.Add(Card(
                $"LowBalance-{account.Id}",
                $"Low Balance - {account.Name}",
                $"{account.Name} is down to {(int)Math.Round(remaining * 100m, MidpointRounding.AwayFromZero)}% of its pre-spend total.",
                NotificationSeverity.Danger));
        }
    }

    private static IEnumerable<Transaction> SelectBudgetTransactions(IEnumerable<Transaction> transactions)
    {
        var included = transactions
            .Where(transaction => !transaction.IsForDeletion &&
                                  transaction.ExpenseCategory != ExpenseCategory.Excluded)
            .ToList();
        var parentIds = included.Where(transaction => transaction.ParentTransactionId.HasValue)
            .Select(transaction => transaction.ParentTransactionId!.Value).ToHashSet();
        return included.Where(transaction => !parentIds.Contains(transaction.Id));
    }

    private static IReadOnlyDictionary<ExpenseCategory, decimal> SpentByCategory(
        IEnumerable<Transaction> transactions,
        DateTime from,
        DateTime to) => transactions
        .Where(transaction => transaction.Type == TransactionType.Expense && transaction.OccurredOn.Date >= from && transaction.OccurredOn.Date <= to)
        .Where(transaction => transaction.ExpenseCategory.HasValue)
        .GroupBy(transaction => transaction.ExpenseCategory!.Value)
        .ToDictionary(group => group.Key, group => group.Sum(transaction => transaction.Amount));

    private static bool IsCreditOverdue(Account account, IReadOnlyList<Transaction> transactions, DateTime today)
    {
        if (account.AccountType != AccountType.Credit || account.MonthlyDueDate is not { } dueDay || account.SpentAmount <= 0)
            return false;
        var dueDate = new DateTime(today.Year, today.Month, Math.Min(dueDay, DateTime.DaysInMonth(today.Year, today.Month)));
        if (today <= dueDate)
            return false;
        return !transactions.Any(transaction => transaction.RepaymentAccountId == account.Id && transaction.OccurredOn.Date >= dueDate);
    }

    private static bool IsRecurringOverdue(RecurringTransaction recurring, IReadOnlyList<Transaction> transactions, DateTime today)
    {
        var dueDate = ResolveCurrentOccurrence(recurring, today);
        if (dueDate is null || dueDate > today)
            return false;
        var nextDueDate = ResolveNextOccurrence(recurring, dueDate.Value);
        return !transactions.Any(transaction => transaction.RelatedRecurringTransactionId == recurring.Id &&
            transaction.OccurredOn.Date >= dueDate && transaction.OccurredOn.Date < nextDueDate);
    }

    private static bool IsGoalOverdue(SavingGoal goal, DateTime today) =>
        goal.SavingEndDate is { } endDate && endDate.Date < today && goal.TargetAmount > goal.CurrentAmount;

    private static DateTime? ResolveCurrentOccurrence(RecurringTransaction item, DateTime today) => item.RecurringPeriod switch
    {
        RecurringPeriod.Weekly or RecurringPeriod.Biweekly when item.RecurringTime is >= 1 and <= 7 =>
            today.AddDays(-(((int)today.DayOfWeek + 6) % 7 - (item.RecurringTime - 1) + 7) % 7),
        RecurringPeriod.Monthly when item.RecurringTime > 0 => new DateTime(today.Year, today.Month,
            Math.Min(item.RecurringTime, DateTime.DaysInMonth(today.Year, today.Month))),
        _ => null
    };

    private static DateTime ResolveNextOccurrence(RecurringTransaction item, DateTime occurrence) => item.RecurringPeriod switch
    {
        RecurringPeriod.Biweekly => occurrence.AddDays(14),
        RecurringPeriod.Weekly => occurrence.AddDays(7),
        RecurringPeriod.Monthly => occurrence.AddMonths(1),
        _ => occurrence.AddDays(1)
    };

    private static NotificationVM Card(string type, string header, string message, NotificationSeverity severity) => new()
    {
        Type = type, Header = header, Message = message, Severity = severity, CreatedOn = DateTime.Now
    };
}
