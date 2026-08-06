using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionBulkQueueResetMessage(
    bool isEnabled,
    IReadOnlyList<TransactionVM> transactions,
    AccountVM? defaultAccount)
{
    public bool IsEnabled { get; } = isEnabled;
    public IReadOnlyList<TransactionVM> Transactions { get; } = transactions;
    public AccountVM? DefaultAccount { get; } = defaultAccount;
}
