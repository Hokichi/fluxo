using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Popups;

public enum TransactionPopupRequestKind
{
    AddTransaction,
    AddRecurringTransaction,
    EditRecurringTransaction,
    ViewTransaction,
    EditTransaction,
    Repayment,
    RepaymentProcessing,
    GoalProcessing,
    RecurringProcessing,
    RecurringDraft
}

public sealed record TransactionPopupRequest
{
    public TransactionPopupRequestKind Kind { get; init; } = TransactionPopupRequestKind.AddTransaction;
    public TransactionPopupDraft? Draft { get; init; }
    public RecurringDraftSnapshot? RecurringDraft { get; init; }
    public TransactionVM? Transaction { get; init; }
    public AccountVM? Account { get; init; }
    public int? RecurringTransactionId { get; init; }
    public bool LockRecurringMode { get; init; }
    public bool UseRecurringDraftMessages { get; init; }
    public IReadOnlyList<AccountVM>? Accounts { get; init; }
    public IReadOnlyList<SavingGoalVM>? Goals { get; init; }
    public IReadOnlyList<RecurringTransactionVM>? RecurringTransactions { get; init; }

    public static TransactionPopupRequest Add(TransactionPopupDraft? draft = null) =>
        new() { Draft = draft };

    public static TransactionPopupRequest AddRecurring(bool isLocked) => new()
    {
        Kind = TransactionPopupRequestKind.AddRecurringTransaction,
        LockRecurringMode = isLocked
    };

    public static TransactionPopupRequest View(TransactionVM transaction) => new()
    {
        Kind = TransactionPopupRequestKind.ViewTransaction,
        Transaction = transaction
    };
}
