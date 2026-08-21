using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class AddTransactionMemoryAction(
    TransactionMemorySnapshot snapshot,
    bool shouldAdjustAccountTotals = true) : ILogMemoryAction
{
    public TransactionMemorySnapshot Snapshot => snapshot;
    public string Description => snapshot.Type == TransactionType.Expense ? "Add expense" : "Add income";
    public string Title => $"{snapshot.Name} Added";
    public string Summary => $"{LogMemoryDisplay.TransactionNoun(snapshot.Type)} added";
    public string Details => $"{LogMemoryDisplay.Amount(snapshot.Amount)} · {LogMemoryDisplay.Date(snapshot.OccurredOn)}";

    public async Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        var transaction = await appData.GetTransactionByIdAsync(snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            return;

        if (shouldAdjustAccountTotals && transaction.AffectsAccountBalance)
        {
            LogMemoryPersistence.RevertTransactionFromAccount(transaction.Account, transaction.Type, transaction.Amount);
            appData.UpdateAccount(transaction.Account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            appData, snapshot.GoalId, -snapshot.Amount, cancellationToken);
        appData.RemoveTransaction(transaction);
    }

    public async Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        if (await appData.GetTransactionByIdAsync(snapshot.TransactionId, cancellationToken) is not null)
            return;

        var account = await LogMemoryPersistence.GetRequiredAccountAsync(
            appData, snapshot.SourceAccountId, cancellationToken);
        var transaction = CreateTransaction(snapshot, account);
        await appData.AddTransactionAsync(transaction, cancellationToken);
        if (shouldAdjustAccountTotals && snapshot.AffectsAccountBalance)
        {
            LogMemoryPersistence.ApplyTransactionToAccount(account, snapshot.Type, snapshot.Amount);
            appData.UpdateAccount(account);
        }
        await LogMemoryPersistence.AdjustGoalAsync(
            appData, snapshot.GoalId, snapshot.Amount, cancellationToken);
    }

    internal static Transaction CreateTransaction(TransactionMemorySnapshot value, Account account) => new()
    {
        Id = value.TransactionId,
        Type = value.Type,
        SourceAccountId = value.SourceAccountId,
        Account = account,
        Name = value.Name,
        Amount = value.Amount,
        OccurredOn = value.OccurredOn,
        Notes = value.Notes,
        ExpenseCategory = value.ExpenseCategory,
        TagId = value.TagId,
        GoalId = value.GoalId,
        RepaymentAccountId = value.RepaymentAccountId,
        ParentTransactionId = value.ParentTransactionId,
        IsPinned = value.IsPinned,
        IsForDeletion = value.IsForDeletion,
        IsIoU = value.IsIoU,
        ShouldAffectBalance = value.ShouldAffectBalance
    };
}
