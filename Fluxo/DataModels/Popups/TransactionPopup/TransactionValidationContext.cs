using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.DataModels.Popups.TransactionPopup;

public readonly record struct TransactionValidationContext(
    bool IsGoal,
    bool IsRepayment,
    bool IsRecurring,
    bool IsInstallments,
    bool IsRepaymentAmountInvalid,
    RecurringPeriod RecurringPeriod,
    string RecurringTimeText,
    DateTime StartDate,
    DateTime InstallmentEndDate,
    AccountVM? AmountValidationAccount,
    decimal CurrentTagSpending,
    bool IgnoreMaximumSpending = false);
