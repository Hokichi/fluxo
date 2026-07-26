using CommunityToolkit.Mvvm.Messaging;
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
    public void Add_split_selects_and_loads_new_child()
    {
        var viewModel = CreateViewModel();

        viewModel.AddSplit(null);

        var child = Assert.Single(viewModel.SplitTransactions);
        Assert.Equal("New Sub-transaction", child.Name);
        Assert.Equal(0m, child.Amount);
        Assert.Same(child, viewModel.SelectedSplitTransaction);
        Assert.Equal(child.Name, viewModel.NameText);
    }

    [Fact]
    public void Add_split_moves_parent_metadata_to_the_new_leaf()
    {
        var viewModel = CreateViewModel();
        var tag = new TagVM { Id = 1, Name = "Food" };
        viewModel.SelectedTag = tag;

        viewModel.AddSplit(null);

        var child = Assert.Single(viewModel.SplitTransactions);
        Assert.Null(viewModel.PendingTransaction.ExpenseCategory);
        Assert.Null(viewModel.PendingTransaction.Tag);
        Assert.Same(tag, child.Tag);
        Assert.Equal(ExpenseCategory.Needs, child.ExpenseCategory);
    }

    [Fact]
    public void Split_equally_rounds_initial_children_to_integers_and_assigns_remainder_to_last()
    {
        var viewModel = CreateViewModel();
        viewModel.AmountText = 100m;
        viewModel.AddSplit(null);
        viewModel.ReturnToSplitRoot();
        viewModel.AddSplit(null);
        viewModel.ReturnToSplitRoot();
        viewModel.AddSplit(null);

        viewModel.ReturnToSplitRoot();
        viewModel.SplitEqually();

        Assert.Equal([33m, 33m, 34m], viewModel.SplitTransactions.Select(child => child.Amount));
    }

    [Fact]
    public void Overflow_disables_add_split_for_the_overflowing_parent()
    {
        var viewModel = CreateViewModel();
        viewModel.AmountText = 10m;
        viewModel.AddSplit(null);
        viewModel.SelectedSplitTransaction!.Amount = 11m;

        Assert.True(viewModel.HasSplitAmountOverflow);
        Assert.False(viewModel.CanAddSplit(null));
    }

    [Fact]
    public void Return_to_top_is_disabled_when_root_is_selected()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanReturnToSplitRoot);

        viewModel.AddSplit(null);

        Assert.True(viewModel.CanReturnToSplitRoot);
        viewModel.ReturnToSplitRoot();
        Assert.False(viewModel.CanReturnToSplitRoot);
    }

    [Fact]
    public void Selecting_split_notifies_side_panel_visibility()
    {
        var viewModel = CreateViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;

        Assert.Contains(nameof(TransactionPopupVM.ShowHistoryPanel), changedProperties);
        Assert.Contains(nameof(TransactionPopupVM.ShowSplitPanel), changedProperties);
    }

    [Fact]
    public void Invalid_selected_child_disables_only_its_parent_add()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var parent = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 100m;
        viewModel.AddSplit(parent);
        viewModel.NameText = string.Empty;

        Assert.False(viewModel.CanAddSplit(parent));
        Assert.True(viewModel.CanAddSplit(null));
    }

    [Fact]
    public void Invalid_root_disables_root_add_split()
    {
        var viewModel = CreateViewModel();
        viewModel.NameText = string.Empty;

        Assert.False(viewModel.CanAddSplit(null));
        Assert.False(viewModel.AddSplitCommand.CanExecute(null));
    }

    [Fact]
    public void Invalid_stored_root_disables_root_add_split_after_selecting_child()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var child = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 100m;
        viewModel.ReturnToSplitRoot();
        viewModel.NameText = string.Empty;
        viewModel.SelectSplitTransaction(child);

        Assert.False(viewModel.CanAddSplit(null));
    }

    [Fact]
    public void Add_split_does_not_require_a_tag()
    {
        var viewModel = CreateViewModel();
        viewModel.IsExpense = true;
        viewModel.SelectedTag = null;

        Assert.True(viewModel.CanAddSplit(null));
        Assert.True(viewModel.AddSplitCommand.CanExecute(null));
    }

    [Fact]
    public void Split_equally_requires_at_least_two_children_for_the_target_level()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.SplitEquallyCommand.CanExecute(null));

        viewModel.AddSplit(null);
        viewModel.ReturnToSplitRoot();
        Assert.False(viewModel.SplitEquallyCommand.CanExecute(null));
        Assert.True(viewModel.ResetSplitCommand.CanExecute(null));

        viewModel.AddSplit(null);
        viewModel.ReturnToSplitRoot();
        Assert.True(viewModel.SplitEquallyCommand.CanExecute(null));
    }

    [Fact]
    public void Nested_split_commands_use_their_parent_parameter()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var parent = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 100m;

        viewModel.AddSplit(parent);
        viewModel.AmountText = 25m;
        viewModel.SelectSplitTransaction(parent);
        Assert.False(viewModel.SplitEquallyCommand.CanExecute(parent));
        Assert.True(viewModel.ResetSplitCommand.CanExecute(parent));

        viewModel.AddSplit(parent);
        viewModel.SelectSplitTransaction(parent);
        Assert.True(viewModel.SplitEquallyCommand.CanExecute(parent));
    }

    [Fact]
    public void Typing_child_amount_updates_root_total_immediately()
    {
        var viewModel = CreateViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.AddSplit(null);

        viewModel.AmountText = 40m;

        Assert.Equal(40m, viewModel.SplitAmount);
        Assert.Contains(nameof(TransactionPopupVM.SplitAmount), changed);
    }

    [Fact]
    public void Typing_grandchild_amount_updates_parent_total_immediately()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var parent = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 100m;
        viewModel.AddSplit(parent);

        viewModel.AmountText = 25m;

        Assert.Equal(25m, parent.ChildAmountTotal);
    }

    [Fact]
    public void Can_save_requires_root_children_to_match_root_amount()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        viewModel.AmountText = 50m;

        Assert.False(viewModel.CanSave);

        viewModel.AmountText = 100m;
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public void Can_save_requires_nested_children_to_match_parent_amount()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var parent = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 100m;
        viewModel.AddSplit(parent);
        viewModel.AmountText = 50m;

        Assert.False(viewModel.CanSave);

        viewModel.AmountText = 100m;
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public void Removing_grandchild_loads_its_parent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var parent = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 100m;
        viewModel.AddSplit(parent);
        var grandchild = Assert.Single(parent.ChildTransactions);

        viewModel.DeleteSplit(grandchild);

        Assert.Same(parent, viewModel.SelectedSplitTransaction);
        Assert.Equal(parent.Name, viewModel.NameText);
    }

    [Fact]
    public void Removing_root_child_loads_root()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var child = Assert.Single(viewModel.SplitTransactions);

        viewModel.DeleteSplit(child);

        Assert.Null(viewModel.SelectedSplitTransaction);
        Assert.Equal("Root", viewModel.NameText);
    }

    [Fact]
    public void Side_panel_tabs_are_mutually_exclusive()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedSidePanel = TransactionPopupSidePanel.Pinned;
        Assert.True(viewModel.ShowPinnedPanel);
        Assert.False(viewModel.ShowHistoryPanel);
        Assert.False(viewModel.ShowSplitPanel);

        viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
        Assert.False(viewModel.ShowPinnedPanel);
        Assert.False(viewModel.ShowHistoryPanel);
        Assert.True(viewModel.ShowSplitPanel);
    }

    [Fact]
    public void Return_label_uses_root_transaction_name()
    {
        var viewModel = CreateViewModel();
        viewModel.NameText = "Groceries";

        Assert.Equal("Return to Groceries", viewModel.ReturnToSplitRootText);
    }

    [Fact]
    public void Selecting_current_split_is_a_noop()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var child = Assert.Single(viewModel.SplitTransactions);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        var changed = viewModel.SelectSplitTransaction(child);

        Assert.False(changed);
        Assert.Empty(changedProperties);
        Assert.Same(child, viewModel.SelectedSplitTransaction);
    }

    private static TransactionPopupVM CreateViewModel()
    {
        var appData = Substitute.For<IAppDataService>();
        var account = new AccountVM
        {
            Id = 1,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 10_000m,
            IsEnabled = true,
            IsDefault = true
        };
        var tag = new TagVM { Id = 1, Name = "General", HexCode = "#22C55E" };
        var viewModel = new TransactionPopupVM(appData, new WeakReferenceMessenger());
        viewModel.ConfigureCatalogs([account], [tag], []);
        viewModel.NameText = "Root";
        viewModel.AmountText = 100m;
        viewModel.SelectedAccount = account;
        viewModel.SelectedTag = tag;
        return viewModel;
    }
}
