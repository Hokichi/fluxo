using CommunityToolkit.Mvvm.Messaging.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionSplitChangedMessage(TransactionVM root)
    : ValueChangedMessage<TransactionVM>(root);
