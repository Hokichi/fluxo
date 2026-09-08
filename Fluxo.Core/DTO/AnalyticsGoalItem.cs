namespace Fluxo.Core.DTO;

public sealed record AnalyticsGoalItem(
    int GoalId,
    string Name,
    decimal CurrentAmount,
    decimal TargetAmount,
    DateTime CreatedOn,
    DateTime? SavingEndDate);
