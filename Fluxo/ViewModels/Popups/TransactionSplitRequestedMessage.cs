using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class TransactionSplitRequestedMessage(int transactionId) : ValueChangedMessage<int>(transactionId);
