using Fluxo.Core.Entities;

namespace Fluxo.Services.Caching;

internal sealed class AppDataSnapshotMaterializer(AppDataSnapshot snapshot)
{
    internal Transaction? TransactionById(int id)
    {
        if (!snapshot.Transactions.TryGetValue(id, out var value))
            return null;

        var account = AppDataSnapshot.CloneAccount(snapshot.Accounts[value.SourceAccountId]);
        var tag = value.TagId is { } tagId && snapshot.Tags.TryGetValue(tagId, out var storedTag)
            ? AppDataSnapshot.CloneTag(storedTag)
            : null;

        return new Transaction
        {
            Id = value.Id,
            Type = value.Type,
            SourceAccountId = value.SourceAccountId,
            Account = account,
            GoalId = value.GoalId,
            RepaymentAccountId = value.RepaymentAccountId,
            RelatedRecurringTransactionId = value.RelatedRecurringTransactionId,
            Name = value.Name,
            Amount = value.Amount,
            OccurredOn = value.OccurredOn,
            LoggedOn = value.LoggedOn,
            Notes = value.Notes,
            ExpenseCategory = value.ExpenseCategory,
            TagId = value.TagId,
            Tag = tag,
            ParentTransactionId = value.ParentTransactionId,
            IsPinned = value.IsPinned,
            IsForDeletion = value.IsForDeletion,
            IsIoU = value.IsIoU,
            ShouldAffectBalance = value.ShouldAffectBalance
        };
    }
}
