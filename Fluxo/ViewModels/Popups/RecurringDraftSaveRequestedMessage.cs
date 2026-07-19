using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.ViewModels.Popups;

public sealed class RecurringDraftSaveRequestedMessage(RecurringDraftSaveInput input)
    : AsyncRequestMessage<TransactionPopupSubmissionResult>
{
    public RecurringDraftSaveInput Input { get; } = input;
}
