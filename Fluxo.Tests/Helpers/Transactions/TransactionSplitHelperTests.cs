using Fluxo.Core.Enums;
using Fluxo.Helpers.Transactions;
using Fluxo.ViewModels.Entities;
using Xunit;

namespace Fluxo.Tests.Helpers.Transactions;

public sealed class TransactionSplitHelperTests
{
    [Fact]
    public void AddChild_creates_root_child_with_required_root_context()
    {
        var root = new TransactionVM
        {
            Type = TransactionType.Expense,
            SourceAccountId = 4,
            Name = "Root",
            Amount = 100m,
            OccurredOn = new DateTime(2026, 7, 27),
            IsIoU = true,
            ShouldAffectBalance = true,
            IsExcludedFromBudget = true
        };

        var child = TransactionSplitHelper.AddChild(root, null);

        Assert.Equal("New Sub-transaction", child.Name);
        Assert.Equal(0m, child.Amount);
        Assert.Equal(root.Type, child.Type);
        Assert.Equal(root.SourceAccountId, child.SourceAccountId);
        Assert.Equal(root.OccurredOn, child.OccurredOn);
        Assert.True(child.IsIoU);
        Assert.True(child.ShouldAffectBalance);
        Assert.True(child.IsExcludedFromBudget);
        Assert.Equal([child], root.ChildTransactions);
    }

    [Fact]
    public void AddChild_allows_grandchild_but_rejects_third_level()
    {
        var root = new TransactionVM();
        var child = TransactionSplitHelper.AddChild(root, null);
        var grandchild = TransactionSplitHelper.AddChild(root, child);

        Assert.False(TransactionSplitHelper.CanAddChild(root, grandchild));
        Assert.Throws<InvalidOperationException>(() => TransactionSplitHelper.AddChild(root, grandchild));
    }

    [Theory]
    [InlineData("", 100, false)]
    [InlineData("Valid", 0, false)]
    [InlineData("Valid", 100, true)]
    public void IsValidParent_requires_a_name_and_positive_amount(string name, decimal amount, bool expected)
    {
        var parent = new TransactionVM { Name = name, Amount = amount };

        Assert.Equal(expected, TransactionSplitHelper.IsValidParent(parent));
    }

    [Fact]
    public void SplitEqually_assigns_final_remainder()
    {
        var parent = new TransactionVM { Amount = 10m };
        parent.ChildTransactions.Add(new TransactionVM());
        parent.ChildTransactions.Add(new TransactionVM());
        parent.ChildTransactions.Add(new TransactionVM());

        TransactionSplitHelper.SplitEqually(parent);

        Assert.Equal([3m, 3m, 4m], parent.ChildTransactions.Select(item => item.Amount));
        Assert.Equal(0m, TransactionSplitHelper.GetRemainingAmount(parent));
    }

    [Fact]
    public void Reset_sets_only_direct_child_amounts_to_zero()
    {
        var root = new TransactionVM { Amount = 100m };
        var child = new TransactionVM { Amount = 50m };
        child.ChildTransactions.Add(new TransactionVM { Amount = 50m });
        root.ChildTransactions.Add(child);

        TransactionSplitHelper.Reset(root);

        Assert.Equal(0m, child.Amount);
        Assert.Equal(50m, child.ChildTransactions.Single().Amount);
    }

    [Fact]
    public void Remove_deletes_branch_and_returns_removed_nodes_parent()
    {
        var root = new TransactionVM();
        var child = TransactionSplitHelper.AddChild(root, null);
        var grandchild = TransactionSplitHelper.AddChild(root, child);

        var removed = TransactionSplitHelper.Remove(root, grandchild, out var parent);

        Assert.True(removed);
        Assert.Same(child, parent);
        Assert.Empty(child.ChildTransactions);
    }

    [Fact]
    public void Balance_checks_each_split_level()
    {
        var root = new TransactionVM { Amount = 100m };
        var child = new TransactionVM { Amount = 100m };
        child.ChildTransactions.Add(new TransactionVM { Amount = 90m });
        root.ChildTransactions.Add(child);

        Assert.False(TransactionSplitHelper.HasOverflow(root));
        Assert.False(TransactionSplitHelper.IsBalanced(root));

        child.ChildTransactions.Single().Amount = 100m;

        Assert.True(TransactionSplitHelper.IsBalanced(root));
    }

    [Fact]
    public void CanAddChild_rejects_an_overfilled_parent()
    {
        var root = new TransactionVM { Amount = 100m };
        root.ChildTransactions.Add(new TransactionVM { Amount = 101m });

        Assert.False(TransactionSplitHelper.CanAddChild(root, null));
    }
}
