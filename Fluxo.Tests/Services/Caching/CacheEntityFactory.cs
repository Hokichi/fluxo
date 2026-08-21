using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.Tests.Services.Caching;

internal static class CacheEntityFactory
{
    internal static Transaction CreateTransaction(
        int id,
        Account account,
        Tag? tag = null,
        bool isForDeletion = false,
        DateTime? occurredOn = null,
        DateTime? loggedOn = null)
    {
        return new Transaction
        {
            Id = id,
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            Account = account,
            Name = $"Transaction {id}",
            Amount = id,
            OccurredOn = occurredOn ?? new DateTime(2026, 8, 20),
            LoggedOn = loggedOn ?? new DateTime(2026, 8, 20, 12, 0, 0),
            Notes = string.Empty,
            TagId = tag?.Id,
            Tag = tag,
            IsForDeletion = isForDeletion
        };
    }
}
