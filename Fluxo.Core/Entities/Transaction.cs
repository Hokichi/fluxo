using Fluxo.Core.Enums;

namespace Fluxo.Core.Entities;

public sealed class Transaction
{
    public int Id { get; set; }
    public TransactionType Type { get; set; }
    public int SourceAccountId { get; set; }
    public Account Account { get; set; } = null!;
    public int? GoalId { get; set; }
    public SavingGoal? Goal { get; set; }
    public int? RepaymentAccountId { get; set; }
    public Account? RepaymentAccount { get; set; }
    public int? RelatedRecurringTransactionId { get; set; }
    public RecurringTransaction? RelatedRecurringTransaction { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime OccurredOn { get; set; }
    public DateTime LoggedOn { get; set; }
    public string Notes { get; set; } = string.Empty;
    public ExpenseCategory? ExpenseCategory { get; set; }
    public int? TagId { get; set; }
    public Tag? Tag { get; set; }
    public int? ParentTransactionId { get; set; }
    public Transaction? ParentTransaction { get; set; }
    public bool IsPinned { get; set; }
    public bool IsForDeletion { get; set; }
    public bool IsIoU { get; set; }
    public bool ShouldAffectBalance { get; set; }
    public bool AffectsAccountBalance => ShouldAffectAccountBalance(IsIoU, ShouldAffectBalance);

    public static bool ShouldAffectAccountBalance(bool isIoU, bool shouldAffectBalance) =>
        !isIoU || shouldAffectBalance;
}
