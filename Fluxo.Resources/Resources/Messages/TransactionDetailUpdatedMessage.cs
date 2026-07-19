namespace Fluxo.Resources.Resources.Messages;

[Flags]
public enum TransactionDetailChangedFields
{
    None = 0,
    Name = 1 << 0,
    Amount = 1 << 1,
    Date = 1 << 2,
    Category = 1 << 3,
    Account = 1 << 4,
    Tag = 1 << 5,
    Note = 1 << 6,
    Pin = 1 << 7,
    IoU = 1 << 8,
    BudgetExclusion = 1 << 9
}

public sealed class TransactionDetailUpdatedMessage(TransactionDetailUpdate value)
    : ValueChangedMessage<TransactionDetailUpdate>(value);

public sealed record TransactionDetailUpdate(
    int TransactionId,
    TransactionDetailSnapshot PreviousState,
    TransactionDetailChangedFields ChangedFields,
    bool SuppressNotificationInvalidation = false)
{
    public bool HasChanges => ChangedFields != TransactionDetailChangedFields.None;

    public bool AffectsAllTimeTotals =>
        (ChangedFields & (TransactionDetailChangedFields.Amount | TransactionDetailChangedFields.Category |
                          TransactionDetailChangedFields.BudgetExclusion)) != 0;

    public bool AffectsTagOrdering => (ChangedFields & TransactionDetailChangedFields.Tag) != 0;

    public bool AffectsVisibleMoneyOut =>
        (ChangedFields &
         (TransactionDetailChangedFields.Amount | TransactionDetailChangedFields.Date |
          TransactionDetailChangedFields.Account)) != 0;

    public bool AffectsAccountState =>
        (ChangedFields & (TransactionDetailChangedFields.Amount | TransactionDetailChangedFields.Account)) != 0;
}

public sealed record TransactionDetailSnapshot(
    decimal Amount,
    DateTime Date,
    ExpenseCategory Category,
    int AccountId,
    int TagId);
