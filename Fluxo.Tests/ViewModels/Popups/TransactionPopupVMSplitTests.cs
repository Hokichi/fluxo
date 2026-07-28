using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMSplitTests
{
    [Fact]
    public void SelectSplit_saves_current_form_then_loads_root()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.NameText = "Edited child";

        vm.SelectSplitCommand.Execute(null);

        Assert.Equal("Edited child", child.Name);
        Assert.Equal("Root", vm.NameText);
        Assert.Null(vm.SelectedSplitTransaction);
    }

    [Fact]
    public void DeleteSplit_selects_parent_after_grandchild_removal()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.AmountText = 100m;
        vm.AddSplitCommand.Execute(child);
        var grandchild = child.ChildTransactions.Single();

        vm.DeleteSplitCommand.Execute(grandchild);

        Assert.Equal("New Sub-transaction", vm.SelectedSplitTransaction!.Name);
        Assert.Empty(child.ChildTransactions);
    }

    [Fact]
    public void SplitCommands_keep_root_remaining_amount_in_sync()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);

        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal(0m, vm.SplitAmountRemaining);
        Assert.False(vm.HasSplitAmountOverflow);

        vm.ResetSplitCommand.Execute(null);

        Assert.Equal(100m, vm.SplitAmountRemaining);
    }

    [Fact]
    public void SplitOverflow_UsesPositiveOverflowAmountAndBlocksSave()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.SelectSplitCommand.Execute(child);
        vm.AmountText = 101m;
        vm.SelectSplitCommand.Execute(null);

        Assert.True(vm.HasSplitAmountOverflow);
        Assert.Equal(1m, vm.SplitAmountRemaining);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void ZeroAmountSplitParent_DisablesAddingChild()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var parent = vm.PendingTransaction.ChildTransactions.Single();

        Assert.False(parent.CanAddChildTransaction);
    }

    [Fact]
    public void InvalidGrandchild_BlocksSaveWhenRootIsSelected()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var parent = vm.PendingTransaction.ChildTransactions.Single();
        vm.AmountText = 100m;
        vm.AddSplit(parent);
        parent.ChildTransactions.Single().Name = string.Empty;
        vm.SelectSplitCommand.Execute(null);

        Assert.False(vm.CanSave);
    }

    [Fact]
    public void SplitEqually_does_not_overwrite_another_selected_child_with_stale_form_values()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);

        vm.SplitEquallyCommand.Execute(null);
        vm.SelectSplitCommand.Execute(vm.PendingTransaction.ChildTransactions[0]);

        Assert.Equal([50m, 50m], vm.PendingTransaction.ChildTransactions.Select(child => child.Amount));
    }

    [Fact]
    public void DeleteSplit_notifies_root_display_when_root_is_already_selected()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.AmountText = 100m;
        vm.SelectSplitCommand.Execute(null);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);

        vm.DeleteSplitCommand.Execute(child);

        Assert.Empty(vm.PendingTransaction.ChildTransactions);
        Assert.Contains(nameof(vm.SplitAmountRemaining), changes);
        Assert.Contains(nameof(vm.HasSplitTransactions), changes);
    }

    [Fact]
    public void AddSplit_keeps_placeholder_hidden_when_root_is_valid()
    {
        var vm = CreateVm();

        vm.AddSplitCommand.Execute(null);

        Assert.False(vm.ShowInvalidSplitPlaceholder);
    }

    [Fact]
    public void AddSplit_shows_placeholder_when_root_is_invalid()
    {
        var vm = CreateVm();
        vm.NameText = string.Empty;

        vm.AddSplitCommand.Execute(null);

        Assert.True(vm.ShowInvalidSplitPlaceholder);
    }

    [Fact]
    public void Non_expense_shows_split_unavailable_placeholder()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);

        vm.IsExpense = false;

        Assert.True(vm.ShowInvalidSplitPlaceholder);
        Assert.False(vm.CanModifySplitTree);
    }

    [Fact]
    public void Split_root_and_parent_child_clear_classification_but_leaf_child_can_edit_it()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        child.Tag = new TagVM { Id = 1, Name = "General" };
        child.ExpenseCategory = ExpenseCategory.Wants;

        vm.SelectSplitCommand.Execute(null);

        Assert.Null(vm.PendingTransaction.Tag);
        Assert.Null(vm.PendingTransaction.ExpenseCategory);
        Assert.False(vm.CanEditTags);
        Assert.False(vm.CanEditCategory);

        vm.SelectSplitCommand.Execute(child);

        Assert.True(vm.CanEditTags);
        Assert.True(vm.CanEditCategory);
        Assert.False(vm.CanEditAccount);
        Assert.False(vm.CanEditDate);

        vm.SelectSplitCommand.Execute(null);

        Assert.True(vm.CanEditAccount);
        Assert.True(vm.CanEditDate);

        vm.SelectSplitCommand.Execute(child);
        vm.AmountText = 100m;
        vm.AddSplitCommand.Execute(child);
        var grandchild = child.ChildTransactions.Single();
        vm.SelectSplitCommand.Execute(null);
        vm.SelectSplitCommand.Execute(grandchild);

        Assert.True(vm.CanEditTags);
        Assert.True(vm.CanEditCategory);
    }

    [Fact]
    public void Split_commands_require_enough_children()
    {
        var vm = CreateVm();

        Assert.False(vm.SplitEquallyCommand.CanExecute(null));
        Assert.False(vm.ResetSplitCommand.CanExecute(null));

        vm.AddSplitCommand.Execute(null);

        Assert.False(vm.SplitEquallyCommand.CanExecute(null));
        Assert.True(vm.ResetSplitCommand.CanExecute(null));

        vm.AddSplitCommand.Execute(null);

        Assert.True(vm.SplitEquallyCommand.CanExecute(null));
    }

    [Fact]
    public void ClearSplitTransactions_removes_tree_and_returns_to_history()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);
        vm.SelectedSidePanel = TransactionPopupSidePanel.Split;

        vm.ClearSplitTransactions();

        Assert.Empty(vm.PendingTransaction.ChildTransactions);
        Assert.Null(vm.SelectedSplitTransaction);
        Assert.Equal(TransactionPopupSidePanel.History, vm.SelectedSidePanel);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Returning_from_goal_or_repayment_to_expense_or_income_clears_amount(
        bool startsAsGoal,
        bool returnsToExpense)
    {
        var vm = CreateVm();
        if (startsAsGoal)
            vm.IsGoal = true;
        else
            vm.IsRepayment = true;
        vm.AmountText = 100m;

        if (returnsToExpense)
            vm.IsExpense = true;
        else
            vm.IsIncome = true;

        Assert.Equal(0m, vm.AmountText);
    }

    [Fact]
    public void Processing_hides_side_panel_and_disables_type_and_split_changes()
    {
        var vm = CreateVm();
        vm.InitializeGoalProcessing([new SavingGoalVM { Id = 1, Name = "Goal" }]);

        Assert.False(vm.ShowSidePanel);
        Assert.False(vm.CanChangeTransactionType);
        Assert.False(vm.CanUseSplit);
        Assert.False(vm.CanModifySplitTree);
    }

    [Fact]
    public void SplitEqually_refreshes_root_child_total()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);

        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal(100m, vm.PendingTransaction.ChildAmountTotal);
    }

    [Fact]
    public void Root_account_and_date_changes_update_all_split_descendants()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.AmountText = 100m;
        vm.AddSplitCommand.Execute(child);
        var grandchild = child.ChildTransactions.Single();

        vm.SelectSplitCommand.Execute(null);
        vm.SelectedAccount = new AccountVM { Id = 2, Name = "Savings", AccountType = AccountType.Checking };
        vm.SelectedDate = new DateTime(2026, 7, 28);

        Assert.All(new[] { child, grandchild }, node =>
        {
            Assert.Equal(2, node.SourceAccountId);
            Assert.Equal(new DateTime(2026, 7, 28), node.OccurredOn);
        });
    }

    [Fact]
    public void BalanceUpdate_SummarizesTerminalLeaves()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        vm.PendingTransaction.ChildTransactions.Clear();
        var needs = new TransactionVM
        {
            Type = TransactionType.Expense,
            Account = vm.PendingTransaction.Account,
            Amount = 30m,
            ExpenseCategory = ExpenseCategory.Needs,
            Tag = new TagVM { Id = 1, Name = "Food" }
        };
        var parent = new TransactionVM
        {
            Type = TransactionType.Expense,
            Account = vm.PendingTransaction.Account,
            Amount = 70m
        };
        parent.ChildTransactions.Add(new TransactionVM
        {
            Type = TransactionType.Expense,
            Account = vm.PendingTransaction.Account,
            Amount = 70m,
            ExpenseCategory = ExpenseCategory.Wants,
            Tag = new TagVM { Id = 2, Name = "Travel" }
        });
        vm.PendingTransaction.ChildTransactions.Add(needs);
        vm.PendingTransaction.ChildTransactions.Add(parent);

        vm.SelectSplitCommand.Execute(null);

        Assert.Equal(ExpenseCategory.Needs, needs.ExpenseCategory);
        Assert.Equal(1, needs.Tag?.Id);
        Assert.Equal(400m, vm.BalanceUpdateAccountToBe);
        Assert.Equal(
        [
            ("Needs", 0m, 30m),
            ("Wants", 0m, 70m)
        ],
        vm.CategoryBalanceUpdates.Select(item => (item.Name, item.CurrentAmount, item.NewAmount)));
        Assert.Equal(
        [
            ("Food", 0m, 30m),
            ("Travel", 0m, 70m)
        ],
        vm.TagBalanceUpdates.Select(item => (item.Name, item.CurrentAmount, item.NewAmount)));

        vm.SelectSplitCommand.Execute(needs);

        Assert.Equal(400m, vm.BalanceUpdateAccountToBe);
        Assert.Equal(
        [
            ("Needs", 0m, 30m),
            ("Wants", 0m, 70m)
        ],
        vm.CategoryBalanceUpdates.Select(item => (item.Name, item.CurrentAmount, item.NewAmount)));
    }

    [Fact]
    public async Task Edit_mode_allows_adding_a_grandchild()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Tag>>([]));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Transaction>>(
        [new Transaction
        {
            Id = 2,
            ParentTransactionId = 1,
            Type = TransactionType.Expense,
            SourceAccountId = 1,
            Account = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking },
            Name = "Child",
            Amount = 100m,
            OccurredOn = new DateTime(2026, 7, 27)
        }]));
        var vm = new TransactionPopupVM(appData, new WeakReferenceMessenger());
        vm.InitializeView(new TransactionVM
        {
            Id = 1,
            Type = TransactionType.Expense,
            SourceAccountId = 1,
            Account = new AccountVM { Id = 1, Name = "Checking", AccountType = AccountType.Checking },
            Name = "Root",
            Amount = 100m,
            OccurredOn = new DateTime(2026, 7, 27)
        });

        await vm.BeginEditingViewedTransactionAsync();
        var child = vm.PendingTransaction.ChildTransactions.Single();
        Assert.True(vm.AddSplitCommand.CanExecute(child));
        vm.AddSplitCommand.Execute(child);

        Assert.Single(child.ChildTransactions);
    }

    private static TransactionPopupVM CreateVm()
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

        var vm = new TransactionPopupVM(appData, new WeakReferenceMessenger())
        {
            IsExpense = true,
            SelectedAccount = new AccountVM { Id = 1, Name = "Checking", AccountType = AccountType.Checking, Balance = 500m },
            SelectedTag = new TagVM { Id = 1, Name = "General" },
            NameText = "Root",
            AmountText = 100m,
            SelectedDate = new DateTime(2026, 7, 27)
        };
        return vm;
    }
}
