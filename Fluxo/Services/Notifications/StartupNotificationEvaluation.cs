using Fluxo.ViewModels.Entities;

namespace Fluxo.Services.Notifications;

public sealed record StartupNotificationEvaluation(
    IReadOnlySet<int> OverdueAccountIds,
    IReadOnlySet<int> OverdueRecurringTransactionIds,
    IReadOnlySet<int> OverdueSavingGoalIds,
    IReadOnlyList<NotificationVM> Notifications);
