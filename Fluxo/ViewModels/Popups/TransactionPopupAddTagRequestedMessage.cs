using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class TransactionPopupAddTagRequestedMessage(int transactionId, Guid ownerToken)
    : ValueChangedMessage<int>(transactionId)
{
    public Guid OwnerToken { get; } = ownerToken;
}
