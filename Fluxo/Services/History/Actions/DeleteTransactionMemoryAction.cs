using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class DeleteTransactionMemoryAction(TransactionMemorySnapshot snapshot) : ILogMemoryAction
{
    public TransactionMemorySnapshot Snapshot => snapshot;
    public string Description => "Delete transaction";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => $"{LogMemoryDisplay.TransactionNoun(snapshot.Type)} deleted";
    public string Details => $"{LogMemoryDisplay.Amount(snapshot.Amount)} · {LogMemoryDisplay.Date(snapshot.OccurredOn)}";

    public async Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        if (await appData.GetTransactionByIdAsync(snapshot.TransactionId, cancellationToken) is not null)
            return;

        var account = await LogMemoryPersistence.GetRequiredAccountAsync(
            appData, snapshot.SourceAccountId, cancellationToken);
        await appData.AddTransactionAsync(AddTransactionMemoryAction.CreateTransaction(snapshot, account), cancellationToken);
        if (snapshot.AffectsAccountBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(account, snapshot.Type, snapshot.Amount);
            appData.UpdateAccount(account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            appData, snapshot.GoalId, snapshot.Amount, cancellationToken);
    }

    public async Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        var transaction = await appData.GetTransactionByIdAsync(snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            return;

        if (transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.RevertTransactionFromAccount(transaction.Account, transaction.Type, transaction.Amount);
            appData.UpdateAccount(transaction.Account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            appData, snapshot.GoalId, -snapshot.Amount, cancellationToken);
        appData.RemoveTransaction(transaction);
    }
}
