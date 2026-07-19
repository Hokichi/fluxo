using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class RecurringDraftSaveRequestedMessage(TransactionPopupVM.RecurringDraftSaveInput input)
    : AsyncRequestMessage<TransactionPopupVM.TransactionPopupSubmissionResult>
{
    public TransactionPopupVM.RecurringDraftSaveInput Input { get; } = input;
}
