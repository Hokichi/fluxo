using CommunityToolkit.Mvvm.Messaging.Messages;
using Fluxo.DataModels.Popups.TransactionPopup;
using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionSplitValidationRequestedMessage(TransactionVM root)
    : RequestMessage<TransactionPopupSubmissionResult>
{
    public TransactionVM Root { get; } = root;
}
