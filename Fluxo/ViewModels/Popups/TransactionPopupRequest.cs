using Fluxo.ViewModels.Entities;

using Fluxo.DataModels.Popups.TransactionPopup;
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
