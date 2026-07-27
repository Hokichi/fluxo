using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.RecurringDraftContracts;

public readonly record struct RecurringDraftSaveInput(
    int? EditingRecurringTransactionId,
    RecurringTransactionType Type,
    string Name,
    decimal Amount,
    RecurringPeriod RecurringPeriod,
    int RecurringTime,
    int AccountId,
    ExpenseCategory? Category,
    int? TagId,
    int? GoalId,
    DateTime? EndDate);
