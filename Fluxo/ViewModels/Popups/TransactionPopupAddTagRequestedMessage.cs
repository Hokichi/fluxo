using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class TransactionPopupAddTagRequestedMessage(int transactionId, TransactionPopupVM? requester = null)
    : ValueChangedMessage<int>(transactionId)
{
    public TransactionPopupVM? Requester { get; } = requester;
}
