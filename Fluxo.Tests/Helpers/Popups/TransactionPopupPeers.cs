using Fluxo.ViewModels.Popups;

namespace Fluxo.Tests.Helpers.Popups;

internal sealed record TransactionPopupPeers(
    TransactionPopupVM Popup,
    TransactionBulkQueueVM Bulk,
    TransactionSplitsVM Splits);
