using Fluxo.Helpers.Transaction;
using Fluxo.ViewModels.Entities;
using Xunit;

namespace Fluxo.Tests.ViewModels.Entities;

public sealed class TransactionVMSemanticsTests
{
    [Fact]
    public void Identity_only_changes_do_not_make_transactions_unequal()
    {
        var loaded = new TransactionVM { Id = 7, Name = "Coffee", Amount = 5m };
        var pending = new TransactionVM { Id = 0, Name = "Coffee", Amount = 5m };

        Assert.Equal(loaded, pending);
        Assert.Equal(loaded.GetHashCode(), pending.GetHashCode());
    }

    [Fact]
    public void Business_field_changes_make_transactions_unequal()
    {
        var first = new TransactionVM { Name = "Coffee", Amount = 5m };
        var second = new TransactionVM { Name = "Coffee", Amount = 6m };

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Loaded_snapshot_retains_identity_and_is_isolated()
    {
        var source = new TransactionVM
        {
            Id = 7,
            Name = "Coffee",
            Account = new AccountVM { Id = 3, Name = "Checking" },
            Tag = new TagVM { Id = 5, Name = "Food" }
        };

        var snapshot = TransactionMappingHelper.CreateLoaded(source);
        snapshot.Name = "Changed";
        snapshot.Account.Name = "Other";
        snapshot.Tag!.Name = "Other";

        Assert.Equal(7, snapshot.Id);
        Assert.Equal("Coffee", source.Name);
        Assert.Equal("Checking", source.Account.Name);
        Assert.Equal("Food", source.Tag.Name);
    }

    [Fact]
    public void Pending_snapshot_copies_business_data_and_clears_identity()
    {
        var source = new TransactionVM
        {
            Id = 7,
            Name = "Coffee",
            LoggedOn = DateTime.Now,
            ParentTransactionId = 4,
            IsForDeletion = true,
            Amount = 5m
        };

        var pending = TransactionMappingHelper.CreatePending(source);

        Assert.Equal(0, pending.Id);
        Assert.Equal(default, pending.LoggedOn);
        Assert.Null(pending.ParentTransactionId);
        Assert.False(pending.IsForDeletion);
        Assert.Equal(source.Name, pending.Name);
        Assert.Equal(source.Amount, pending.Amount);
    }

    [Fact]
    public void Empty_pending_snapshot_has_cleared_identity()
    {
        var pending = TransactionMappingHelper.CreatePending();

        Assert.Equal(0, pending.Id);
        Assert.Equal(default, pending.LoggedOn);
        Assert.Null(pending.ParentTransactionId);
        Assert.False(pending.IsForDeletion);
    }

    [Fact]
    public void Child_total_tracks_direct_child_amount_changes()
    {
        var parent = new TransactionVM { Amount = 10m };
        var child = new TransactionVM { Amount = 4m };

        parent.ChildTransactions.Add(child);

        Assert.Equal(4m, parent.ChildAmountTotal);
        Assert.False(parent.HasChildAmountOverflow);

        child.Amount = 11m;

        Assert.Equal(11m, parent.ChildAmountTotal);
        Assert.True(parent.HasChildAmountOverflow);
        Assert.False(parent.CanAddChildTransaction);
    }
}
