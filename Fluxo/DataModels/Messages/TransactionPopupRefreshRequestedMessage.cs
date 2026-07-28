using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionPopupRefreshRequestedMessage(int transactionId) : AsyncRequestMessage<bool>
{
    public int TransactionId { get; } = transactionId;
}
