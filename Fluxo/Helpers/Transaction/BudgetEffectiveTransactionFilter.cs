using Fluxo.Core.Entities;
using Fluxo.ViewModels.Entities;
using TransactionEntity = Fluxo.Core.Entities.Transaction;

namespace Fluxo.Helpers.Transaction;

internal static class BudgetEffectiveTransactionFilter
{
    internal static IEnumerable<TransactionVM> Select(IEnumerable<TransactionVM> transactions)
    {
        var included = transactions
            .Where(transaction => !transaction.IsForDeletion && !transaction.IsExcludedFromBudget)
            .ToList();
        var parentIds = included
            .Select(transaction => transaction.ParentTransactionId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        return included.Where(transaction => !parentIds.Contains(transaction.Id));
    }

    internal static IEnumerable<TransactionEntity> Select(IEnumerable<TransactionEntity> transactions)
    {
        var included = transactions
            .Where(transaction => !transaction.IsForDeletion && !transaction.IsExcludedFromBudget)
            .ToList();
        var parentIds = included
            .Select(transaction => transaction.ParentTransactionId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        return included.Where(transaction => !parentIds.Contains(transaction.Id));
    }
}
