using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class TransactionSplitCloneRequestedMessage(TransactionPopupDraft draft)
    : ValueChangedMessage<TransactionPopupDraft>(draft);
