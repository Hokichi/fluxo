using CommunityToolkit.Mvvm.Messaging.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionSplitEqualOverrideRequestedMessage(TransactionVM parent)
    : RequestMessage<bool>
{
    public TransactionVM Parent { get; } = parent;
}
