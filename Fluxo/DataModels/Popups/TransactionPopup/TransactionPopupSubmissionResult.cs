
namespace Fluxo.DataModels.Popups.TransactionPopup;

public readonly record struct TransactionPopupSubmissionResult(
    bool IsSuccess,
    string? ErrorMessage,
    int? TransactionId = null,
    bool RequiresConfirmation = false)
{
    public static TransactionPopupSubmissionResult Success(int? transactionId = null) =>
        new(true, null, transactionId, false);

    public static TransactionPopupSubmissionResult Failure(string? errorMessage) =>
        new(false, errorMessage, null, false);

    public static TransactionPopupSubmissionResult Confirmation(string? message) =>
        new(false, message, null, true);
}
