using Fluxo.Core.Enums;

namespace Fluxo.Core.Entities;

public sealed class RecurringTransaction
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public RecurringPeriod RecurringPeriod { get; set; } = RecurringPeriod.Monthly;
    public int RecurringTime { get; set; }
    public RecurringTransactionType Type { get; set; }
    public ExpenseCategory? Category { get; set; }
    public int SourceId { get; set; }
    public int? TagId { get; set; }
    public int? GoalId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? EndDate { get; set; }
    public Account Source { get; set; } = null!;
    public Tag? Tag { get; set; }
    public SavingGoal? Goal { get; set; }
}
