using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Persistence;

public sealed class TransactionService(IAppDataService appData, IDataOperationRunner runner)
    : ITransactionService
{
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var transaction = await appData.GetTransactionByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (transaction is null)
            return;

        transaction.IsForDeletion = true;
        appData.UpdateTransaction(transaction);
        await appData.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task PostTerminationCleanupAsync(CancellationToken cancellationToken = default) =>
        runner.RunAsync("cleanup terminated transactions", async (scope, ct) =>
        {
            var transactions = await scope.UnitOfWork.Transactions.GetMarkedForDeletionAsync(ct);
            foreach (var transaction in transactions)
                scope.UnitOfWork.Transactions.Remove(transaction);
            if (transactions.Count > 0)
                await scope.UnitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
}
