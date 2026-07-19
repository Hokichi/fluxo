using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Popups;

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

public readonly record struct RecurringDraftSnapshot(
    int? EditingRecurringTransactionId,
    RecurringTransactionType Type,
    string Name,
    decimal Amount,
    RecurringPeriod RecurringPeriod,
    int RecurringTime,
    int AccountId,
    ExpenseCategory? Category,
    int? TagId,
    int? GoalId);
