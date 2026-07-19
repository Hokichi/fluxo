using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Popups;

public readonly record struct TransactionPopupDraft(
    bool IsExpense,
    string Name,
    decimal AmountText,
    int? AccountId,
    DateTime Date,
    string Note,
    ExpenseCategory? Category,
    int? TagId,
    bool IsGoal = false,
    int? GoalId = null,
    bool IsIoU = false,
    bool IsExcludedFromBudget = false,
    bool LockTransactionType = false,
    bool ShouldAffectBalance = false);
