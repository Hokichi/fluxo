namespace Fluxo.Helpers.Transaction;

public static class AddRecurringTransactionHelper
{
    public static State CreateState(bool isLocked) => new(isLocked, isLocked, true, false);

    public readonly record struct State(
        bool IsTransactionTypeLocked,
        bool IsRecurringModeLocked,
        bool IsRecurring,
        bool IsInstallments);
}
