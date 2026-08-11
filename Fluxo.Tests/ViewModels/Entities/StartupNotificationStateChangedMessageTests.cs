using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Xunit;

namespace Fluxo.Tests.ViewModels.Entities;

public sealed class StartupNotificationStateChangedMessageTests
{
    [Fact]
    public void StartupNotificationStateChangedMessage_Account_UpdatesOverdueState_FromNotificationEvaluation()
    {
        var account = new AccountVM { Id = 1 };

        WeakReferenceMessenger.Default.Send(Evaluation(accounts: new HashSet<int> { 1 }));

        Assert.True(account.IsOverdue);
        WeakReferenceMessenger.Default.UnregisterAll(account);
    }

    [Fact]
    public void StartupNotificationStateChangedMessage_RecurringTransaction_UpdatesOverdueState_FromNotificationEvaluation()
    {
        var recurring = new RecurringTransactionVM { Id = 2 };

        WeakReferenceMessenger.Default.Send(Evaluation(recurring: new HashSet<int> { 2 }));

        Assert.True(recurring.IsOverdue);
        WeakReferenceMessenger.Default.UnregisterAll(recurring);
    }

    [Fact]
    public void StartupNotificationStateChangedMessage_SavingGoal_UpdatesOverdueState_FromNotificationEvaluation()
    {
        var goal = new SavingGoalVM { Id = 3 };

        WeakReferenceMessenger.Default.Send(Evaluation(goals: new HashSet<int> { 3 }));

        Assert.True(goal.IsOverdue);
        WeakReferenceMessenger.Default.UnregisterAll(goal);
    }

    private static StartupNotificationStateChangedMessage Evaluation(
        IReadOnlySet<int>? accounts = null,
        IReadOnlySet<int>? recurring = null,
        IReadOnlySet<int>? goals = null) => new(new StartupNotificationEvaluation(
        accounts ?? new HashSet<int>(),
        recurring ?? new HashSet<int>(),
        goals ?? new HashSet<int>(),
        []));
}
