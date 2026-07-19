using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class TransactionPopupAddTagRequestedMessage(int transactionId) : ValueChangedMessage<int>(transactionId);
