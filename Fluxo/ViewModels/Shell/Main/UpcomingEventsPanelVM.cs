using System.Collections.ObjectModel;
using System.Globalization;
using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.Helpers.Popups;

using Fluxo.DataModels.Shell.Main.UpcomingEventsPanel;
namespace Fluxo.ViewModels.Shell.Main;

public partial class UpcomingEventsPanelVM : ObservableRecipient, IRecipient<DashboardDataInvalidatedMessage>
{
    private const int UpcomingWindowDays = 14;
    private readonly IAppDataService _appData;
    private readonly IMapper _mapper;
    private readonly Func<DateTime> _todayProvider;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    public UpcomingEventsPanelVM(
        IAppDataService appData,
        IMapper mapper,
        Func<DateTime>? todayProvider = null,
        IMessenger? messenger = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _appData = appData;
        _mapper = mapper;
        _todayProvider = todayProvider ?? (() => DateTime.Today);
        IsActive = true;
    }

    [ObservableProperty]
    private bool _hasEvents;

    public ObservableCollection<UpcomingEventItemVM> Events { get; } = [];

    public void Receive(DashboardDataInvalidatedMessage message)
    {
        if (!message.Value.HasFlag(DashboardDataInvalidationScope.All) &&
            !message.Value.HasFlag(DashboardDataInvalidationScope.Notifications) &&
            !message.Value.HasFlag(DashboardDataInvalidationScope.SavingGoals))
        {
            return;
        }

        _ = ReloadFromServicesAsync();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var recurring = await _appData.GetRecurringTransactionsAsync(cancellationToken);
        var goals = await _appData.GetSavingGoalsAsync(cancellationToken);
        var accounts = await _appData.GetAccountsAsync(cancellationToken);
        var snapshot = new UpcomingEventsSnapshot(recurring, goals, accounts);

        var recurringTransactions = _mapper.Map<IReadOnlyList<RecurringTransactionVM>>(
            snapshot.RecurringTransactions);
        var today = DateOnly.FromDateTime(_todayProvider().Date);
        var endDate = today.AddDays(UpcomingWindowDays);

        var events = BuildRecurringEvents(recurringTransactions, today, endDate)
            .Concat(BuildGoalDeadlineEvents(snapshot.SavingGoals, today, endDate))
            .Concat(BuildCreditPaymentEvents(snapshot.Accounts, today, endDate))
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Events.Clear();
        foreach (var item in events)
            Events.Add(item);

        HasEvents = Events.Count > 0;
    }

    private async Task ReloadFromServicesAsync()
    {
        await _reloadGate.WaitAsync();

        try
        {
            await LoadAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static IEnumerable<UpcomingEventItemVM> BuildRecurringEvents(
        IEnumerable<RecurringTransactionVM> recurringTransactions,
        DateOnly today,
        DateOnly endDate)
    {
        foreach (var transaction in recurringTransactions.Where(transaction => transaction.IsEnabled))
        {
            var dueDate = NotificationPanelVM.ResolveRecurringTransactionDueDate(
                transaction,
                today.ToDateTime(TimeOnly.MinValue));
            if (!dueDate.HasValue)
                continue;

            var eventDate = DateOnly.FromDateTime(dueDate.Value.Date);
            if (eventDate < today || eventDate > endDate)
                continue;

            yield return new UpcomingEventItemVM(
                eventDate,
                transaction.Name,
                FormatAmount(transaction.Amount),
                ResolveRecurringEventTypeText(transaction));
        }
    }

    private static IEnumerable<UpcomingEventItemVM> BuildGoalDeadlineEvents(
        IEnumerable<SavingGoal> savingGoals,
        DateOnly today,
        DateOnly endDate)
    {
        foreach (var goal in savingGoals.Where(goal =>
                     goal.SavingEndDate.HasValue &&
                     goal.TargetAmount > 0m &&
                     goal.CurrentAmount < goal.TargetAmount))
        {
            var deadline = DateOnly.FromDateTime(goal.SavingEndDate!.Value.Date);
            if (deadline < today || deadline > endDate)
                continue;

            var amountLeft = Math.Max(goal.TargetAmount - goal.CurrentAmount, 0m);
            yield return new UpcomingEventItemVM(
                deadline,
                $"{goal.Name} deadline",
                $"{FormatAmount(amountLeft)} left from {FormatAmount(goal.TargetAmount)}",
                "Goal Deadline");
        }
    }

    private static IEnumerable<UpcomingEventItemVM> BuildCreditPaymentEvents(
        IEnumerable<Account> accounts,
        DateOnly today,
        DateOnly endDate)
    {
        foreach (var account in accounts.Where(account =>
                     account.AccountType == AccountType.Credit &&
                     account.IsEnabled &&
                     account.SpentAmount > 0m))
        {
            var dueDate = MonthlyDueDateHelper.ResolveUpcomingDate(
                account.MonthlyDueDate,
                today.ToDateTime(TimeOnly.MinValue));
            if (!dueDate.HasValue)
                continue;

            var eventDate = DateOnly.FromDateTime(dueDate.Value);
            if (eventDate > endDate)
                continue;

            yield return new UpcomingEventItemVM(
                eventDate,
                account.Name,
                FormatAmount(account.SpentAmount),
                "Payment");
        }
    }

    private static string ResolveRecurringEventTypeText(RecurringTransactionVM transaction)
    {
        return transaction.Type switch
        {
            RecurringTransactionType.Income => "Income",
            RecurringTransactionType.GoalUpdate => "Goal",
            RecurringTransactionType.Expense when transaction.Source.AccountType is
                AccountType.Credit => "Payment",
            RecurringTransactionType.Expense => "Expense",
            _ => "Event"
        };
    }

    private static string FormatAmount(decimal amount)
    {
        return amount.ToString("N0", CultureInfo.InvariantCulture);
    }

}
