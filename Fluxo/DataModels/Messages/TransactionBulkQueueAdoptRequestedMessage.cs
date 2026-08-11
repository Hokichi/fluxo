using CommunityToolkit.Mvvm.Messaging.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionBulkQueueAdoptRequestedMessage(TransactionVM transaction)
    : ValueChangedMessage<TransactionVM>(transaction);
