using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed record TransactionMemorySnapshot(
    int TransactionId,
    TransactionType Type,
    int SourceAccountId,
    string Name,
    decimal Amount,
    DateTime OccurredOn,
    string Notes,
    ExpenseCategory? ExpenseCategory,
    int? TagId,
    int? GoalId,
    int? RepaymentAccountId,
    int? ParentTransactionId,
    bool IsPinned,
    bool IsForDeletion,
    bool IsIoU,
    DateTime LoggedOn = default,
    bool ShouldAffectBalance = false)
{
    public bool AffectsAccountBalance =>
        Transaction.ShouldAffectAccountBalance(IsIoU, ShouldAffectBalance);

    public static TransactionMemorySnapshot Create(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return new TransactionMemorySnapshot(
            transaction.Id,
            transaction.Type,
            transaction.SourceAccountId,
            transaction.Name,
            transaction.Amount,
            transaction.OccurredOn,
            transaction.Notes,
            transaction.ExpenseCategory,
            transaction.TagId,
            transaction.GoalId,
            transaction.RepaymentAccountId,
            transaction.ParentTransactionId,
            transaction.IsPinned,
            transaction.IsForDeletion,
            transaction.IsIoU,
            transaction.LoggedOn,
            transaction.ShouldAffectBalance);
    }
}
