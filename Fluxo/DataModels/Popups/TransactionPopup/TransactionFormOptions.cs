using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.TransactionPopup;

public readonly record struct TransactionFormOptions(
    bool IsGoal,
    bool IsRepayment,
    bool IsRecurring,
    bool IsInstallments,
    RecurringPeriod SelectedRecurringPeriod,
    string RecurringTimeText,
    DateTime StartDate,
    DateTime InstallmentEndDate);
