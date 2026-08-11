using Fluxo.Core.Enums;

namespace Fluxo.Core.DTO;

public sealed class RecurringTransactionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public RecurringPeriod RecurringPeriod { get; set; }
    public int RecurringTime { get; set; }
    public RecurringTransactionType Type { get; set; }
    public ExpenseCategory? Category { get; set; }
    public int SourceId { get; set; }
    public int? TagId { get; set; }
    public int? GoalId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? EndDate { get; set; }
    public AccountDto Source { get; set; } = new();
    public TagDto? Tag { get; set; }
    public SavingGoalDto? Goal { get; set; }
}
