namespace Fluxo.Core.DTO;

public sealed record CalendarGoalDeadlineItem(
    int Id,
    string Name,
    decimal CurrentAmount,
    decimal TargetAmount,
    DateTime SavingEndDate);
