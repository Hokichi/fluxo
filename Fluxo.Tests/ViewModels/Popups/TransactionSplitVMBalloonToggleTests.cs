using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionSplitVMBalloonToggleTests
{
    [Theory]
    [InlineData(false, false, "A one-time transaction")]
    [InlineData(true, false, "A transaction marked as debt/IoU but doesn't affect the accounts")]
    [InlineData(true, true, "A transaction marked as debt/IoU and affects the accounts")]
    public void ModeDescription_MatchesBalanceBehavior(bool isIoU, bool posted, string expected)
    {
        Assert.Equal(expected, TransactionSplitVM.GetTransactionModeDescription(isIoU, posted));
    }

    [Theory]
    [InlineData(1, 1, 100, 95, 10, false, false)]
    [InlineData(1, 2, 100, 90, 10, false, false)]
    [InlineData(1, 2, 100, 95, 10, false, true)]
    [InlineData(1, 2, 100, 95, 10, true, false)]
    public void MaximumSpendingConfirmation_RequiresChangedOverflowingUnapprovedAccount(
        int currentAccountId,
        int destinationAccountId,
        decimal maximumSpending,
        decimal destinationSpending,
        decimal amount,
        bool overflowApproved,
        bool expected)
    {
        Assert.Equal(expected, TransactionSplitVM.RequiresMaximumSpendingConfirmation(
            currentAccountId,
            destinationAccountId,
            maximumSpending,
            destinationSpending,
            amount,
            overflowApproved));
    }

    [Fact]
    public void CalculateAccountSpending_IncludesExcludedButNotDeletedOrSplitParents()
    {
        var transactions = new[]
        {
            new Transaction { Id = 1, SourceAccountId = 2, Type = TransactionType.Expense, Amount = 30m },
            new Transaction { Id = 2, SourceAccountId = 2, Type = TransactionType.Expense, Amount = 20m, IsExcludedFromBudget = true },
            new Transaction { Id = 3, SourceAccountId = 2, Type = TransactionType.Expense, Amount = 50m, IsForDeletion = true },
            new Transaction { Id = 4, SourceAccountId = 3, Type = TransactionType.Expense, Amount = 100m },
            new Transaction { Id = 5, SourceAccountId = 2, Type = TransactionType.Expense, Amount = 10m, ParentTransactionId = 1 }
        };

        Assert.Equal(30m, TransactionSplitVM.CalculateAccountSpending(transactions, accountId: 2));
    }

    [Fact]
    public void CreateEqualSplitAmounts_PutsRoundingRemainderOnLastChild()
    {
        Assert.Equal([33.33m, 33.33m, 33.34m], TransactionSplitVM.CreateEqualSplitAmounts(100m, 3));
    }

    [Fact]
    public void SplitRowModeToggles_BindToPostedIouState()
    {
        var row = new TransactionSplitRowVM();

        Assert.True(row.IsRegularMode);
        Assert.False(row.IsPostedIoUMode);

        row.IsPostedIoUMode = true;

        Assert.False(row.IsRegularMode);
        Assert.True(row.IsPostedIoUMode);

        row.IsRegularMode = true;

        Assert.True(row.IsRegularMode);
        Assert.False(row.IsPostedIoUMode);
    }

    [Fact]
    public void SplitRow_CheckingCreatesOneChildAndUncheckingPreservesIt()
    {
        var row = new TransactionSplitRowVM { AmountText = 100m };

        row.IsSplit = true;
        var child = Assert.Single(row.ChildRows);
        row.IsSplit = false;

        Assert.Same(child, Assert.Single(row.ChildRows));
    }

    [Fact]
    public void SplitRow_CheckingOneRowDoesNotSplitAnother()
    {
        var splitRow = new TransactionSplitRowVM();
        var regularRow = new TransactionSplitRowVM();

        splitRow.IsSplit = true;

        Assert.True(splitRow.IsSplit);
        Assert.Single(splitRow.ChildRows);
        Assert.False(regularRow.IsSplit);
        Assert.Empty(regularRow.ChildRows);
    }

    [Fact]
    public void SplitRow_ChildAmountsOverParentMarkChangedChildInvalid()
    {
        var row = new TransactionSplitRowVM { AmountText = 10m, IsSplit = true };
        var child = Assert.Single(row.ChildRows);
        child.AmountText = 11m;

        row.RecalculateChildRemainder(child);

        Assert.True(row.HasNegativeChildRemainder);
        Assert.True(child.IsCausingNegativeRemainder);
    }

    [Fact]
    public void SplitRow_ShowsRemainingAndHidesLeafMetadata()
    {
        var row = new TransactionSplitRowVM { AmountText = 100m, IsSplit = true };
        var child = Assert.Single(row.ChildRows);
        child.AmountText = 30m;

        row.RecalculateChildRemainder(child);

        Assert.Equal(70m, row.RemainingAmount);
        Assert.False(row.ShowLeafTags);
        Assert.False(row.CanSelectCategory);
    }

    [Fact]
    public void SplitEqually_UsesDirectChildren_AndSplitClearsThem()
    {
        var row = new TransactionSplitRowVM { AmountText = 100m, IsSplit = true };
        row.AddChildRow();

        row.SetSplitEquallyModeCommand.Execute(null);
        Assert.Equal([50m, 50m], row.ChildRows.Select(child => child.AmountText));

        row.SetSplitModeCommand.Execute(null);
        Assert.All(row.ChildRows, child => Assert.Equal(0m, child.AmountText));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void NestedBudgetExclusion_UsesParentOwnAndPostedIou(
        bool parentExcluded, bool childExcluded, bool postedIou, bool expected)
    {
        var parent = new TransactionSplitRowVM { IsExcludedFromBudget = parentExcluded };
        var child = new TransactionSplitRowVM { IsExcludedFromBudget = childExcluded, IsIoU = postedIou };

        Assert.Equal(expected, TransactionSplitVM.IsNestedRowExcluded(parent, child));
    }

    [Fact]
    public void ClearSplitParentMetadata_ClearsCategoryAndTag()
    {
        var tag = new Tag { Id = 4, Name = "Food" };
        var transaction = new Transaction { ExpenseCategory = ExpenseCategory.Needs, Tag = tag, TagId = tag.Id };

        TransactionSplitVM.ClearSplitParentMetadata(transaction);

        Assert.Null(transaction.ExpenseCategory);
        Assert.Null(transaction.Tag);
        Assert.Null(transaction.TagId);
    }

    [Fact]
    public void RecalculateNestedSplitRemainders_RefreshesParentAmountChange()
    {
        var parent = new TransactionSplitRowVM { AmountText = 10m, IsSplit = true };
        var child = Assert.Single(parent.ChildRows);
        child.AmountText = 10m;

        parent.AmountText = 9m;
        TransactionSplitVM.RecalculateNestedSplitRemainders([parent]);

        Assert.True(parent.HasNegativeChildRemainder);
        Assert.True(child.IsCausingNegativeRemainder);
    }

    [Fact]
    public void ApplyEqualSplitAmounts_RecalculatesWhenRowsChange()
    {
        var rows = new List<TransactionSplitRowVM>
        {
            new(),
            new()
        };

        TransactionSplitVM.ApplyEqualSplitAmounts(rows, 100m);
        rows.Add(new TransactionSplitRowVM());
        TransactionSplitVM.ApplyEqualSplitAmounts(rows, 100m);

        Assert.Equal([33.33m, 33.33m, 33.34m], rows.Select(row => row.AmountText));
    }

    [Fact]
    public void ClearSplitAmounts_OnlyClearsAmounts()
    {
        var tag = new TagVM { Id = 4, Name = "Food", HexCode = "#22C55E" };
        var row = new TransactionSplitRowVM
        {
            AmountText = 25m,
            NameText = "Lunch",
            IsIoU = true,
            SelectedExpenseCategory = ExpenseCategory.Wants,
            SelectedTag = tag
        };

        TransactionSplitVM.ClearSplitAmounts([row]);

        Assert.Equal(0m, row.AmountText);
        Assert.Equal("Lunch", row.NameText);
        Assert.True(row.IsIoU);
        Assert.Equal(ExpenseCategory.Wants, row.SelectedExpenseCategory);
        Assert.Same(tag, row.SelectedTag);
    }

    [Fact]
    public void SplitChangeCheck_IgnoresEmptyNewRows()
    {
        var row = new TransactionSplitRowVM();

        Assert.False(TransactionSplitVM.HasEffectiveSplitChanges([row], [], []));
    }

    [Fact]
    public void SplitChangeCheck_DetectsChangedPersistedRow()
    {
        var saved = new TransactionSplitRowVM { TransactionId = 7, AmountText = 25m, NameText = "Lunch" };
        var changed = new TransactionSplitRowVM { TransactionId = 7, AmountText = 30m, NameText = "Lunch" };

        Assert.True(TransactionSplitVM.HasEffectiveSplitChanges([changed], [saved], []));
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(99, 100, false)]
    public void CanUseEqualSplit_RequiresAmountAtLeastOriginal(decimal amount, decimal original, bool expected)
    {
        Assert.Equal(expected, TransactionSplitVM.CanUseEqualSplit(amount, original));
    }
}
