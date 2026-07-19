using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transaction;

public static class TransactionMappingHelper
{
    public static TransactionVM CreateLoaded(TransactionVM source) => Copy(source);

    public static TransactionVM CreatePending(TransactionVM source) => new()
    {
        Type = source.Type,
        SourceAccountId = source.SourceAccountId,
        GoalId = source.GoalId,
        RepaymentAccountId = source.RepaymentAccountId,
        Account = Copy(source.Account),
        Name = source.Name,
        Amount = source.Amount,
        OccurredOn = source.OccurredOn,
        Notes = source.Notes,
        ExpenseCategory = source.ExpenseCategory,
        Tag = source.Tag is null ? null : Copy(source.Tag),
        IsPinned = source.IsPinned,
        IsIoU = source.IsIoU,
        ShouldAffectBalance = source.ShouldAffectBalance,
        IsExcludedFromBudget = source.IsExcludedFromBudget
    };

    public static TransactionVM CreatePending() => new();

    private static TransactionVM Copy(TransactionVM source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        SourceAccountId = source.SourceAccountId,
        GoalId = source.GoalId,
        RepaymentAccountId = source.RepaymentAccountId,
        Account = Copy(source.Account),
        Name = source.Name,
        Amount = source.Amount,
        OccurredOn = source.OccurredOn,
        LoggedOn = source.LoggedOn,
        Notes = source.Notes,
        ExpenseCategory = source.ExpenseCategory,
        Tag = source.Tag is null ? null : Copy(source.Tag),
        ParentTransactionId = source.ParentTransactionId,
        IsPinned = source.IsPinned,
        IsForDeletion = source.IsForDeletion,
        IsIoU = source.IsIoU,
        ShouldAffectBalance = source.ShouldAffectBalance,
        IsExcludedFromBudget = source.IsExcludedFromBudget
    };

    private static AccountVM Copy(AccountVM source) => new()
    {
        AccountLimit = source.AccountLimit,
        Balance = source.Balance,
        DeductSource = source.DeductSource,
        Id = source.Id,
        InterestRate = source.InterestRate,
        IsEnabled = source.IsEnabled,
        IsDefault = source.IsDefault,
        MaximumSpending = source.MaximumSpending,
        MinimumPayment = source.MinimumPayment,
        MonthlyDueDate = source.MonthlyDueDate,
        MoneyIn = source.MoneyIn,
        MoneyOut = source.MoneyOut,
        Name = source.Name,
        PinnedOnUI = source.PinnedOnUI,
        AccountType = source.AccountType,
        SpentAmount = source.SpentAmount,
        IsSelected = source.IsSelected,
        Difference = source.Difference,
        IsOverdue = source.IsOverdue
    };

    private static TagVM Copy(TagVM source) => new()
    {
        HexCode = source.HexCode,
        Id = source.Id,
        IsSystemTag = source.IsSystemTag,
        Name = source.Name,
        SpendingLimit = source.SpendingLimit
    };
}
