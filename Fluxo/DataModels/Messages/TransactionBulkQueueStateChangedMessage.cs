using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionBulkQueueStateChangedMessage(
    IReadOnlyList<TransactionVM> transactions,
    TransactionVM? selectedTransaction,
    bool hasChanges,
    bool allValid)
{
    public IReadOnlyList<TransactionVM> Transactions { get; } = transactions;
    public TransactionVM? SelectedTransaction { get; } = selectedTransaction;
    public bool HasChanges { get; } = hasChanges;
    public bool AllValid { get; } = allValid;
}
