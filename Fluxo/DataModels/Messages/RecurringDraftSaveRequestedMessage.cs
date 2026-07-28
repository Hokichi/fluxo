using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.DataModels.Messages;

public sealed class RecurringDraftSaveRequestedMessage(RecurringDraftSaveInput input)
    : AsyncRequestMessage<TransactionPopupSubmissionResult>
{
    public RecurringDraftSaveInput Input { get; } = input;
}
