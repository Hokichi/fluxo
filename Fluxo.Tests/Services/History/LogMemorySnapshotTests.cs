using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Services.History;
using Xunit;

namespace Fluxo.Tests.Services.History;

public sealed class LogMemorySnapshotTests
{
    [Fact]
    public void LogMemorySnapshot_Create_CapturesUnifiedTransactionFlags()
    {
        var account = new Account { Id = 1, Name = "Checking" };
        var tag = new Tag { Id = 2, Name = "Budget Reconciliation", HexCode = "#9ca3af" };
        var goal = new SavingGoal { Id = 4, Name = "Goal" };
        var transaction = new Transaction
        {
            Id = 3,
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            Account = account,
            TagId = tag.Id,
            Tag = tag,
            GoalId = goal.Id,
            RepaymentAccountId = 5,
            Name = "Lend",
            Amount = 10m,
            LoggedOn = new DateTime(2026, 6, 28, 12, 30, 0),
            ExpenseCategory = ExpenseCategory.Needs,
            IsIoU = true,
            ShouldAffectBalance = true,
            IsExcludedFromBudget = true
        };

        var snapshot = TransactionMemorySnapshot.Create(transaction);

        Assert.Equal(TransactionType.Expense, snapshot.Type);
        Assert.True(snapshot.IsIoU);
        Assert.True(snapshot.ShouldAffectBalance);
        Assert.True(snapshot.AffectsAccountBalance);
        Assert.True(snapshot.IsExcludedFromBudget);
        Assert.Equal(transaction.LoggedOn, snapshot.LoggedOn);
        Assert.Equal(goal.Id, snapshot.GoalId);
        Assert.Equal(5, snapshot.RepaymentAccountId);
    }
}
