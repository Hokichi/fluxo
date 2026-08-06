using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Messages;

public sealed class TransactionSplitContextChangedMessage(
    TransactionVM root,
    bool isTabSelected,
    bool canModify,
    bool isRootLoaded = false)
{
    public TransactionVM Root { get; } = root;
    public bool IsTabSelected { get; } = isTabSelected;
    public bool CanModify { get; } = canModify;
    public bool IsRootLoaded { get; } = isRootLoaded;
}
