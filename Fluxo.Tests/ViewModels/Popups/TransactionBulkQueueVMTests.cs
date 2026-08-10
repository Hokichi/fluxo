using CommunityToolkit.Mvvm.Messaging;
using Fluxo.DataModels.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using System.Windows.Data;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionBulkQueueVMTests
{
    [Fact]
    public void Queue_groups_dates_descending_and_times_descending()
    {
        var messenger = new WeakReferenceMessenger();
        var oldDay = new TransactionVM { Name = "Old day", OccurredOn = new DateTime(2026, 8, 9, 23, 0, 0) };
        var earlier = new TransactionVM { Name = "Earlier", OccurredOn = new DateTime(2026, 8, 10, 8, 0, 0) };
        var later = new TransactionVM { Name = "Later", OccurredOn = new DateTime(2026, 8, 10, 17, 0, 0) };
        using var vm = new TransactionBulkQueueVM(messenger);

        messenger.Send(new TransactionBulkQueueResetMessage(true, [oldDay, earlier, later], null),
            TransactionPopupMessageToken.Default);

        var groups = vm.QueuedTransactionsView.Groups!.Cast<CollectionViewGroup>().ToList();

        Assert.Equal(new DateTime(2026, 8, 10), groups[0].Name);
        Assert.Collection(groups[0].Items.Cast<TransactionVM>(),
            item => Assert.Equal("Later", item.Name),
            item => Assert.Equal("Earlier", item.Name));
        Assert.Equal(new DateTime(2026, 8, 9), groups[1].Name);
    }

    [Fact]
    public void Changing_queued_occurrence_reorders_grouped_view()
    {
        var messenger = new WeakReferenceMessenger();
        var first = new TransactionVM { Name = "First", OccurredOn = new DateTime(2026, 8, 10, 8, 0, 0) };
        var second = new TransactionVM { Name = "Second", OccurredOn = new DateTime(2026, 8, 10, 9, 0, 0) };
        using var vm = new TransactionBulkQueueVM(messenger);
        messenger.Send(new TransactionBulkQueueResetMessage(true, [first, second], null), TransactionPopupMessageToken.Default);

        first.OccurredOn = new DateTime(2026, 8, 10, 10, 0, 0);

        var group = Assert.IsAssignableFrom<CollectionViewGroup>(Assert.Single(vm.QueuedTransactionsView.Groups!));
        Assert.Same(first, group.Items[0]);
        Assert.Same(first, vm.SelectedQueuedTransaction);
    }

    [Fact]
    public void Adding_and_switching_preserves_exact_instances()
    {
        var messenger = new WeakReferenceMessenger();
        var loads = new List<TransactionVM>();
        var recipient = new object();
        messenger.Register<object, TransactionLoadRequestedMessage, TransactionPopupMessageToken>(recipient, TransactionPopupMessageToken.Default,
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

        messenger.Send(new TransactionBulkQueueResetMessage(true, [first], account), TransactionPopupMessageToken.Default);
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

    [Fact]
    public void Adding_queue_item_uses_current_local_date_and_time()
    {
        var messenger = new WeakReferenceMessenger();
        using var vm = new TransactionBulkQueueVM(messenger);
        messenger.Send(new TransactionBulkQueueResetMessage(
            true, [], new AccountVM { Id = 1 }), TransactionPopupMessageToken.Default);
        var before = DateTime.Now;

        vm.AddQueuedTransactionCommand.Execute(null);

        var after = DateTime.Now;
        var occurredOn = Assert.Single(vm.QueuedTransactions).OccurredOn;
        Assert.InRange(occurredOn, before, after);
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
        messenger.Register<object, TransactionBulkQueueStateChangedMessage, TransactionPopupMessageToken>(recipient, TransactionPopupMessageToken.Default,
            (_, message) => state = message);
        using var vm = new TransactionBulkQueueVM(messenger);

        messenger.Send(new TransactionBulkQueueResetMessage(
            true, [new TransactionVM { Name = name, Amount = amount }], null), TransactionPopupMessageToken.Default);

        Assert.Equal(expected, state!.HasChanges);
    }

    [Fact]
    public void Select_and_remove_requests_operate_on_exact_instances()
    {
        var messenger = new WeakReferenceMessenger();
        var first = new TransactionVM { Name = "First" };
        var second = new TransactionVM { Name = "Second" };
        using var vm = new TransactionBulkQueueVM(messenger);
        messenger.Send(new TransactionBulkQueueResetMessage(true, [first, second], null), TransactionPopupMessageToken.Default);

        messenger.Send(new TransactionBulkQueueSelectRequestedMessage(second), TransactionPopupMessageToken.Default);
        Assert.Same(second, vm.SelectedQueuedTransaction);

        messenger.Send(new TransactionBulkQueueRemoveRequestedMessage(second), TransactionPopupMessageToken.Default);
        Assert.DoesNotContain(second, vm.QueuedTransactions);
        Assert.Same(first, vm.SelectedQueuedTransaction);
    }

    [Fact]
    public void Selection_uses_reference_identity_for_equal_transactions()
    {
        var messenger = new WeakReferenceMessenger();
        var first = new TransactionVM { Name = "Same", Amount = 1m };
        var second = new TransactionVM { Name = "Same", Amount = 1m };
        using var vm = new TransactionBulkQueueVM(messenger);
        messenger.Send(new TransactionBulkQueueResetMessage(true, [first, second], null), TransactionPopupMessageToken.Default);

        vm.SelectedQueuedTransaction = first;
        vm.SelectedQueuedTransaction = second;

        Assert.Same(second, vm.SelectedQueuedTransaction);

        messenger.Send(new TransactionBulkQueueRemoveRequestedMessage(second), TransactionPopupMessageToken.Default);

        Assert.Single(vm.QueuedTransactions);
        Assert.Same(first, vm.QueuedTransactions[0]);
    }
}
