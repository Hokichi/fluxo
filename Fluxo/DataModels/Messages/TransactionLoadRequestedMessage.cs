using CommunityToolkit.Mvvm.Messaging.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionLoadRequestedMessage(TransactionVM transaction)
    : ValueChangedMessage<TransactionVM>(transaction);
