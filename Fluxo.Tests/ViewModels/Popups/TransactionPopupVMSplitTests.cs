using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Tests.Helpers.Popups;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMSplitTests
{
    [Fact]
    public void Switching_between_child_and_root_saves_exact_instances()
    {
        var peers = CreatePeers();
        peers.Splits.AddSplitCommand.Execute(null);
        var root = peers.Splits.RootTransaction!;
        var child = root.ChildTransactions.Single();
        peers.Popup.NameText = "Edited child";
        peers.Popup.AmountText = 100m;

        peers.Splits.SelectSplitCommand.Execute(null);

        Assert.Same(root, peers.Popup.PendingTransaction);
        Assert.Equal("Edited child", child.Name);
        Assert.Equal("Root", peers.Popup.NameText);
    }

    [Fact]
    public void Invalid_split_updates_root_and_blocks_save_in_real_time()
    {
        var peers = CreatePeers();
        peers.Splits.AddSplitCommand.Execute(null);
        peers.Popup.NameText = "Child";
        peers.Popup.AmountText = 40m;

        Assert.False(peers.Splits.RootTransaction!.IsValid);
        Assert.False(peers.Popup.CanPersist);
    }

    [Fact]
    public void Switching_split_expense_to_goal_update_shows_unavailable_placeholder()
    {
        var peers = CreatePeers();
        peers.Splits.AddSplitCommand.Execute(null);

        peers.Popup.IsGoal = true;

        Assert.True(peers.Popup.ShowSplitPanel);
        Assert.True(peers.Splits.ShowInvalidSplitPlaceholder);
    }

    [Fact]
    public void Root_account_and_date_changes_update_all_descendants()
    {
        var peers = CreatePeers();
        peers.Splits.AddSplitCommand.Execute(null);
        var root = peers.Splits.RootTransaction!;
        var child = root.ChildTransactions.Single();
        peers.Popup.AmountText = 100m;
        peers.Splits.AddSplitCommand.Execute(child);
        var grandchild = child.ChildTransactions.Single();
        peers.Splits.SelectSplitCommand.Execute(null);

        peers.Popup.SelectedAccount = new AccountVM
            { Id = 2, Name = "Savings", AccountType = AccountType.Checking };
        peers.Popup.SelectedDate = new DateTime(2026, 7, 28);

        Assert.All(new[] { child, grandchild }, node =>
        {
            Assert.Equal(2, node.SourceAccountId);
            Assert.Equal(new DateTime(2026, 7, 28), node.OccurredOn);
        });
    }

    [Fact]
    public void Balance_update_uses_terminal_leaves_while_child_is_selected()
    {
        var peers = CreatePeers();
        var root = peers.Splits.RootTransaction!;
        root.ChildTransactions.Clear();
        var needs = Leaf(root.Account, 30m, ExpenseCategory.Needs, 1, "Food");
        var parent = new TransactionVM
        {
            Type = TransactionType.Expense,
            SourceAccountId = 1,
            Account = root.Account,
            Name = "Parent",
            Amount = 70m
        };
        parent.ChildTransactions.Add(Leaf(root.Account, 70m, ExpenseCategory.Wants, 2, "Travel"));
        root.ChildTransactions.Add(needs);
        root.ChildTransactions.Add(parent);
        peers.Popup.SelectedSidePanel = TransactionPopupSidePanel.Split;

        peers.Splits.SelectSplitCommand.Execute(needs);

        Assert.Equal(400m, peers.Popup.BalanceUpdateAccountToBe);
        Assert.Equal(
        [
            ("Needs", 0m, 30m),
            ("Wants", 0m, 70m)
        ], peers.Popup.CategoryBalanceUpdates.Select(item =>
            (item.Name, item.CurrentAmount, item.NewAmount)));
    }

    [Fact]
    public async Task Edit_mode_loads_tree_in_popup_and_allows_grandchild()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(
            [
                new()
                {
                    Id = 2,
                    ParentTransactionId = 1,
                    Type = TransactionType.Expense,
                    SourceAccountId = 1,
                    Account = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking },
                    Name = "Child",
                    Amount = 100m,
                    OccurredOn = new DateTime(2026, 7, 27)
                }
            ]));
        var messenger = new WeakReferenceMessenger();
        var popup = new TransactionPopupVM(appData, messenger);
        using var bulk = new TransactionBulkQueueVM(messenger);
        using var splits = new TransactionSplitsVM(messenger);
        popup.InitializeView(Root());

        await popup.BeginEditingViewedTransactionAsync();
        var child = splits.RootTransaction!.ChildTransactions.Single();
        splits.AddSplitCommand.Execute(child);

        Assert.Single(child.ChildTransactions);
    }

    private static TransactionPopupPeers CreatePeers()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(new Account
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                Balance = 500m
            }));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        var messenger = new WeakReferenceMessenger();
        var popup = new TransactionPopupVM(appData, messenger);
        var bulk = new TransactionBulkQueueVM(messenger);
        var splits = new TransactionSplitsVM(messenger);
        var root = Root();
        root.Account.Balance = 500m;
        popup.LoadTransaction(root);
        popup.SelectedTag = new TagVM { Id = 1, Name = "General" };
        popup.SelectedSidePanel = TransactionPopupSidePanel.Split;
        return new TransactionPopupPeers(popup, bulk, splits);
    }

    private static TransactionVM Root() => new()
    {
        Id = 1,
        Type = TransactionType.Expense,
        SourceAccountId = 1,
        Account = new AccountVM
            { Id = 1, Name = "Checking", AccountType = AccountType.Checking },
        Name = "Root",
        Amount = 100m,
        OccurredOn = new DateTime(2026, 7, 27)
    };

    private static TransactionVM Leaf(
        AccountVM account,
        decimal amount,
        ExpenseCategory category,
        int tagId,
        string tagName) => new()
    {
        Type = TransactionType.Expense,
        SourceAccountId = account.Id,
        Account = account,
        Name = tagName,
        Amount = amount,
        ExpenseCategory = category,
        Tag = new TagVM { Id = tagId, Name = tagName }
    };
}
