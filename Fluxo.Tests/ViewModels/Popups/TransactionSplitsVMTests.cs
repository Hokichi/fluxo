using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.DataModels.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionSplitsVMTests
{
    [Fact]
    public void TransactionSplitsVM_Child_SelectionLoadsChildAndLeavingTabLoads_Root()
    {
        var messenger = new WeakReferenceMessenger();
        var loads = new List<TransactionVM>();
        var recipient = new object();
        messenger.Register<object, TransactionLoadRequestedMessage, TransactionPopupMessageToken>(recipient, TransactionPopupMessageToken.Default,
            (_, message) => loads.Add(message.Value));
        var root = ValidRoot(100m);
        using var vm = new TransactionSplitsVM(messenger);

        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);
        vm.AddSplitCommand.Execute(null);
        var child = root.ChildTransactions.Single();
        vm.SelectSplitCommand.Execute(child);
        messenger.Send(new TransactionSplitContextChangedMessage(root, false, true), TransactionPopupMessageToken.Default);

        Assert.Same(child, loads[^2]);
        Assert.Same(root, loads[^1]);
        Assert.Null(vm.SelectedSplitTransaction);
    }

    [Fact]
    public void TransactionSplitsVM_Child_CannotBeSelectedOutsideSplit_Tab()
    {
        var messenger = new WeakReferenceMessenger();
        var root = ValidRoot(100m);
        var child = ValidLeaf(100m);
        root.ChildTransactions.Add(child);
        using var vm = new TransactionSplitsVM(messenger);
        messenger.Send(new TransactionSplitContextChangedMessage(root, false, false), TransactionPopupMessageToken.Default);

        vm.SelectSplitCommand.Execute(child);

        Assert.Null(vm.SelectedSplitTransaction);
    }

    [Theory]
    [InlineData(40, "Split amounts must equal their parent amount.")]
    [InlineData(101, "Split amounts cannot exceed their parent amount.")]
    public void TransactionSplitsVM_Invalid_TotalsFailValidation(decimal childAmount, string expectedMessage)
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        root.ChildTransactions.Add(ValidLeaf(childAmount));
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        var result = messenger
            .Send(new TransactionSplitValidationRequestedMessage(root), TransactionPopupMessageToken.Default)
            .Response;

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedMessage, result.ErrorMessage);
    }

    [Fact]
    public void TransactionSplitsVM_Invalid_LeafFailsValidationInReal_Time()
    {
        var messenger = new WeakReferenceMessenger();
        var changes = 0;
        var recipient = new object();
        messenger.Register<object, TransactionSplitChangedMessage, TransactionPopupMessageToken>(recipient, TransactionPopupMessageToken.Default,
            (_, _) => changes++);
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        var leaf = ValidLeaf(100m);
        root.ChildTransactions.Add(leaf);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        leaf.Name = string.Empty;
        var result = messenger
            .Send(new TransactionSplitValidationRequestedMessage(root), TransactionPopupMessageToken.Default)
            .Response;

        Assert.True(changes > 0);
        Assert.False(result.IsSuccess);
        Assert.Equal("Please fix the invalid sub-transactions.", result.ErrorMessage);
    }

    [Fact]
    public void TransactionSplitsVM_Validation_MetadataChangesDoNotPublishSplit_Changes()
    {
        var messenger = new WeakReferenceMessenger();
        var changes = 0;
        var recipient = new object();
        messenger.Register<object, TransactionSplitChangedMessage, TransactionPopupMessageToken>(recipient,
            TransactionPopupMessageToken.Default, (_, _) => changes++);
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        var leaf = ValidLeaf(100m);
        root.ChildTransactions.Add(leaf);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true),
            TransactionPopupMessageToken.Default);
        changes = 0;

        leaf.IsValid = !leaf.IsValid;

        Assert.Equal(0, changes);
    }

    [Fact]
    public void TransactionSplitsVM_Balanced_TaglessExpenseLeafIs_Valid()
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        var leaf = ValidLeaf(100m);
        leaf.Tag = null;
        root.ChildTransactions.Add(leaf);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true),
            TransactionPopupMessageToken.Default);

        var result = messenger
            .Send(new TransactionSplitValidationRequestedMessage(root), TransactionPopupMessageToken.Default)
            .Response;

        Assert.True(result.IsSuccess);
        Assert.True(leaf.IsValid);
    }

    [Fact]
    public void TransactionSplitsVM_Invalid_SplitTreeStaysEditableWhenRootFieldsAre_Valid()
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        vm.AddSplitCommand.Execute(null);
        root.ChildTransactions.Single().Amount = 0m;

        Assert.False(vm.ShowInvalidSplitPlaceholder);
        Assert.False(messenger.Send(new TransactionSplitValidationRequestedMessage(root), TransactionPopupMessageToken.Default).Response.IsSuccess);
    }

    [Fact]
    public void TransactionSplitsVM_Non_ModifiableSplitContextShowsUnavailable_Placeholder()
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);

        messenger.Send(
            new TransactionSplitContextChangedMessage(root, true, false),
            TransactionPopupMessageToken.Default);

        Assert.True(vm.ShowInvalidSplitPlaceholder);
    }

    [Fact]
    public void TransactionSplitsVM_Add_DeleteEqualAndResetMutateTheRoot_Tree()
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        vm.AddSplitCommand.Execute(null);
        vm.SelectSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);
        vm.SplitEquallyCommand.Execute(null);
        Assert.Equal([50m, 50m], root.ChildTransactions.Select(child => child.Amount));

        vm.ResetSplitCommand.Execute(null);
        Assert.Equal([0m, 0m], root.ChildTransactions.Select(child => child.Amount));

        var second = root.ChildTransactions[1];
        vm.DeleteSplitCommand.Execute(second);
        Assert.DoesNotContain(root.ChildTransactions,
            child => ReferenceEquals(child, second));
    }

    [Fact]
    public void TransactionSplitsVM_SplitEqually_NonZeroChildrenDeclined_DoesNotOverwrite()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        var root = ValidRoot(100m);
        root.ChildTransactions.Add(ValidLeaf(25m));
        root.ChildTransactions.Add(ValidLeaf(75m));
        messenger.Register<object, TransactionSplitEqualOverrideRequestedMessage, TransactionPopupMessageToken>(
            recipient, TransactionPopupMessageToken.Default, (_, message) => message.Reply(false));
        using var vm = new TransactionSplitsVM(messenger);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal([25m, 75m], root.ChildTransactions.Select(child => child.Amount));
    }

    [Fact]
    public void TransactionSplitsVM_SplitEqually_NonZeroChildrenAccepted_OverwritesAllChildren()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        var root = ValidRoot(100m);
        root.ChildTransactions.Add(ValidLeaf(25m));
        root.ChildTransactions.Add(ValidLeaf(75m));
        messenger.Register<object, TransactionSplitEqualOverrideRequestedMessage, TransactionPopupMessageToken>(
            recipient, TransactionPopupMessageToken.Default, (_, message) => message.Reply(true));
        using var vm = new TransactionSplitsVM(messenger);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal([50m, 50m], root.ChildTransactions.Select(child => child.Amount));
    }

    [Fact]
    public void TransactionSplitsVM_SplitEqually_ZeroChildren_SplitsWithoutConfirmation()
    {
        var messenger = new WeakReferenceMessenger();
        var root = ValidRoot(100m);
        root.ChildTransactions.Add(ValidLeaf(0m));
        root.ChildTransactions.Add(ValidLeaf(0m));
        using var vm = new TransactionSplitsVM(messenger);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal([50m, 50m], root.ChildTransactions.Select(child => child.Amount));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-100, -50, -50)]
    public void TransactionSplitsVM_SplitEqually_ZeroOrNegativeParent_RemainsExecutable(
        decimal parentAmount,
        decimal firstChildAmount,
        decimal secondChildAmount)
    {
        var messenger = new WeakReferenceMessenger();
        var root = ValidRoot(parentAmount);
        root.ChildTransactions.Add(ValidLeaf(0m));
        root.ChildTransactions.Add(ValidLeaf(0m));
        using var vm = new TransactionSplitsVM(messenger);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        Assert.True(vm.SplitEquallyCommand.CanExecute(null));
        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal([firstChildAmount, secondChildAmount], root.ChildTransactions.Select(child => child.Amount));
    }

    [Fact]
    public void TransactionSplitsVM_SplitEqually_RootCreatesNestedChildOverflow()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        var root = ValidRoot(100m);
        var child = ValidLeaf(100m);
        child.ChildTransactions.Add(ValidLeaf(75m));
        child.ChildTransactions.Add(ValidLeaf(0m));
        root.ChildTransactions.Add(child);
        root.ChildTransactions.Add(ValidLeaf(0m));
        messenger.Register<object, TransactionSplitEqualOverrideRequestedMessage, TransactionPopupMessageToken>(
            recipient, TransactionPopupMessageToken.Default, (_, message) => message.Reply(true));
        using var vm = new TransactionSplitsVM(messenger);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);

        vm.SplitEquallyCommand.Execute(null);
        var result = messenger.Send(
            new TransactionSplitValidationRequestedMessage(root), TransactionPopupMessageToken.Default).Response;

        Assert.True(child.HasChildAmountOverflow);
        Assert.False(result.IsSuccess);
        Assert.Equal("Split amounts cannot exceed their parent amount.", result.ErrorMessage);
    }

    [Fact]
    public void TransactionSplitsVM_Grandchild_IsLastSupportedLevelAndParentClassificationIs_Cleared()
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionSplitsVM(messenger);
        var root = ValidRoot(100m);
        messenger.Send(new TransactionSplitContextChangedMessage(root, true, true), TransactionPopupMessageToken.Default);
        vm.AddSplitCommand.Execute(null);
        var child = root.ChildTransactions.Single();
        child.Amount = 100m;
        child.Tag = new TagVM { Id = 2, Name = "Food" };
        child.ExpenseCategory = ExpenseCategory.Needs;

        vm.AddSplitCommand.Execute(child);
        var grandchild = child.ChildTransactions.Single();

        Assert.Null(child.Tag);
        Assert.Null(child.ExpenseCategory);
        Assert.False(vm.AddSplitCommand.CanExecute(grandchild));
    }

    private static TransactionVM ValidRoot(decimal amount) => new()
    {
        Type = TransactionType.Expense,
        SourceAccountId = 1,
        Account = new AccountVM { Id = 1, Name = "Checking" },
        Name = "Root",
        Amount = amount,
        OccurredOn = DateTime.Today
    };

    private static TransactionVM ValidLeaf(decimal amount) => new()
    {
        Type = TransactionType.Expense,
        SourceAccountId = 1,
        Account = new AccountVM { Id = 1, Name = "Checking" },
        Name = "Leaf",
        Amount = amount,
        OccurredOn = DateTime.Today,
        ExpenseCategory = ExpenseCategory.Needs,
        Tag = new TagVM { Id = 1, Name = "General" }
    };
}
