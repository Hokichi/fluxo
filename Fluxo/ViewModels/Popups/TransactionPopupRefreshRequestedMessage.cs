using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class TransactionPopupRefreshRequestedMessage(int transactionId) : AsyncRequestMessage<bool>
{
    public int TransactionId { get; } = transactionId;
}
