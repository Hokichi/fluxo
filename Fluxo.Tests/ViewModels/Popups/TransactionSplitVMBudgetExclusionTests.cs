using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionSplitVMBudgetExclusionTests
{
    [Fact]
    public void ClearParentTransactionCategory_ClearsExistingCategory()
    {
        var parent = new Transaction { ExpenseCategory = ExpenseCategory.Needs };

        TransactionSplitVM.ClearParentTransactionCategory(parent);

        Assert.Null(parent.ExpenseCategory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyParentStateToChildTransactions_MirrorsBudgetExclusion(bool isExcludedFromBudget)
    {
        var parentAccount = new Account { Id = 7, Name = "Checking" };
        var children = new[]
        {
            new Transaction { SourceAccountId = 1, IsExcludedFromBudget = !isExcludedFromBudget },
            new Transaction { SourceAccountId = 2, IsExcludedFromBudget = !isExcludedFromBudget }
        };

        TransactionSplitVM.ApplyParentStateToChildTransactions(
            children,
            parentAccount,
            isExcludedFromBudget);

        Assert.All(children, child =>
        {
            Assert.Same(parentAccount, child.Account);
            Assert.Equal(parentAccount.Id, child.SourceAccountId);
            Assert.Equal(isExcludedFromBudget, child.IsExcludedFromBudget);
        });
    }
}
