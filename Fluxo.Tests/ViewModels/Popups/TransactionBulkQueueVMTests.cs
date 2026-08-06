using CommunityToolkit.Mvvm.Messaging;
using Fluxo.DataModels.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionBulkQueueVMTests
{
    [Fact]
    public void Adding_and_switching_preserves_exact_instances()
    {
        var messenger = new WeakReferenceMessenger();
        var loads = new List<TransactionVM>();
        var recipient = new object();
        messenger.Register<object, TransactionLoadRequestedMessage>(recipient,
            (_, message) => loads.Add(message.Value));
        var account = new AccountVM { Id = 1, Name = "Checking", IsDefault = true };
        var first = new TransactionVM
        {
            Account = account,
            SourceAccountId = account.Id,
            Name = "First",
            Amount = 10m
        };
        using var vm = new TransactionBulkQueueVM(messenger);

        messenger.Send(new TransactionBulkQueueResetMessage(true, [first], account));
        vm.AddQueuedTransactionCommand.Execute(null);
        var second = vm.SelectedQueuedTransaction!;
        second.Name = "Second";
        second.Amount = 20m;
        vm.SelectedQueuedTransaction = first;
        vm.SelectedQueuedTransaction = second;

        Assert.Same(first, vm.QueuedTransactions[0]);
        Assert.Same(second, vm.QueuedTransactions[1]);
        Assert.Same(second, loads[^1]);
    }

    [Theory]
    [InlineData("", 0, false)]
    [InlineData("Named", 0, true)]
    [InlineData("", 1, true)]
    public void Queue_reports_exact_bulk_change_rule(string name, decimal amount, bool expected)
    {
        var messenger = new WeakReferenceMessenger();
        TransactionBulkQueueStateChangedMessage? state = null;
        var recipient = new object();
        messenger.Register<object, TransactionBulkQueueStateChangedMessage>(recipient,
            (_, message) => state = message);
        using var vm = new TransactionBulkQueueVM(messenger);

        messenger.Send(new TransactionBulkQueueResetMessage(
            true, [new TransactionVM { Name = name, Amount = amount }], null));

        Assert.Equal(expected, state!.HasChanges);
    }

    [Fact]
    public void Select_and_remove_requests_operate_on_exact_instances()
    {
        var messenger = new WeakReferenceMessenger();
        var first = new TransactionVM { Name = "First" };
        var second = new TransactionVM { Name = "Second" };
        using var vm = new TransactionBulkQueueVM(messenger);
        messenger.Send(new TransactionBulkQueueResetMessage(true, [first, second], null));

        messenger.Send(new TransactionBulkQueueSelectRequestedMessage(second));
        Assert.Same(second, vm.SelectedQueuedTransaction);

        messenger.Send(new TransactionBulkQueueRemoveRequestedMessage(second));
        Assert.DoesNotContain(second, vm.QueuedTransactions);
        Assert.Same(first, vm.SelectedQueuedTransaction);
    }
}
