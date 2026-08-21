using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class EditTransactionMemoryAction(
    TransactionMemorySnapshot before,
    TransactionMemorySnapshot after) : ILogMemoryAction
{
    public TransactionMemorySnapshot Before => before;
    public TransactionMemorySnapshot After => after;
    public string Description => "Edit transaction";
    public string Title => $"{after.Name} Updated";
    public string Summary => $"{LogMemoryDisplay.TransactionNoun(after.Type)} information updated";
    public string Details => LogMemoryDisplay.Changes(
        ("Name", before.Name, after.Name),
        ("Type", before.Type.ToString(), after.Type.ToString()),
        ("Amount", LogMemoryDisplay.Amount(before.Amount), LogMemoryDisplay.Amount(after.Amount)),
        ("Date", LogMemoryDisplay.Date(before.OccurredOn), LogMemoryDisplay.Date(after.OccurredOn)),
        ("Category", before.ExpenseCategory?.ToString() ?? "None", after.ExpenseCategory?.ToString() ?? "None"),
        ("Notes", LogMemoryDisplay.Text(before.Notes), LogMemoryDisplay.Text(after.Notes)),
        ("Pinned", LogMemoryDisplay.YesNo(before.IsPinned), LogMemoryDisplay.YesNo(after.IsPinned)));
    public Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default) =>
        ApplyAsync(appData, before, cancellationToken);
    public Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default) =>
        ApplyAsync(appData, after, cancellationToken);

    private static async Task ApplyAsync(IAppDataService appData, TransactionMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var transaction = await appData.GetTransactionByIdAsync(snapshot.TransactionId, cancellationToken);
        if (transaction is null)
            return;

        var oldAccount = transaction.Account;
        var newAccount = await LogMemoryPersistence.GetRequiredAccountAsync(
            appData, snapshot.SourceAccountId, cancellationToken);
        var oldAffectsBalance = transaction.AffectsAccountBalance;
        if (oldAffectsBalance)
            LogMemoryPersistence.RevertTransactionFromAccount(oldAccount, transaction.Type, transaction.Amount);
        if (snapshot.AffectsAccountBalance)
            LogMemoryPersistence.ApplyTransactionToAccount(newAccount, snapshot.Type, snapshot.Amount);

        transaction.Type = snapshot.Type;
        transaction.SourceAccountId = snapshot.SourceAccountId;
        transaction.Account = newAccount;
        transaction.Name = snapshot.Name;
        transaction.Amount = snapshot.Amount;
        transaction.OccurredOn = snapshot.OccurredOn;
        transaction.Notes = snapshot.Notes;
        transaction.ExpenseCategory = snapshot.ExpenseCategory;
        transaction.TagId = snapshot.TagId;
        transaction.GoalId = snapshot.GoalId;
        transaction.RepaymentAccountId = snapshot.RepaymentAccountId;
        transaction.ParentTransactionId = snapshot.ParentTransactionId;
        transaction.IsPinned = snapshot.IsPinned;
        transaction.IsForDeletion = snapshot.IsForDeletion;
        transaction.IsIoU = snapshot.IsIoU;
        transaction.ShouldAffectBalance = snapshot.ShouldAffectBalance;

        appData.UpdateTransaction(transaction);
        if (oldAffectsBalance)
            appData.UpdateAccount(oldAccount);
        if (snapshot.AffectsAccountBalance && (!ReferenceEquals(oldAccount, newAccount) || !oldAffectsBalance))
            appData.UpdateAccount(newAccount);
    }
}
